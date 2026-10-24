#pragma once
// Flick-owned exact-HWND adapter over the pinned OBS 32.2.2 C WinRT API.
// Lifecycle follows upstream window-capture.c / libobs-winrt (GPL-2.0-or-later).
// No title resolver, process injection, audio source or monitor fallback.
#include <dwmapi.h>
#include "obs.h"
#include "graphics/graphics.h"
#include "game_window_identity.hpp"
#include <atomic>
#include <filesystem>

namespace flick::game {
struct WinrtCapture;
struct WinrtApi {
    HMODULE module = nullptr;
    BOOL (*supported)() = nullptr;
    WinrtCapture *(*init)(BOOL, HWND, BOOL, BOOL) = nullptr;
    void (*free)(WinrtCapture *) = nullptr;
    BOOL (*active)(const WinrtCapture *) = nullptr;
    void (*render)(WinrtCapture *) = nullptr;
    uint32_t (*width)(const WinrtCapture *) = nullptr;
    uint32_t (*height)(const WinrtCapture *) = nullptr;
    gs_color_space (*color_space)(const WinrtCapture *) = nullptr;
};
inline WinrtApi winrt_api;
inline constexpr const char *exact_window_source_id = "flick_exact_window_capture";
struct ExactWindowSource {
    HWND window = nullptr;
    HANDLE process = nullptr;
    ProcessIdentity identity;
    WinrtCapture *capture = nullptr;
    bool attempted = false, loss_registered = false;
    std::atomic<bool> terminal{false};
    std::atomic<uint32_t> width{0}, height{0};
};
inline bool bound_window_alive(ExactWindowSource *source) {
    DWORD pid = 0;
    if (!source || source->terminal || !source->process || WaitForSingleObject(source->process, 0) != WAIT_TIMEOUT ||
        !GetWindowThreadProcessId(source->window, &pid) || pid != source->identity.pid || !IsWindow(source->window) ||
        !IsWindowVisible(source->window) || IsIconic(source->window)) return false;
    DWORD cloaked = 0;
    return SUCCEEDED(DwmGetWindowAttribute(source->window, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && !cloaked;
}
inline void retire_exact_window(ExactWindowSource *source) {
    source->terminal = true;
    source->width = 0; source->height = 0;
    if (source->capture) { winrt_api.free(source->capture); source->capture = nullptr; }
}
inline void exact_device_loss_release(void *data) {
    // The stock WinRT callback would rebuild from a raw, possibly recycled
    // HWND. Retire instead. Our callback remains registered until destruction;
    // freeing the capture removes only its already-replaced stock callback.
    retire_exact_window(static_cast<ExactWindowSource *>(data));
}
inline void exact_device_loss_rebuild(void *, void *) { /* A new consented session is required. */ }
inline void *create_exact_window(obs_data_t *settings, obs_source_t *) {
    auto *source = new ExactWindowSource;
    source->window = reinterpret_cast<HWND>(static_cast<uintptr_t>(obs_data_get_int(settings, "hwnd")));
    source->identity.pid = static_cast<DWORD>(obs_data_get_int(settings, "pid"));
    source->identity.creation_time = static_cast<uint64_t>(obs_data_get_int(settings, "creation_time"));
    if (!from_utf8(obs_data_get_string(settings, "executable_path"), source->identity.executable_path) ||
        !window_matches_identity(source->window, source->identity)) { delete source; return nullptr; }
    source->process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, source->identity.pid);
    ProcessIdentity actual;
    if (!source->process || !process_identity_from_handle(source->process, source->identity.pid, actual) ||
        !same_process_identity(source->identity, actual) || !bound_window_alive(source)) {
        if (source->process) CloseHandle(source->process);
        delete source; return nullptr;
    }
    return source;
}
inline void tick_exact_window(void *data, float) {
    auto *source = static_cast<ExactWindowSource *>(data);
    if (!source) return;
    if (!bound_window_alive(source)) { retire_exact_window(source); return; }
    if (!source->attempted) {
        source->attempted = true;
        if (!winrt_api.supported()) { retire_exact_window(source); return; }
        obs_enter_graphics();
        source->capture = winrt_api.init(TRUE, source->window, FALSE, TRUE);
        if (source->capture) {
            // Replace automatic device-loss rebinding with a terminal stop.
            gs_unregister_loss_callbacks(source->capture);
            gs_device_loss loss{exact_device_loss_release, exact_device_loss_rebuild, source};
            gs_register_loss_callbacks(&loss);
            source->loss_registered = true;
        }
        obs_leave_graphics();
        if (!source->capture || !bound_window_alive(source)) { retire_exact_window(source); return; }
    }
    if (source->capture && !winrt_api.active(source->capture)) { retire_exact_window(source); return; }
    source->width = source->capture ? winrt_api.width(source->capture) : 0;
    source->height = source->capture ? winrt_api.height(source->capture) : 0;
}
inline void render_exact_window(void *data, gs_effect_t *) {
    auto *source = static_cast<ExactWindowSource *>(data);
    if (!source) return;
    if (!bound_window_alive(source)) { source->terminal = true; source->width = 0; source->height = 0; return; }
    if (source->capture && winrt_api.active(source->capture)) winrt_api.render(source->capture);
}
inline void destroy_exact_window_on_graphics(void *data) {
    auto *source = static_cast<ExactWindowSource *>(data);
    if (source->loss_registered) {
        obs_enter_graphics(); gs_unregister_loss_callbacks(source); obs_leave_graphics();
    }
    retire_exact_window(source);
    if (source->process) CloseHandle(source->process);
    delete source;
}
inline void destroy_exact_window(void *data) {
    if (!data) return;
    obs_queue_task(OBS_TASK_GRAPHICS, destroy_exact_window_on_graphics, data, false);
}
inline uint32_t exact_width(void *data) { return data ? static_cast<ExactWindowSource *>(data)->width.load() : 0; }
inline uint32_t exact_height(void *data) { return data ? static_cast<ExactWindowSource *>(data)->height.load() : 0; }
inline const char *exact_name(void *) { return "Flick Exact Window"; }
inline gs_color_space exact_color_space(void *, size_t, const gs_color_space *) { return GS_CS_SRGB; }
inline bool register_exact_window_source(const std::filesystem::path &runtime_bin) {
    if (!winrt_api.module) {
        // Retained through OBS shutdown and queued texture/event destruction.
        // Libobs loads this same pinned runtime file for its graphics dispatcher.
        winrt_api.module = LoadLibraryExW((runtime_bin / L"libobs-winrt.dll").c_str(), nullptr,
                                         LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!winrt_api.module) return false;
#define FLICK_WINRT(member, name) winrt_api.member = reinterpret_cast<decltype(winrt_api.member)>(GetProcAddress(winrt_api.module, name)); if (!winrt_api.member) return false
        FLICK_WINRT(supported, "winrt_capture_supported");
        FLICK_WINRT(init, "winrt_capture_init_window"); FLICK_WINRT(free, "winrt_capture_free");
        FLICK_WINRT(active, "winrt_capture_active"); FLICK_WINRT(render, "winrt_capture_render");
        FLICK_WINRT(width, "winrt_capture_width"); FLICK_WINRT(height, "winrt_capture_height");
        FLICK_WINRT(color_space, "winrt_capture_get_color_space");
#undef FLICK_WINRT
    }
    if (!winrt_api.supported || !winrt_api.init || !winrt_api.free || !winrt_api.active || !winrt_api.render ||
        !winrt_api.width || !winrt_api.height || !winrt_api.color_space) return false;
    obs_source_info info{};
    info.id = exact_window_source_id; info.type = OBS_SOURCE_TYPE_INPUT;
    info.output_flags = OBS_SOURCE_VIDEO | OBS_SOURCE_CUSTOM_DRAW | OBS_SOURCE_SRGB;
    info.get_name = exact_name; info.create = create_exact_window; info.destroy = destroy_exact_window;
    info.video_tick = tick_exact_window; info.video_render = render_exact_window;
    info.get_width = exact_width; info.get_height = exact_height; info.video_get_color_space = exact_color_space;
    info.icon_type = OBS_ICON_TYPE_WINDOW_CAPTURE;
    obs_register_source(&info);
    return true;
}
}
