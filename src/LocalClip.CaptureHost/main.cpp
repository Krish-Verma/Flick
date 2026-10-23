#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <psapi.h>
#include <dwmapi.h>
#include <iostream>
#include <string>
#include <vector>
#include <mutex>
#include <thread>
#include <atomic>
#include <chrono>
#include <sstream>
#include <iomanip>
#include <filesystem>
#include <system_error>

#include "obs.h"
#include "json.hpp"
#include "encoder_policy.hpp"

using json = nlohmann::json;

// Synchronization & Protocol Output
static std::mutex g_io_mutex;
static std::mutex g_state_mutex;

static void send_json_line(const json &j) {
    std::lock_guard<std::mutex> lock(g_io_mutex);
    std::string s = j.dump();
    std::cout << s << "\n";
    std::cout.flush();
}

static void send_event(const std::string &event_name, const json &data) {
    json j;
    j["event"] = event_name;
    j["data"] = data;
    send_json_line(j);
}

static void send_response(const std::string &req_id, bool ok, const json &result, const std::string &err = "") {
    json j;
    j["id"] = req_id;
    j["ok"] = ok;
    if (ok) {
        j["result"] = result;
    } else {
        j["error"] = err;
    }
    send_json_line(j);
}

// Redirect OBS logs to stderr so stdout is 100% clean JSON
static void obs_log_redirect(int lvl, const char *msg, va_list args, void *p) {
    char buf[4096];
    vsnprintf(buf, sizeof(buf), msg, args);
    std::cerr << "[OBS " << lvl << "] " << buf << std::endl;
}

// Capture State
enum class CaptureState {
    Off,
    Starting,
    Buffering,
    Armed,
    Stopping,
    Paused,
    Faulted
};

static const char* state_to_string(CaptureState s) {
    switch (s) {
        case CaptureState::Off: return "Off";
        case CaptureState::Starting: return "Starting";
        case CaptureState::Buffering: return "Buffering";
        case CaptureState::Armed: return "Armed";
        case CaptureState::Stopping: return "Stopping";
        case CaptureState::Paused: return "Paused";
        case CaptureState::Faulted: return "Faulted";
        default: return "Unknown";
    }
}

struct ActiveCapture {
    CaptureState state = CaptureState::Off;
    std::string target_type; // "monitor" or "window"
    int monitor_index = 0;
    HWND window_hwnd = nullptr;
    int fps = 30;
    int width = 1920;
    int height = 1080;
    int bitrate_kbps = 15000;
    std::string encoder_id;
    std::string encoder_name;
    bool software_fallback = false;
    int duration_sec = 30;
    int buffer_mb = 256;
    std::string output_dir;
    std::string audio_mode; // "separated" or "system_fallback"
    bool enable_mic = false;
    std::string mic_device_id = "default";
    std::string system_device_id = "default";

    std::chrono::steady_clock::time_point start_time;
    std::atomic<bool> is_armed{false};

    obs_source_t *video_source = nullptr;
    obs_scene_t *video_scene = nullptr;
    obs_source_t *sys_audio_source = nullptr;
    obs_source_t *mic_audio_source = nullptr;
    obs_encoder_t *video_encoder = nullptr;
    std::vector<obs_encoder_t*> audio_encoders;
    obs_output_t *replay_output = nullptr;

    // Track metadata
    std::vector<std::string> track_names;

    // Pending save requests
    std::string pending_save_id;
    std::string pending_save_prefix;
    std::chrono::steady_clock::time_point pending_save_start;
    std::filesystem::file_time_type pending_save_walltime;
    std::atomic<bool> save_in_progress{false};
};

static ActiveCapture g_cap;
static std::atomic<bool> g_obs_initialized{false};

static std::filesystem::path runtime_bin_directory() {
    wchar_t file_name[32768] = {};
    DWORD len = GetModuleFileNameW(nullptr, file_name, 32768);
    if (!len || len >= 32768) return {};
    return std::filesystem::path(file_name).parent_path();
}

// Forward declaration
static void internal_stop_capture(const std::string &reason);
static void cleanup_capture_resources();

// Replay buffer callbacks: only the current OBS output and save request can
// publish a clip. Never pick the newest file from a directory (stale-clip bug).
static void on_replay_saved(void *data, calldata_t *cd) {
    obs_output_t *output = reinterpret_cast<obs_output_t*>(data);
    std::string save_id;
    std::string out_dir;
    std::vector<std::string> tracks;
    std::filesystem::file_time_type requested_at;
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        if (!g_cap.save_in_progress || !g_cap.replay_output ||
            g_cap.replay_output != output) return;
        save_id = g_cap.pending_save_id;
        out_dir = g_cap.output_dir;
        requested_at = g_cap.pending_save_walltime;
        tracks = g_cap.track_names;
    }

    auto reject = [&](const char *message) {
        {
            std::lock_guard<std::mutex> lock(g_state_mutex);
            if (g_cap.replay_output == output && g_cap.pending_save_id == save_id)
                g_cap.save_in_progress = false;
        }
        send_event("save_failed", {{"reason", message}, {"save_id", save_id}});
    };

    // The OBS saved signal may omit the path. Query THIS output, not the
    // filesystem, and reject an absent/stale/unrelated result.
    std::string saved_path;
    proc_handler_t *ph = obs_output_get_proc_handler(output);
    if (ph) {
        calldata_t reply;
        calldata_init(&reply);
        if (proc_handler_call(ph, "get_last_replay", &reply)) {
            const char *p = nullptr;
            if (calldata_get_string(&reply, "path", &p) && p && *p)
                saved_path = p;
        }
        calldata_free(&reply);
    }
    if (saved_path.empty()) {
        reject("OBS did not supply the path for this replay; refusing to guess");
        return;
    }

    std::error_code ec;
    auto source = std::filesystem::u8path(saved_path);
    auto directory = std::filesystem::u8path(out_dir);
    if (source.extension() != L".mkv" ||
        !std::filesystem::is_regular_file(source, ec) || ec ||
        !std::filesystem::equivalent(source.parent_path(), directory, ec) || ec) {
        reject("Replay file is missing or outside the configured originals folder");
        return;
    }

    const auto modified = std::filesystem::last_write_time(source, ec);
    if (ec || modified + std::chrono::seconds(4) < requested_at ||
        std::filesystem::file_size(source, ec) == 0 || ec) {
        reject("Replay path points to an empty or stale file");
        return;
    }

    // Each request gets a distinct immutable name even when multiple saves
    // occur within the same wall-clock second. MoveFileW never replaces.
    std::string safe_id;
    for (unsigned char c : save_id) {
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '-' || c == '_')
            safe_id.push_back(static_cast<char>(c));
    }
    if (safe_id.empty()) {
        reject("Replay save ID was invalid");
        return;
    }
    auto target = directory / std::filesystem::u8path("Flick_" + safe_id + ".mkv");
    if (!MoveFileW(source.c_str(), target.c_str())) {
        reject("Could not finalize the unique replay file without overwriting another clip");
        return;
    }

    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        if (g_cap.replay_output == output && g_cap.pending_save_id == save_id)
            g_cap.save_in_progress = false;
    }
    send_event("replay_saved", {
        {"save_id", save_id},
        {"path", target.u8string()},
        {"tracks", tracks}
    });
}

// Window enumeration helpers
struct WindowInfo {
    uint64_t hwnd = 0;
    std::string title;
    DWORD pid = 0;
    std::string exe_name;
    int width = 0;
    int height = 0;
};

static BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam) {
    auto *list = reinterpret_cast<std::vector<WindowInfo>*>(lParam);
    if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return TRUE;

    int cloaked = 0;
    if (SUCCEEDED(DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && cloaked) {
        return TRUE;
    }

    LONG_PTR exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
    if (exStyle & WS_EX_TOOLWINDOW) return TRUE;

    int titleLen = GetWindowTextLengthW(hwnd);
    if (titleLen <= 0) return TRUE;

    RECT rc;
    GetWindowRect(hwnd, &rc);
    int w = rc.right - rc.left;
    int h = rc.bottom - rc.top;
    if (w < 100 || h < 100) return TRUE;

    std::vector<wchar_t> wtitle(titleLen + 1);
    GetWindowTextW(hwnd, wtitle.data(), titleLen + 1);

    wchar_t clsName[256];
    GetClassNameW(hwnd, clsName, 256);
    if (wcscmp(clsName, L"Progman") == 0 || wcscmp(clsName, L"WorkerW") == 0 || wcscmp(clsName, L"Shell_TrayWnd") == 0) {
        return TRUE;
    }

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);

    std::string exeName;
    HANDLE hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (hProc) {
        wchar_t path[MAX_PATH];
        DWORD size = MAX_PATH;
        if (QueryFullProcessImageNameW(hProc, 0, path, &size)) {
            wchar_t *slash = wcsrchr(path, L'\\');
            std::wstring wexe = (slash ? slash + 1 : path);
            char cexe[256];
            WideCharToMultiByte(CP_UTF8, 0, wexe.c_str(), -1, cexe, sizeof(cexe), nullptr, nullptr);
            exeName = cexe;
        }
        CloseHandle(hProc);
    }

    char ctitle[512];
    WideCharToMultiByte(CP_UTF8, 0, wtitle.data(), -1, ctitle, sizeof(ctitle), nullptr, nullptr);

    WindowInfo info;
    info.hwnd = reinterpret_cast<uint64_t>(hwnd);
    info.title = ctitle;
    info.pid = pid;
    info.exe_name = exeName;
    info.width = w;
    info.height = h;

    list->push_back(info);
    return TRUE;
}

static void enumerate_all_windows(std::vector<WindowInfo> &wins) {
    HDESK hdesk = OpenDesktopW(L"default", 0, FALSE, DESKTOP_READOBJECTS | DESKTOP_ENUMERATE);
    if (hdesk) {
        EnumDesktopWindows(hdesk, EnumWindowsProc, reinterpret_cast<LPARAM>(&wins));
        CloseDesktop(hdesk);
    } else {
        EnumWindows(EnumWindowsProc, reinterpret_cast<LPARAM>(&wins));
    }
}

// Monitor enumeration helpers
struct MonitorInfo {
    int index = 0;
    std::string device_name;
    int width = 0;
    int height = 0;
    bool is_primary = false;
};

static BOOL CALLBACK EnumMonitorsProc(HMONITOR hMon, HDC hdc, LPRECT rc, LPARAM lParam) {
    auto *list = reinterpret_cast<std::vector<MonitorInfo>*>(lParam);
    MONITORINFOEXW mi;
    mi.cbSize = sizeof(mi);
    if (GetMonitorInfoW(hMon, &mi)) {
        MonitorInfo minfo;
        minfo.index = static_cast<int>(list->size());
        char cdev[128];
        WideCharToMultiByte(CP_UTF8, 0, mi.szDevice, -1, cdev, sizeof(cdev), nullptr, nullptr);
        minfo.device_name = cdev;
        minfo.width = mi.rcMonitor.right - mi.rcMonitor.left;
        minfo.height = mi.rcMonitor.bottom - mi.rcMonitor.top;
        minfo.is_primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
        list->push_back(minfo);
    }
    return TRUE;
}

// Initialization of OBS engine
static bool ensure_obs_init(int width, int height, int fps) {
    bool already_initialized = g_obs_initialized.load();
    if (already_initialized) {
        // A failed reset may have torn down the old video subsystem. Inspect
        // the live engine rather than trusting the last requested dimensions.
        struct obs_video_info current = {};
        if (obs_get_video_info(&current) && obs_get_video() &&
            current.base_width == static_cast<uint32_t>(width) && current.base_height == static_cast<uint32_t>(height) &&
            current.output_width == static_cast<uint32_t>(width) && current.output_height == static_cast<uint32_t>(height) &&
            current.fps_num == static_cast<uint32_t>(fps) && current.fps_den == 1)
            return true;
    }

    if (!already_initialized) {
        base_set_log_handler(obs_log_redirect, nullptr);
        if (!obs_startup("en-US", nullptr, nullptr)) return false;
    }

    struct obs_video_info ovi = {};
    ovi.graphics_module = "libobs-d3d11.dll";
    ovi.fps_num = static_cast<uint32_t>(fps);
    ovi.fps_den = 1;
    ovi.base_width = static_cast<uint32_t>(width);
    ovi.base_height = static_cast<uint32_t>(height);
    ovi.output_width = static_cast<uint32_t>(width);
    ovi.output_height = static_cast<uint32_t>(height);
    ovi.output_format = VIDEO_FORMAT_NV12;
    ovi.adapter = 0;
    ovi.gpu_conversion = true;
    ovi.colorspace = VIDEO_CS_709;
    ovi.range = VIDEO_RANGE_PARTIAL;
    ovi.scale_type = OBS_SCALE_BILINEAR;
    if (obs_reset_video(&ovi) != OBS_VIDEO_SUCCESS) {
        if (!already_initialized) obs_shutdown();
        return false;
    }
    if (already_initialized) return true;

    struct obs_audio_info oai = {};
    oai.samples_per_sec = 48000;
    oai.speakers = SPEAKERS_STEREO;
    if (!obs_reset_audio(&oai)) {
        obs_shutdown();
        return false;
    }
    const auto bin_dir = runtime_bin_directory();
    const auto runtime_dir = bin_dir.parent_path().parent_path();
    const char *mods[] = {
        "obs-ffmpeg", "obs-nvenc", "obs-qsv11", "obs-x264", "obs-outputs", "win-capture", "win-wasapi"
    };
    bool load_ok = true;
    for (const char *m : mods) {
        obs_module_t *mod = nullptr;
        auto dll = runtime_dir / "obs-plugins" / "64bit" / (std::string(m) + ".dll");
        auto data = runtime_dir / "data" / "obs-plugins" / m;
        std::string dll_utf8 = dll.u8string();
        std::string data_utf8 = data.u8string();
        int code = obs_open_module(&mod, dll_utf8.c_str(), data_utf8.c_str());
        if (code != MODULE_SUCCESS || !mod || !obs_init_module(mod)) {
            std::cerr << "Failed to load OBS plugin " << m << ", code=" << code << std::endl;
            if (std::string(m) != "obs-x264" && std::string(m) != "obs-nvenc" && std::string(m) != "obs-qsv11")
                load_ok = false;
        }
    }
    if (!load_ok) {
        obs_shutdown();
        return false;
    }
    obs_post_load_modules();
    g_obs_initialized = true;
    return true;
}

// Build a fresh pipeline for one registered encoder. The caller owns retry
// cleanup and never holds g_state_mutex while OBS starts/stops worker threads.
static bool start_capture_attempt(const json &params, const flick::capture::Encoder &encoder,
                                  flick::capture::Profile profile, bool &retryable, std::string &out_err) {
    retryable = false;
    if (!ensure_obs_init(profile.width, profile.height, profile.fps)) {
        retryable = true;
        out_err = "OBS could not configure the requested video profile";
        return false;
    }

    g_cap.target_type = params.value("target_type", "monitor");
    g_cap.monitor_index = params.value("monitor_index", 0);
    uint64_t hwnd_raw = params.value("window_hwnd", 0ULL);
    g_cap.window_hwnd = reinterpret_cast<HWND>(hwnd_raw);
    g_cap.fps = profile.fps;
    g_cap.width = profile.width;
    g_cap.height = profile.height;
    g_cap.bitrate_kbps = profile.bitrate_kbps;
    g_cap.encoder_id = encoder.id;
    g_cap.encoder_name = encoder.name;
    g_cap.software_fallback = encoder.software;
    g_cap.duration_sec = params.value("duration_sec", 30);
    g_cap.buffer_mb = params.value("buffer_mb", 256);
    g_cap.output_dir = params.value("output_dir", std::string{});
    g_cap.audio_mode = params.value("audio_mode", "system_fallback");
    g_cap.enable_mic = params.value("enable_mic", false);
    g_cap.mic_device_id = params.value("mic_device_id", "default");
    g_cap.system_device_id = params.value("system_device_id", "default");

    std::error_code directory_error;
    if (g_cap.output_dir.empty() ||
        (!std::filesystem::create_directories(std::filesystem::u8path(g_cap.output_dir), directory_error) && directory_error)) {
        out_err = "Unable to create originals directory";
        return false;
    }
    if (g_cap.target_type != "monitor" && g_cap.target_type != "window") {
        out_err = "Unknown capture target type";
        return false;
    }
    if (g_cap.target_type == "window" && !g_cap.window_hwnd) {
        out_err = "Window unavailable; refusing silent monitor fallback";
        return false;
    }

    // 1. Create Video Source
    if (g_cap.target_type == "window" && g_cap.window_hwnd) {
        HDESK hdesk = OpenDesktopW(L"default", 0, FALSE, GENERIC_ALL);
        if (hdesk) {
            SetThreadDesktop(hdesk);
            CloseDesktop(hdesk);
        }
        if (!IsWindow(g_cap.window_hwnd)) {
            out_err = "Target window handle is invalid or closed";
            return false;
        }
        obs_data_t *w_settings = obs_data_create();
        // OBS window capture expects title:class:exe format
        wchar_t wtitle[256] = {0};
        wchar_t wcls[256] = {0};
        GetWindowTextW(g_cap.window_hwnd, wtitle, 256);
        GetClassNameW(g_cap.window_hwnd, wcls, 256);
        DWORD pid = 0;
        GetWindowThreadProcessId(g_cap.window_hwnd, &pid);
        wchar_t wexe[MAX_PATH] = {0};
        HANDLE hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
        if (hProc) {
            DWORD sz = MAX_PATH;
            QueryFullProcessImageNameW(hProc, 0, wexe, &sz);
            CloseHandle(hProc);
        }
        wchar_t *slash = wcsrchr(wexe, L'\\');
        std::wstring exeOnly = (slash ? slash + 1 : wexe);

        std::wstringstream wss;
        wss << wtitle << L":" << wcls << L":" << exeOnly;
        const std::wstring window_key = wss.str();
        const int utf8_size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, window_key.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (utf8_size <= 0) {
            obs_data_release(w_settings);
            out_err = "Target window identity could not be encoded";
            return false;
        }
        std::string cwindow(static_cast<size_t>(utf8_size), '\0');
        if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, window_key.c_str(), -1, cwindow.data(), utf8_size, nullptr, nullptr)) {
            obs_data_release(w_settings);
            out_err = "Target window identity could not be encoded";
            return false;
        }
        obs_data_set_string(w_settings, "window", cwindow.c_str());
        obs_data_set_int(w_settings, "method", 2); // 2 = Windows Graphics Capture (non-injecting)
        obs_data_set_bool(w_settings, "cursor", true);
        g_cap.video_source = obs_source_create("window_capture", "WindowSource", w_settings, nullptr);
        obs_data_release(w_settings);
    } else {
        // Monitor capture
        obs_data_t *m_settings = obs_data_create();
        std::vector<MonitorInfo> mons;
        EnumDisplayMonitors(nullptr, nullptr, EnumMonitorsProc, reinterpret_cast<LPARAM>(&mons));
        std::string mon_id = params.value("monitor_id", "");
        if (mon_id.empty()) {
            if (g_cap.monitor_index < 0 || g_cap.monitor_index >= static_cast<int>(mons.size())) {
                obs_data_release(m_settings);
                out_err = "Selected monitor is unavailable; refusing silent monitor fallback";
                return false;
            }
            mon_id = mons[g_cap.monitor_index].device_name;
        } else {
            auto selected = std::find_if(mons.begin(), mons.end(), [&](const MonitorInfo &monitor) {
                return monitor.device_name == mon_id;
            });
            if (selected == mons.end()) {
                obs_data_release(m_settings);
                out_err = "Selected monitor is disconnected; refusing silent monitor fallback";
                return false;
            }
            g_cap.monitor_index = static_cast<int>(std::distance(mons.begin(), selected));
        }
        std::cerr << "[CaptureHost] Configured monitor capture: mon_id=" << mon_id << ", index=" << g_cap.monitor_index << ", method=2 (WGC)" << std::endl;
        if (!mon_id.empty()) {
            obs_data_set_string(m_settings, "monitor_id", mon_id.c_str());
        }
        obs_data_set_int(m_settings, "monitor", g_cap.monitor_index);
        obs_data_set_int(m_settings, "method", 2); // 2 = Windows Graphics Capture (WinRT, non-injecting, works on Win11/HAGS)
        obs_data_set_bool(m_settings, "capture_cursor", true);
        g_cap.video_source = obs_source_create("monitor_capture", "DisplaySource", m_settings, nullptr);
        obs_data_release(m_settings);
    }

    if (!g_cap.video_source) {
        out_err = "Failed to create video capture source";
        return false;
    }
    // Fit the entire selected source into the negotiated canvas. Directly
    // outputting a 4K source onto a 720p canvas would crop its top-left corner.
    g_cap.video_scene = obs_scene_create("CaptureCanvas");
    obs_sceneitem_t *item = g_cap.video_scene ? obs_scene_add(g_cap.video_scene, g_cap.video_source) : nullptr;
    if (!item) {
        out_err = "Could not create the aspect-preserving capture canvas";
        return false;
    }
    struct vec2 bounds;
    vec2_set(&bounds, static_cast<float>(g_cap.width), static_cast<float>(g_cap.height));
    obs_sceneitem_set_bounds_type(item, OBS_BOUNDS_SCALE_INNER);
    obs_sceneitem_set_bounds(item, &bounds);
    obs_sceneitem_set_bounds_alignment(item, OBS_ALIGN_CENTER);
    obs_sceneitem_set_alignment(item, OBS_ALIGN_TOP | OBS_ALIGN_LEFT);
    obs_set_output_source(0, obs_scene_get_source(g_cap.video_scene));

    // 2. Audio Sources & Multi-Track Routing
    g_cap.track_names.clear();
    g_cap.track_names.push_back("Combined Mix");

    // System Audio
    obs_data_t *sys_settings = obs_data_create();
    obs_data_set_string(sys_settings, "device_id", g_cap.system_device_id.c_str());
    g_cap.sys_audio_source = obs_source_create("wasapi_output_capture", "SystemAudio", sys_settings, nullptr);
    obs_data_release(sys_settings);

    if (!g_cap.sys_audio_source) {
        out_err = "System audio loopback source could not be created";
        return false;
    }
    if (g_cap.sys_audio_source) {
        // Route to Track 1 (bit 0) and Track 2 (bit 1)
        obs_source_set_audio_mixers(g_cap.sys_audio_source, (1 << 0) | (1 << 1));
        obs_set_output_source(1, g_cap.sys_audio_source);
        g_cap.track_names.push_back("System Audio");
    }

    // Microphone Audio (if enabled)
    if (g_cap.enable_mic) {
        obs_data_t *mic_settings = obs_data_create();
        obs_data_set_string(mic_settings, "device_id", g_cap.mic_device_id.c_str());
        g_cap.mic_audio_source = obs_source_create("wasapi_input_capture", "Microphone", mic_settings, nullptr);
        obs_data_release(mic_settings);

        if (!g_cap.mic_audio_source) {
            out_err = "Microphone source could not be created; disable mic or select an input";
            return false;
        }
        if (g_cap.mic_audio_source) {
            // Route to Track 1 (bit 0) and Track 3 (bit 2)
            obs_source_set_audio_mixers(g_cap.mic_audio_source, (1 << 0) | (1 << 2));
            obs_set_output_source(2, g_cap.mic_audio_source);
            g_cap.track_names.push_back("Microphone");
        }
    }

    // Use vendor-specific settings from the pinned OBS 32.2.2 plugins.
    obs_data_t *v_settings = obs_data_create();
    obs_data_set_int(v_settings, "bitrate", g_cap.bitrate_kbps);
    const bool modern_nvenc = g_cap.encoder_id.rfind("obs_nvenc_", 0) == 0;
    obs_data_set_string(v_settings, "rate_control", modern_nvenc ? "cbr" : "CBR");
    obs_data_set_int(v_settings, "keyint_sec", 2);
    if (encoder.software) {
        obs_data_set_string(v_settings, "preset", "ultrafast");
        obs_data_set_string(v_settings, "tune", "zerolatency");
        obs_data_set_string(v_settings, "profile", "high");
        const std::string options = "threads=" + std::to_string(flick::capture::software_threads(std::thread::hardware_concurrency())) +
                                    " lookahead-threads=1";
        obs_data_set_string(v_settings, "x264opts", options.c_str());
    } else if (g_cap.encoder_id.rfind("h264_", 0) == 0) {
        obs_data_set_string(v_settings, "preset", "speed");
        obs_data_set_bool(v_settings, "pre_analysis", false);
    }
    g_cap.video_encoder = obs_video_encoder_create(encoder.id, "h264_enc", v_settings, nullptr);
    obs_data_release(v_settings);
    if (!g_cap.video_encoder) {
        retryable = true;
        out_err = std::string(encoder.name) + " could not create an encoder";
        return false;
    }
    obs_encoder_set_video(g_cap.video_encoder, obs_get_video());

    // 4. Audio Encoders for active tracks
    g_cap.audio_encoders.clear();
    for (size_t t = 0; t < g_cap.track_names.size(); ++t) {
        obs_data_t *a_settings = obs_data_create();
        obs_data_set_int(a_settings, "bitrate", 128);
        std::string a_name = "aac_track_" + std::to_string(t);
        obs_encoder_t *a_enc = obs_audio_encoder_create("ffmpeg_aac", a_name.c_str(), a_settings, static_cast<size_t>(t), nullptr);
        obs_data_release(a_settings);
        if (!a_enc) {
            out_err = "Could not create AAC audio encoder";
            return false;
        }
        obs_encoder_set_audio(a_enc, obs_get_audio());
        g_cap.audio_encoders.push_back(a_enc);
    }

    // 5. Replay Buffer Output
    obs_data_t *rb_settings = obs_data_create();
    obs_data_set_int(rb_settings, "max_time_sec", g_cap.duration_sec);
    obs_data_set_int(rb_settings, "max_size_mb", g_cap.buffer_mb);
    obs_data_set_string(rb_settings, "directory", g_cap.output_dir.c_str());
    obs_data_set_string(rb_settings, "format", "Clip_%CCYY-%MM-%DD_%hh-%mm-%ss");
    obs_data_set_string(rb_settings, "extension", "mkv");
    g_cap.replay_output = obs_output_create("replay_buffer", "ReplayBuffer", rb_settings, nullptr);
    obs_data_release(rb_settings);

    if (!g_cap.replay_output) {
        out_err = "Failed to create replay buffer output";
        return false;
    }

    obs_output_set_video_encoder(g_cap.replay_output, g_cap.video_encoder);
    for (size_t t = 0; t < g_cap.audio_encoders.size(); ++t) {
        obs_output_set_audio_encoder(g_cap.replay_output, g_cap.audio_encoders[t], static_cast<size_t>(t));
    }

    signal_handler_connect(obs_output_get_signal_handler(g_cap.replay_output), "saved", on_replay_saved, g_cap.replay_output);

    if (!obs_output_start(g_cap.replay_output)) {
        retryable = true;
        const char *obs_error = obs_output_get_last_error(g_cap.replay_output);
        out_err = std::string("Failed to start replay buffer: ") + (obs_error ? obs_error : "unknown OBS error");
        return false;
    }

    // The successful public registration is available through OBS's API.
    // OBS 32.2.2 returns orig_info.id here; an internal texture-to-memory
    // reroute stays within that vendor's hardware encoder and is not exposed.
    if (const char *selected_id = obs_encoder_get_id(g_cap.video_encoder)) {
        g_cap.encoder_id = selected_id;
        if (const auto *selected = flick::capture::find_encoder(g_cap.encoder_id)) {
            g_cap.encoder_name = selected->name;
            g_cap.software_fallback = selected->software;
        } else if (const char *name = obs_encoder_get_display_name(selected_id))
            g_cap.encoder_name = name;
    }
    const auto actual_width = obs_encoder_get_width(g_cap.video_encoder);
    const auto actual_height = obs_encoder_get_height(g_cap.video_encoder);
    struct obs_video_info actual_video = {};
    if (!actual_width || !actual_height || !obs_get_video_info(&actual_video) || !actual_video.fps_den) {
        retryable = true;
        out_err = "Encoder started without a valid output video profile";
        return false;
    }
    g_cap.width = static_cast<int>(actual_width);
    g_cap.height = static_cast<int>(actual_height);
    g_cap.fps = static_cast<int>(actual_video.fps_num / actual_video.fps_den);
    return true;
}

static json capture_configuration_json() {
    return {{"encoder", g_cap.encoder_id}, {"encoder_name", g_cap.encoder_name},
            {"software_fallback", g_cap.software_fallback}, {"width", g_cap.width},
            {"height", g_cap.height}, {"fps", g_cap.fps}};
}

static bool start_capture_session(const json &params, std::string &out_err) {
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        if (g_cap.state != CaptureState::Off && g_cap.state != CaptureState::Faulted) {
            out_err = "Capture is already active or transitioning";
            return false;
        }
        g_cap.state = CaptureState::Starting;
        g_cap.save_in_progress = false;
    }
    try {
        cleanup_capture_resources();
        const std::string requested = params.value("encoder", "auto");
        const bool allow_software = params.value("allow_software_fallback", true);
        const flick::capture::Profile profile{params.value("width", 1920), params.value("height", 1080),
                                              params.value("fps", 30), params.value("bitrate_kbps", 15000)};
        // Validate before OBS initialization, including unknown explicit IDs.
        (void)flick::capture::candidates(requested, allow_software, {});
        const auto bootstrap = flick::capture::profile_for(*flick::capture::find_encoder("obs_x264"), profile);
        const int duration = params.value("duration_sec", 30), buffer = params.value("buffer_mb", 256);
        if (duration < 1 || duration > 300 || buffer < 16 || buffer > 2048)
            throw std::invalid_argument("Replay duration or buffer memory limit is unsupported.");
        if (!g_obs_initialized && !ensure_obs_init(bootstrap.width, bootstrap.height, bootstrap.fps)) {
            out_err = "OBS D3D11 capture initialization failed. Install current GPU drivers and the Microsoft Visual C++ v14 x64 runtime; Windows graphics support is required.";
            internal_stop_capture("initialization_failed");
            return false;
        }
        std::vector<std::string> registered;
        const char *id = nullptr;
        for (size_t index = 0; obs_enum_encoder_types(index, &id); ++index) {
            const char *codec = obs_get_encoder_codec(id);
            if (obs_get_encoder_type(id) == OBS_ENCODER_VIDEO && codec && std::string(codec) == "h264")
                registered.emplace_back(id);
        }
        const auto options = flick::capture::candidates(requested, allow_software, registered);
        if (options.empty()) {
            out_err = "No requested H.264 encoder is registered. Update GPU drivers or enable the CPU compatibility fallback.";
            internal_stop_capture("no_encoder");
            return false;
        }
        std::string failures;
        const bool started = flick::capture::start_candidates(options, [&](const flick::capture::Encoder &encoder) {
            const auto actual = flick::capture::profile_for(encoder, profile);
            bool retryable = false;
            std::string error;
            if (start_capture_attempt(params, encoder, actual, retryable, error)) return flick::capture::Attempt::started;
            if (!failures.empty()) failures += "; ";
            failures += std::string(encoder.id) + ": " + error;
            return retryable ? flick::capture::Attempt::retryable_failure : flick::capture::Attempt::fatal_failure;
        }, cleanup_capture_resources);
        if (!started) {
            out_err = "Capture could not start. " + failures;
            internal_stop_capture("start_failed");
            return false;
        }
        g_cap.start_time = std::chrono::steady_clock::now();
        {
            std::lock_guard<std::mutex> lock(g_state_mutex);
            g_cap.state = CaptureState::Buffering;
            g_cap.is_armed = false;
        }
        auto configured = capture_configuration_json();
        send_event("capture_configured", configured);
        configured["state"] = "Buffering";
        configured["target"] = g_cap.target_type;
        configured["duration_sec"] = g_cap.duration_sec;
        configured["tracks"] = g_cap.track_names;
        send_event("state_changed", configured);
        return true;
    } catch (const std::exception &error) {
        out_err = std::string("Capture could not start: ") + error.what();
        internal_stop_capture("configuration_failed");
        return false;
    }
}

// Release OBS worker/output resources without holding g_state_mutex: saved
// callbacks need that mutex, including callbacks already in flight at stop.
static void cleanup_capture_resources() {
    obs_output_t *output = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        g_cap.save_in_progress = false;
        g_cap.is_armed = false;
        output = g_cap.replay_output;
        g_cap.replay_output = nullptr;
    }
    if (output) {
        // In OBS 32.2.2, disconnect takes the same per-signal mutex that is
        // held while callbacks execute. From this command thread it waits
        // for any in-flight saved callback before the local owner is freed.
        signal_handler_disconnect(obs_output_get_signal_handler(output), "saved", on_replay_saved, output);
        obs_output_stop(output);
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(2000);
        while (obs_output_active(output) && std::chrono::steady_clock::now() < deadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(20));
        if (obs_output_active(output)) {
            std::cerr << "Replay buffer stop deadline exceeded, forcing stop" << std::endl;
            obs_output_force_stop(output);
        }
        obs_output_release(output);
    }

    if (g_cap.video_encoder) {
        obs_encoder_release(g_cap.video_encoder);
        g_cap.video_encoder = nullptr;
    }

    for (auto *a_enc : g_cap.audio_encoders) {
        obs_encoder_release(a_enc);
    }
    g_cap.audio_encoders.clear();

    // OBS dereferences its engine even when setting a null source. Idle stop,
    // first-start cleanup and an initialization failure have no live engine.
    if (g_obs_initialized) {
        obs_set_output_source(0, nullptr);
        obs_set_output_source(1, nullptr);
        obs_set_output_source(2, nullptr);
    }

    if (g_cap.sys_audio_source) {
        obs_source_release(g_cap.sys_audio_source);
        g_cap.sys_audio_source = nullptr;
    }

    if (g_cap.mic_audio_source) {
        obs_source_release(g_cap.mic_audio_source);
        g_cap.mic_audio_source = nullptr;
    }

    if (g_cap.video_scene) {
        obs_scene_release(g_cap.video_scene);
        g_cap.video_scene = nullptr;
    }

    if (g_cap.video_source) {
        obs_source_release(g_cap.video_source);
        g_cap.video_source = nullptr;
    }

    // Capture plugins destroy textures asynchronously. Finish those releases
    // before a retry changes the video canvas/device resources.
    if (g_obs_initialized) obs_wait_for_destroy_queue();

    g_cap.encoder_id.clear();
    g_cap.encoder_name.clear();
    g_cap.software_fallback = false;
}

// Stop is called by the command reader, never while holding g_state_mutex.
static void internal_stop_capture(const std::string &reason) {
    cleanup_capture_resources();
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        g_cap.state = CaptureState::Off;
        g_cap.is_armed = false;
        g_cap.save_in_progress = false;
    }

    json d;
    d["state"] = "Off";
    d["reason"] = reason;
    send_event("state_changed", d);
}

// Save replay buffer request
static bool trigger_save_replay(const std::string &save_id, const std::string &prefix, std::string &out_err) {
    std::lock_guard<std::mutex> lock(g_state_mutex);
    if (!g_cap.replay_output || !obs_output_active(g_cap.replay_output)) {
        out_err = "Replay buffer is not active";
        return false;
    }

    if (g_cap.save_in_progress) {
        out_err = "A save request is already in progress";
        return false;
    }

    g_cap.save_in_progress = true;
    g_cap.pending_save_id = save_id;
    g_cap.pending_save_prefix = prefix;
    g_cap.pending_save_start = std::chrono::steady_clock::now();
    g_cap.pending_save_walltime = std::filesystem::file_time_type::clock::now();

    proc_handler_t *ph = obs_output_get_proc_handler(g_cap.replay_output);
    calldata_t cd;
    calldata_init(&cd);
    bool accepted = ph && proc_handler_call(ph, "save", &cd);
    calldata_free(&cd);
    if (!accepted) {
        g_cap.save_in_progress = false;
        out_err = "Replay buffer did not accept save operation";
        return false;
    }
    return true;
}

// Status query
static json get_current_status() {
    std::lock_guard<std::mutex> lock(g_state_mutex);
    json j;
    std::string fault_reason;

    // A driver/encoder may fail after obs_output_start accepted the pipeline.
    // Do not advance an inactive output to Armed just because time elapsed.
    if ((g_cap.state == CaptureState::Buffering || g_cap.state == CaptureState::Armed || g_cap.state == CaptureState::Paused) &&
        (!g_cap.replay_output || !obs_output_active(g_cap.replay_output))) {
        g_cap.state = CaptureState::Faulted;
        g_cap.is_armed = false;
        fault_reason = "capture_output_stopped";
        const char *detail = g_cap.replay_output ? obs_output_get_last_error(g_cap.replay_output) : nullptr;
        send_event("fault", {{"reason", detail && *detail ? detail : "Capture output stopped unexpectedly; check GPU drivers or try CPU compatibility mode"}});
    }

    double elapsed = 0.0;
    if (g_cap.state == CaptureState::Buffering || g_cap.state == CaptureState::Armed) {
        auto now = std::chrono::steady_clock::now();
        elapsed = std::chrono::duration<double>(now - g_cap.start_time).count();
        if (elapsed >= g_cap.duration_sec && g_cap.state == CaptureState::Buffering) {
            g_cap.state = CaptureState::Armed;
            g_cap.is_armed = true;
            json d;
            d["state"] = "Armed";
            send_event("state_changed", d);
        }
    }

    double buffered = (elapsed > g_cap.duration_sec) ? static_cast<double>(g_cap.duration_sec) : elapsed;
    if (g_cap.save_in_progress &&
        std::chrono::steady_clock::now() - g_cap.pending_save_start > std::chrono::seconds(15)) {
        g_cap.save_in_progress = false;
        send_event("save_failed", {{"reason", "Replay save timed out after 15 seconds"}});
    }
    j["buffered_sec"] = buffered;
    j["max_sec"] = g_cap.duration_sec;
    j["target_type"] = g_cap.target_type;
    j.update(capture_configuration_json());
    j["buffered_duration_estimated"] = true;
    j["tracks"] = g_cap.track_names;

    // Check target window liveness
    if (g_cap.target_type == "window" && g_cap.window_hwnd) {
        HDESK hdesk = OpenDesktopW(L"default", 0, FALSE, GENERIC_ALL);
        if (hdesk) {
            SetThreadDesktop(hdesk);
            CloseDesktop(hdesk);
        }
        if (!IsWindow(g_cap.window_hwnd)) {
            j["target_valid"] = false;
            // Trip fault
            if (g_cap.state != CaptureState::Faulted && g_cap.state != CaptureState::Off) {
                g_cap.state = CaptureState::Faulted;
                g_cap.is_armed = false;
                fault_reason = "capture_target_lost";
                json fd;
                fd["reason"] = "Target window was closed";
                send_event("fault", fd);
            }
        } else {
            j["target_valid"] = true;
        }
    } else {
        j["target_valid"] = true;
    }

    j["state"] = state_to_string(g_cap.state);
    j["is_armed"] = g_cap.is_armed.load();
    if (!fault_reason.empty()) j["fault_reason"] = fault_reason;

    // Process memory usage (private bytes)
    PROCESS_MEMORY_COUNTERS_EX pmc;
    if (GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&pmc, sizeof(pmc))) {
        j["private_bytes"] = pmc.PrivateUsage;
        j["working_set"] = pmc.WorkingSetSize;
    }

    return j;
}

// Main entry point
int main(int argc, char **argv) {
    HDESK hDefaultDesk = OpenDesktopW(L"default", 0, FALSE, GENERIC_ALL);
    if (hDefaultDesk) {
        SetThreadDesktop(hDefaultDesk);
        CloseDesktop(hDefaultDesk);
    }

    const auto bin_dir = runtime_bin_directory();
    if (bin_dir.empty() || !SetCurrentDirectoryW(bin_dir.c_str())) {
        std::cerr << "Unable to locate local OBS runtime directory" << std::endl;
        return 2;
    }

    // Start background stdin reader
    std::string line;
    while (std::getline(std::cin, line)) {
        if (line.empty()) continue;

        try {
            json req = json::parse(line);
            std::string id = req.value("id", "");
            std::string cmd = req.value("cmd", "");

            if (cmd == "ping") {
                send_response(id, true, "pong");
            }
            else if (cmd == "enum_targets") {
                // Windows
                std::vector<WindowInfo> wins;
                enumerate_all_windows(wins);
                json jwins = json::array();
                for (const auto &w : wins) {
                    jwins.push_back({
                        {"hwnd", w.hwnd},
                        {"title", w.title},
                        {"pid", w.pid},
                        {"exe", w.exe_name},
                        {"width", w.width},
                        {"height", w.height}
                    });
                }

                // Monitors
                std::vector<MonitorInfo> mons;
                EnumDisplayMonitors(nullptr, nullptr, EnumMonitorsProc, reinterpret_cast<LPARAM>(&mons));
                json jmons = json::array();
                for (const auto &m : mons) {
                    jmons.push_back({
                        {"index", m.index},
                        {"device", m.device_name},
                        {"width", m.width},
                        {"height", m.height},
                        {"primary", m.is_primary}
                    });
                }

                json res;
                res["windows"] = jwins;
                res["monitors"] = jmons;
                send_response(id, true, res);
            }
            else if (cmd == "start_capture") {
                json params = req.value("params", json::object());
                std::string err;
                if (start_capture_session(params, err)) {
                    auto configured = capture_configuration_json();
                    configured["status"] = "Buffering";
                    send_response(id, true, configured);
                } else {
                    send_response(id, false, nullptr, err);
                }
            }
            else if (cmd == "save_replay") {
                json params = req.value("params", json::object());
                std::string save_id = params.value("save_id", "clip");
                std::string prefix = params.value("prefix", "Clip");
                std::string err;
                if (trigger_save_replay(save_id, prefix, err)) {
                    send_response(id, true, {{"save_id", save_id}, {"status", "saving"}});
                } else {
                    send_response(id, false, nullptr, err);
                }
            }
            else if (cmd == "get_status") {
                json st = get_current_status();
                send_response(id, true, st);
                if (g_cap.state == CaptureState::Faulted) {
                    // Do not leave an actively encoding source behind a faulted UI.
                    internal_stop_capture(st.value("fault_reason", "capture_fault"));
                }
            }
            else if (cmd == "stop_capture") {
                internal_stop_capture("user_requested");
                send_response(id, true, {{"status", "Off"}});
            }
            else if (cmd == "shutdown") {
                internal_stop_capture("shutdown");
                if (g_obs_initialized) {
                    obs_shutdown();
                    g_obs_initialized = false;
                }
                send_response(id, true, {{"status", "shutdown_complete"}});
                break;
            }
            else {
                send_response(id, false, nullptr, "Unknown command: " + cmd);
            }
        }
        catch (const std::exception &ex) {
            send_response("", false, nullptr, std::string("JSON parse/exec error: ") + ex.what());
        }
    }

    // Clean exit
    if (g_obs_initialized) {
        cleanup_capture_resources();
        obs_shutdown();
        g_obs_initialized = false;
    }
    return 0;
}
