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
#include "util/windows/window-helpers.h"
#include "json.hpp"
#include "encoder_policy.hpp"
#include "diagnostic_policy.hpp"
#include "audio_policy.hpp"
#include "exact_process_audio.hpp"
#include "strict_window_source.hpp"

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
    std::string monitor_id;
    HWND window_hwnd = nullptr;
    DWORD window_pid = 0;
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
    bool enable_system_audio = true;
    std::string mic_device_id = "default";
    std::string system_device_id = "default";

    std::chrono::steady_clock::time_point start_time;
    std::atomic<bool> is_armed{false};

    obs_source_t *video_source = nullptr;
    obs_scene_t *video_scene = nullptr;
    obs_source_t *sys_audio_source = nullptr;
    obs_source_t *mic_audio_source = nullptr;
    obs_source_t *game_audio_source = nullptr;
    obs_source_t *voice_audio_source = nullptr;
    DWORD game_audio_pid = 0, voice_audio_pid = 0;
    bool separated_audio = false;
    obs_encoder_t *video_encoder = nullptr;
    std::vector<obs_encoder_t*> audio_encoders;
    obs_output_t *replay_output = nullptr;

    // Track metadata
    std::vector<std::string> track_names;

    // Pending save requests
    flick::capture::SaveLifecycle save;
    std::chrono::steady_clock::time_point pending_save_start;
    std::filesystem::file_time_type pending_save_walltime;
};

static ActiveCapture g_cap;
static std::atomic<bool> g_obs_initialized{false};

// This callback has its own small metadata lock and never takes the capture
// state lock or emits protocol messages on an encoder's worker thread.
static std::mutex g_history_mutex;
static flick::capture::ReplayHistory g_history;
static bool g_history_enabled = false;
static bool g_history_failed = false;

static void on_replay_packet(obs_output_t *, struct encoder_packet *packet, struct encoder_packet_time *, void *) {
    if (!packet || packet->timebase_den <= 0 || packet->timebase_num <= 0) return;
    const auto pts_us = static_cast<int64_t>(static_cast<long double>(packet->pts) * packet->timebase_num * 1000000 / packet->timebase_den);
    std::lock_guard<std::mutex> lock(g_history_mutex);
    if (g_history_enabled) {
        try {
            g_history.observe({packet->dts_usec, pts_us, static_cast<uint64_t>(packet->size),
                               packet->type == OBS_ENCODER_VIDEO, packet->keyframe});
        } catch (...) {
            // Never unwind a C++ allocation failure through an OBS C callback.
            g_history_enabled = false;
            g_history_failed = true;
            g_history.reset(30, 256, 30);
        }
    }
}

static flick::capture::ReplaySnapshot replay_snapshot() {
    std::lock_guard<std::mutex> lock(g_history_mutex);
    return g_history.snapshot();
}

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
        if (!g_cap.save.pending() || !g_cap.replay_output ||
            g_cap.replay_output != output) return;
        save_id = g_cap.save.id();
        out_dir = g_cap.output_dir;
        requested_at = g_cap.pending_save_walltime;
        tracks = g_cap.track_names;
    }

    auto reject = [&](const char *message) {
        bool rejected = false;
        {
            std::lock_guard<std::mutex> lock(g_state_mutex);
            if (g_cap.replay_output == output) rejected = g_cap.save.complete(save_id);
        }
        if (rejected) send_event("save_failed", {{"reason", message}, {"save_id", save_id}});
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
    if (!flick::capture::valid_save_id(save_id)) {
        reject("Replay save ID was invalid");
        return;
    }
    auto target = directory / std::filesystem::u8path("Flick_" + save_id + ".mkv");
    bool moved = false;
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        if (g_cap.replay_output != output || !g_cap.save.pending() || g_cap.save.id() != save_id) return;
        // Commit ownership and the atomic rename together: timeout cannot
        // concurrently fail this ID and let a late mux publish another save.
        moved = MoveFileW(source.c_str(), target.c_str()) != FALSE;
        g_cap.save.complete(save_id);
    }
    if (!moved) {
        send_event("save_failed", {{"reason", "Could not finalize the unique replay file without overwriting another clip"}, {"save_id", save_id}});
        return;
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
    std::string executable_path;
    uint64_t creation_time = 0;
    int width = 0;
    int height = 0;
};

static bool utf8_from_wide(const wchar_t *value, std::string &result) {
    const int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return false;
    std::string converted(static_cast<size_t>(bytes), '\0');
    if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, converted.data(), bytes, nullptr, nullptr)) return false;
    converted.pop_back();
    result = std::move(converted);
    return true;
}

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
    if (titleLen <= 0 || titleLen > 32768) return TRUE;

    RECT rc = {};
    if (!GetWindowRect(hwnd, &rc)) return TRUE;
    int w = rc.right - rc.left;
    int h = rc.bottom - rc.top;
    if (w < 100 || h < 100) return TRUE;

    std::vector<wchar_t> wtitle(titleLen + 1);
    if (!GetWindowTextW(hwnd, wtitle.data(), titleLen + 1)) return TRUE;

    wchar_t clsName[256] = {};
    if (!GetClassNameW(hwnd, clsName, 256)) return TRUE;
    if (wcscmp(clsName, L"Progman") == 0 || wcscmp(clsName, L"WorkerW") == 0 || wcscmp(clsName, L"Shell_TrayWnd") == 0) {
        return TRUE;
    }

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);

    std::string exeName;
    HANDLE hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (hProc) {
        wchar_t path[MAX_PATH] = {};
        DWORD size = MAX_PATH;
        if (QueryFullProcessImageNameW(hProc, 0, path, &size)) {
            wchar_t *slash = wcsrchr(path, L'\\');
            std::wstring wexe = (slash ? slash + 1 : path);
            utf8_from_wide(wexe.c_str(), exeName);
        }
        CloseHandle(hProc);
    }

    std::string title;
    if (!utf8_from_wide(wtitle.data(), title)) return TRUE;

    WindowInfo info;
    info.hwnd = reinterpret_cast<uint64_t>(hwnd);
    info.title = std::move(title);
    info.pid = pid;
    info.exe_name = exeName;
    flick::game::ProcessIdentity process_identity;
    if (flick::game::query_process_identity(pid, process_identity)) {
        utf8_from_wide(process_identity.executable_path.c_str(), info.executable_path);
        info.creation_time = process_identity.creation_time;
        utf8_from_wide(std::filesystem::path(process_identity.executable_path).filename().wstring().c_str(), info.exe_name);
    }
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
        char cdev[128] = {};
        if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, mi.szDevice, -1, cdev, sizeof(cdev), nullptr, nullptr)) return TRUE;
        minfo.device_name = cdev;
        minfo.width = mi.rcMonitor.right - mi.rcMonitor.left;
        minfo.height = mi.rcMonitor.bottom - mi.rcMonitor.top;
        minfo.is_primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
        list->push_back(minfo);
    }
    return TRUE;
}

static bool selected_target_available(std::string &reason) {
    if (g_cap.target_type == "window") {
        DWORD pid = 0;
        GetWindowThreadProcessId(g_cap.window_hwnd, &pid);
        DWORD cloaked = 0;
        const bool is_cloaked = SUCCEEDED(DwmGetWindowAttribute(g_cap.window_hwnd, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && cloaked;
        if (!IsWindow(g_cap.window_hwnd) || !pid || pid != g_cap.window_pid ||
            !IsWindowVisible(g_cap.window_hwnd) || IsIconic(g_cap.window_hwnd) || is_cloaked) {
            reason = "Selected window is closed, hidden or minimized; reselect a visible window";
            return false;
        }
    } else if (g_cap.target_type == "monitor") {
        std::vector<MonitorInfo> monitors;
        EnumDisplayMonitors(nullptr, nullptr, EnumMonitorsProc, reinterpret_cast<LPARAM>(&monitors));
        const auto found = std::find_if(monitors.begin(), monitors.end(), [](const MonitorInfo &monitor) {
            return !g_cap.monitor_id.empty() && monitor.device_name == g_cap.monitor_id;
        });
        if (found == monitors.end()) {
            reason = "Selected monitor was disconnected; reselect a display";
            return false;
        }
    } else {
        reason = "Selected capture target is unavailable";
        return false;
    }
    return true;
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
    if (!flick::game::register_exact_window_source(bin_dir)) {
        obs_shutdown();
        return false;
    }
    flick::audio::register_process_audio();
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
    g_cap.monitor_id.clear();
    uint64_t hwnd_raw = params.value("window_hwnd", 0ULL);
    g_cap.window_hwnd = reinterpret_cast<HWND>(hwnd_raw);
    g_cap.window_pid = 0;
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
    g_cap.enable_system_audio = params.value("enable_system_audio", true);
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
        GetWindowThreadProcessId(g_cap.window_hwnd, &g_cap.window_pid);
        if (!selected_target_available(out_err)) {
            return false;
        }
        flick::game::ProcessIdentity process_identity;
        if (!flick::game::query_process_identity(g_cap.window_pid, process_identity)) {
            out_err = "Selected window process identity is unavailable; refusing recording";
            return false;
        }
        std::vector<wchar_t> title(32769);
        if (!GetWindowTextW(g_cap.window_hwnd, title.data(), static_cast<int>(title.size()))) {
            out_err = "Selected window title is unavailable";
            return false;
        }
        std::string selected_title, selected_exe;
        const auto basename = std::filesystem::path(process_identity.executable_path).filename().wstring();
        if (!utf8_from_wide(title.data(), selected_title) || !utf8_from_wide(basename.c_str(), selected_exe)) {
            out_err = "Selected window metadata is unavailable";
            return false;
        }
        const bool expected_window = params.contains("expected_window_pid") ||
                                     params.contains("expected_window_title") || params.contains("expected_window_exe");
        if (expected_window && (!params.contains("expected_window_pid") || !params.contains("expected_window_title") ||
            !params.contains("expected_window_exe") || !flick::capture::expected_window_matches(
                params.value("expected_window_pid", 0u), params.value("expected_window_title", ""),
                params.value("expected_window_exe", ""), g_cap.window_pid, selected_title, selected_exe))) {
            out_err = "The consented window changed; select it again before recording";
            return false;
        }
        const bool expected_process = params.contains("expected_window_path") || params.contains("expected_window_creation_time");
        if (expected_process) {
            if (!expected_window || !params.contains("expected_window_path") || !params.contains("expected_window_creation_time")) {
                out_err = "The approved process identity is incomplete";
                return false;
            }
            flick::game::ProcessIdentity expected;
            expected.pid = g_cap.window_pid;
            expected.creation_time = params.value("expected_window_creation_time", 0ULL);
            if (!flick::game::from_utf8(params.value("expected_window_path", ""), expected.executable_path) ||
                !flick::game::same_process_identity(expected, process_identity)) {
                out_err = "The approved game process changed; refusing recording";
                return false;
            }
        }
        std::string full_path;
        if (!utf8_from_wide(process_identity.executable_path.c_str(), full_path)) {
            out_err = "Selected executable path cannot be represented safely";
            return false;
        }
        obs_data_t *w_settings = obs_data_create();
        obs_data_set_int(w_settings, "hwnd", static_cast<long long>(hwnd_raw));
        obs_data_set_int(w_settings, "pid", process_identity.pid);
        obs_data_set_int(w_settings, "creation_time", static_cast<long long>(process_identity.creation_time));
        obs_data_set_string(w_settings, "executable_path", full_path.c_str());
        g_cap.video_source = obs_source_create(flick::game::exact_window_source_id, "ExactWindowSource", w_settings, nullptr);
        obs_data_release(w_settings);
    } else {
        // Monitor capture
        std::vector<MonitorInfo> mons;
        EnumDisplayMonitors(nullptr, nullptr, EnumMonitorsProc, reinterpret_cast<LPARAM>(&mons));
        std::vector<std::string> ids;
        for (const auto &monitor : mons) ids.push_back(monitor.device_name);
        g_cap.monitor_index = static_cast<int>(flick::capture::select_monitor(ids, params.value("monitor_id", ""), g_cap.monitor_index));
        g_cap.monitor_id = mons[g_cap.monitor_index].device_name;
        const auto &mon_id = g_cap.monitor_id;
        std::cerr << "[CaptureHost] Configured monitor capture: mon_id=" << mon_id << ", index=" << g_cap.monitor_index << ", method=2 (WGC)" << std::endl;
        obs_data_t *m_settings = obs_data_create();
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

    // Real process isolation is explicit and never falls back to device audio.
    g_cap.separated_audio = g_cap.audio_mode == "separated" && g_cap.enable_system_audio;
    if (g_cap.audio_mode != "separated" && g_cap.audio_mode != "systemfallback" && g_cap.audio_mode != "system_fallback") {
        out_err = "Unknown audio capture mode"; return false;
    }
    g_cap.game_audio_pid = g_cap.voice_audio_pid = 0;
    auto requested_pid = [&](const char *key) -> DWORD {
        return params.contains(key) && params[key].is_object() ? params[key].value("pid", 0u) : 0u;
    };
    if (g_cap.separated_audio) {
        if (!flick::audio::supported()) { out_err = "App audio requires Windows build 20348 or newer. Select System Audio explicitly if desired."; return false; }
        g_cap.game_audio_pid = requested_pid("game_audio"); g_cap.voice_audio_pid = requested_pid("voice_audio");
        if (!g_cap.game_audio_pid && !g_cap.voice_audio_pid) { out_err = "Select at least one app audio source in Settings"; return false; }
        if (g_cap.game_audio_pid && g_cap.voice_audio_pid &&
            (flick::audio::is_ancestor(g_cap.game_audio_pid, g_cap.voice_audio_pid) || flick::audio::is_ancestor(g_cap.voice_audio_pid, g_cap.game_audio_pid))) {
            out_err = "Game and Voice audio process trees overlap. Select distinct apps to avoid duplicate audio."; return false;
        }
    }
    const auto routing = flick::audio::routing(g_cap.separated_audio,
        g_cap.enable_system_audio && !g_cap.separated_audio, g_cap.game_audio_pid != 0, g_cap.voice_audio_pid != 0, g_cap.enable_mic);
    g_cap.track_names = routing.tracks;
    auto create_process = [&](const char *key, const char *name, uint32_t mask, int channel, obs_source_t *&source) {
        if (!mask) return true;
        const auto &identity = params.at(key);
        const DWORD pid = identity.value("pid", 0u);
        const auto birth = identity.value("creation_time", uint64_t{0});
        const auto path = identity.value("path", std::string{});
        if (!flick::audio::process_identity_valid(pid, birth, path)) { out_err = "App audio identity is incomplete; refresh and select the app again"; return false; }
        obs_data_t *settings = obs_data_create();
        obs_data_set_int(settings, "pid", pid); obs_data_set_int(settings, "birth", static_cast<int64_t>(birth)); obs_data_set_string(settings, "path", path.c_str());
        source = obs_source_create("flick_exact_process_audio", name, settings, nullptr);
        obs_data_release(settings);
        calldata_t status; calldata_init(&status);
        const bool attached = source && proc_handler_call(obs_source_get_proc_handler(source), "get_audio_status", &status) && calldata_bool(&status, "attached");
        calldata_free(&status);
        if (!attached) { out_err = std::string(name) + " could not attach to the exact selected process; refresh the source or choose System Audio explicitly"; return false; }
        obs_source_set_audio_mixers(source, mask); obs_set_output_source(channel, source); return true;
    };
    if (!create_process("game_audio", "Game Audio", routing.game, 3, g_cap.game_audio_source) ||
        !create_process("voice_audio", "Voice Audio", routing.voice, 4, g_cap.voice_audio_source)) return false;
    if (routing.system) {
        obs_data_t *settings = obs_data_create();
        obs_data_set_string(settings, "device_id", g_cap.system_device_id.c_str());
        g_cap.sys_audio_source = obs_source_create("wasapi_output_capture", "SystemAudio", settings, nullptr);
        obs_data_release(settings);
        if (!g_cap.sys_audio_source) { out_err = "System audio loopback source could not be created"; return false; }
        obs_source_set_audio_mixers(g_cap.sys_audio_source, routing.system); obs_set_output_source(1, g_cap.sys_audio_source);
    }
    if (g_cap.enable_mic) {
        obs_data_t *settings = obs_data_create();
        obs_data_set_string(settings, "device_id", g_cap.mic_device_id.c_str());
        g_cap.mic_audio_source = obs_source_create("wasapi_input_capture", "Microphone", settings, nullptr);
        obs_data_release(settings);
        if (!g_cap.mic_audio_source) { out_err = "Microphone source could not be created; disable mic or select an input"; return false; }
        obs_source_set_audio_mixers(g_cap.mic_audio_source, routing.microphone); obs_set_output_source(2, g_cap.mic_audio_source);
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

    {
        std::lock_guard<std::mutex> lock(g_history_mutex);
        g_history.reset(g_cap.duration_sec, g_cap.buffer_mb, g_cap.fps);
        g_history_enabled = true;
        g_history_failed = false;
    }
    obs_output_add_packet_callback(g_cap.replay_output, on_replay_packet, nullptr);
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

static json audio_configuration_json() {
    json sources = json::array();
    auto process = [&](const char *kind, obs_source_t *source, DWORD pid) {
        if (!source) return;
        calldata_t status; calldata_init(&status);
        const bool queried = proc_handler_call(obs_source_get_proc_handler(source), "get_audio_status", &status);
        const bool attached = queried && calldata_bool(&status, "attached");
        sources.push_back({{"kind", kind}, {"process_id", pid}, {"state", attached ? "Attached" : "Lost"},
                           {"signal_observed", queried && calldata_bool(&status, "signal_observed")}});
        calldata_free(&status);
    };
    process("Game", g_cap.game_audio_source, g_cap.game_audio_pid);
    process("Voice", g_cap.voice_audio_source, g_cap.voice_audio_pid);
    if (g_cap.sys_audio_source) sources.push_back({{"kind", "System"}, {"state", "Device loopback"}, {"signal_observed", false}});
    if (g_cap.mic_audio_source) sources.push_back({{"kind", "Microphone"}, {"state", "Input device"}, {"signal_observed", false}});
    return {{"audio_mode_requested", g_cap.audio_mode},
            {"audio_mode_actual", g_cap.separated_audio ? "Separated" : (g_cap.sys_audio_source ? "SystemFallback" : "NoSystemAudio")},
            {"audio_sources", sources}};
}

static json capture_configuration_json() {
    json result = {{"encoder", g_cap.encoder_id}, {"encoder_name", g_cap.encoder_name},
            {"software_fallback", g_cap.software_fallback}, {"width", g_cap.width},
            {"height", g_cap.height}, {"fps", g_cap.fps},
            {"system_audio_enabled", g_cap.enable_system_audio}, {"microphone_enabled", g_cap.enable_mic}};
    result.update(audio_configuration_json()); return result;
}

static bool start_capture_session(const json &params, std::string &out_err) {
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        if (g_cap.state != CaptureState::Off && g_cap.state != CaptureState::Faulted) {
            out_err = "Capture is already active or transitioning";
            return false;
        }
        g_cap.state = CaptureState::Starting;
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
        flick::capture::validate_replay_limits(duration, buffer);
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
    // Keep an accepted save's callback connected while its mux finishes.
    // Command handling is serialized; workers may settle the request while
    // this bounded wait owns neither the state nor the OBS signal mutex.
    auto save_deadline = std::chrono::steady_clock::now();
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        if (g_cap.save.pending()) save_deadline = g_cap.pending_save_start + std::chrono::seconds(15);
    }
    for (;;) {
        bool pending = false;
        { std::lock_guard<std::mutex> lock(g_state_mutex); pending = g_cap.save.pending(); }
        if (!pending || std::chrono::steady_clock::now() >= save_deadline) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    obs_output_t *output = nullptr;
    std::string failed_save;
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        failed_save = g_cap.save.fail_pending();
        g_cap.is_armed = false;
        output = g_cap.replay_output;
        g_cap.replay_output = nullptr;
    }
    if (!failed_save.empty())
        send_event("save_failed", {{"save_id", failed_save}, {"reason", "Replay save did not finish before capture stopped"}});
    {
        std::lock_guard<std::mutex> lock(g_history_mutex);
        g_history_enabled = false;
    }
    if (output) {
        // OBS removes packet callbacks under the callback-list mutex, waiting
        // for this metadata observer without the state/history locks held.
        obs_output_remove_packet_callback(output, on_replay_packet, nullptr);
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
    {
        std::lock_guard<std::mutex> lock(g_history_mutex);
        g_history.reset(30, 256, 30);
        g_history_failed = false;
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
        obs_set_output_source(3, nullptr);
        obs_set_output_source(4, nullptr);
    }

    if (g_cap.sys_audio_source) {
        obs_source_release(g_cap.sys_audio_source);
        g_cap.sys_audio_source = nullptr;
    }

    if (g_cap.game_audio_source) { obs_source_release(g_cap.game_audio_source); g_cap.game_audio_source = nullptr; }
    if (g_cap.voice_audio_source) { obs_source_release(g_cap.voice_audio_source); g_cap.voice_audio_source = nullptr; }
    g_cap.game_audio_pid = g_cap.voice_audio_pid = 0;
    g_cap.separated_audio = false;
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

    { std::lock_guard<std::mutex> lock(g_state_mutex); g_cap.save.reset(); }

    g_cap.encoder_id.clear();
    g_cap.encoder_name.clear();
    g_cap.software_fallback = false;
}

// Stop is called by the command reader, never while holding g_state_mutex.
static void internal_stop_capture(const std::string &reason) {
    bool transitioning = false;
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        transitioning = g_cap.state != CaptureState::Off;
        if (transitioning) g_cap.state = CaptureState::Stopping;
        g_cap.is_armed = false;
    }
    if (transitioning) send_event("state_changed", {{"state", "Stopping"}, {"reason", reason}});
    cleanup_capture_resources();
    {
        std::lock_guard<std::mutex> lock(g_state_mutex);
        g_cap.state = CaptureState::Off;
        g_cap.is_armed = false;
    }

    json d;
    d["state"] = "Off";
    d["reason"] = reason;
    send_event("state_changed", d);
}

// Save replay buffer request
static bool trigger_save_replay(const std::string &save_id, const std::string &, std::string &out_err) {
    std::lock_guard<std::mutex> lock(g_state_mutex);
    if (!g_cap.replay_output || !obs_output_active(g_cap.replay_output)) {
        out_err = "Replay buffer is not active";
        return false;
    }

    if (!selected_target_available(out_err)) {
        g_cap.state = CaptureState::Faulted;
        g_cap.is_armed = false;
        send_event("fault", {{"reason", out_err}});
        return false;
    }
    if (!g_cap.video_source || !obs_source_get_width(g_cap.video_source) || !obs_source_get_height(g_cap.video_source) ||
        !replay_snapshot().save_ready) {
        out_err = "No decodable replay video is retained yet; wait for the selected source to produce video";
        return false;
    }

    if (!flick::capture::valid_save_id(save_id)) {
        out_err = "Save ID must contain 1 to 128 letters, digits, dashes or underscores";
        return false;
    }
    if (g_cap.save.pending()) {
        out_err = "A save request is already in progress";
        return false;
    }
    if (!g_cap.save.begin(save_id)) {
        out_err = "Previous replay save failed; restart capture before saving again";
        return false;
    }
    g_cap.pending_save_start = std::chrono::steady_clock::now();
    g_cap.pending_save_walltime = std::filesystem::file_time_type::clock::now();

    proc_handler_t *ph = obs_output_get_proc_handler(g_cap.replay_output);
    calldata_t cd;
    calldata_init(&cd);
    bool accepted = ph && proc_handler_call(ph, "save", &cd);
    calldata_free(&cd);
    if (!accepted) {
        g_cap.save.complete(save_id);
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

    std::string target_error;
    bool target_valid = true;
    bool source_ready = false;
    auto capturing = [&] { return g_cap.state == CaptureState::Buffering || g_cap.state == CaptureState::Armed || g_cap.state == CaptureState::Paused; };
    if (capturing()) {
        target_valid = selected_target_available(target_error);
        source_ready = g_cap.video_source && obs_source_get_width(g_cap.video_source) && obs_source_get_height(g_cap.video_source);
        if (!target_valid || (!source_ready && std::chrono::steady_clock::now() - g_cap.start_time > std::chrono::seconds(5))) {
            g_cap.state = CaptureState::Faulted;
            g_cap.is_armed = false;
            fault_reason = "capture_target_lost";
            if (target_error.empty()) target_error = "Selected source did not produce video; reselect a visible source or check graphics support";
            send_event("fault", {{"reason", target_error}});
        }
    }
    if (capturing() && g_cap.separated_audio) {
        const auto audio = audio_configuration_json();
        for (const auto &source : audio["audio_sources"]) if (source.value("state", "") == "Lost") {
            g_cap.state = CaptureState::Faulted; g_cap.is_armed = false; fault_reason = "capture_audio_source_lost";
            send_event("fault", {{"reason", "A selected app audio process exited or stopped. Replay stopped; refresh the audio selection."}}); break;
        }
    }
    const auto history = replay_snapshot();
    {
        std::lock_guard<std::mutex> history_lock(g_history_mutex);
        if (capturing() && g_history_failed) {
            g_cap.state = CaptureState::Faulted;
            g_cap.is_armed = false;
            fault_reason = "capture_buffer_tracking_failed";
            send_event("fault", {{"reason", "Replay buffer tracking failed; restart capture"}});
        }
    }
    if (g_cap.state == CaptureState::Buffering || g_cap.state == CaptureState::Armed) {
        const auto next_state = source_ready && history.save_ready ? CaptureState::Armed : CaptureState::Buffering;
        g_cap.is_armed = next_state == CaptureState::Armed;
        if (g_cap.state != next_state) {
            g_cap.state = next_state;
            send_event("state_changed", {{"state", state_to_string(next_state)}});
        }
    }

    const double buffered = capturing() && source_ready ? (std::min)(static_cast<double>(g_cap.duration_sec), history.seconds) : 0.0;
    if (g_cap.save.pending() &&
        std::chrono::steady_clock::now() - g_cap.pending_save_start > std::chrono::seconds(15)) {
        const auto expired_id = g_cap.save.fail_pending();
        g_cap.state = CaptureState::Faulted;
        g_cap.is_armed = false;
        fault_reason = "capture_save_timed_out";
        send_event("save_failed", {{"reason", "Replay save timed out after 15 seconds; capture stopped to protect save identity"}, {"save_id", expired_id}});
    }
    j["buffered_sec"] = buffered;
    j["max_sec"] = g_cap.duration_sec;
    j["target_type"] = g_cap.target_type;
    j.update(capture_configuration_json());
    j["buffered_duration_estimated"] = true;
    j["buffer_tracking_limited"] = history.tracking_limited;
    j["tracks"] = g_cap.track_names;
    j["target_valid"] = target_valid;

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
            else if (cmd == "get_capabilities") {
                send_response(id, true, {{"protocol_version", 2}, {"diagnostic_no_system_audio", true},
                                         {"source_identity_binding", true}, {"exact_process_audio", flick::audio::supported()},
                                         {"exact_window_binding", true}});
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
                        {"exe_path", w.executable_path},
                        {"executable_path", w.executable_path},
                        {"process_creation_time", w.creation_time},
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
                if (g_cap.state == CaptureState::Faulted)
                    internal_stop_capture("capture_target_lost");
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
