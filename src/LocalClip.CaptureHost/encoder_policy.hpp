#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <stdexcept>
#include <string>
#include <vector>

namespace flick::capture {

struct Encoder {
    const char *id;
    const char *name;
    bool software;
};

// OBS Studio 32.2.2: obs-nvenc/nvenc.c, obs-qsv11/obs-qsv11.c,
// obs-ffmpeg/texture-amf.cpp, obs-ffmpeg/obs-ffmpeg-nvenc.c, obs-x264/obs-x264.c.
// "soft"/"fallback" vendor IDs still encode on hardware, using CPU-memory input.
// jim_nvenc is accepted for older configurations only when actually registered.
inline constexpr std::array<Encoder, 11> encoders{{
    {"obs_nvenc_h264_tex", "NVIDIA NVENC H.264", false},
    {"obs_qsv11_v2", "Intel Quick Sync H.264", false},
    {"h264_texture_amf", "AMD AMF H.264", false},
    {"obs_nvenc_h264_soft", "NVIDIA NVENC H.264 (compatibility)", false},
    {"obs_qsv11_soft_v2", "Intel Quick Sync H.264 (compatibility)", false},
    {"h264_fallback_amf", "AMD AMF H.264 (compatibility)", false},
    {"ffmpeg_nvenc", "NVIDIA NVENC H.264 (FFmpeg)", false},
    {"obs_qsv11", "Intel Quick Sync H.264 (legacy)", false},
    {"obs_qsv11_soft", "Intel Quick Sync H.264 (legacy compatibility)", false},
    {"jim_nvenc", "NVIDIA NVENC H.264 (legacy)", false},
    {"obs_x264", "CPU x264 H.264", true},
}};

inline const Encoder *find_encoder(const std::string &id) {
    for (const auto &encoder : encoders)
        if (id == encoder.id) return &encoder;
    return nullptr;
}

inline std::vector<Encoder> candidates(const std::string &requested, bool allow_software,
                                       const std::vector<std::string> &registered) {
    if (requested != "auto" && !find_encoder(requested))
        throw std::invalid_argument("Unknown H.264 encoder. Choose auto or a supported NVIDIA, Intel, AMD or obs_x264 encoder ID.");
    if (requested == "obs_x264" && !allow_software)
        throw std::invalid_argument("CPU encoding is disabled by allow_software_fallback=false.");
    std::vector<Encoder> result;
    for (const auto &encoder : encoders) {
        if (std::find(registered.begin(), registered.end(), encoder.id) == registered.end()) continue;
        if (encoder.software ? allow_software : requested == "auto" || requested == encoder.id)
            result.push_back(encoder);
    }
    return result;
}

struct Profile { int width; int height; int fps; int bitrate_kbps; };

inline Profile profile_for(const Encoder &encoder, Profile requested) {
    if (requested.width < 320 || requested.width > 7680 || requested.height < 240 || requested.height > 4320 ||
        requested.width % 2 || requested.height % 2 || requested.fps < 1 || requested.fps > 120 ||
        requested.bitrate_kbps < 100 || requested.bitrate_kbps > 200000)
        throw std::invalid_argument("Unsupported capture dimensions, frame rate or bitrate.");
    if (!encoder.software) return requested;
    const double scale = (std::min)({1.0, 1280.0 / requested.width, 720.0 / requested.height});
    requested.width = (std::max)(2, static_cast<int>(std::floor(requested.width * scale)) & ~1);
    requested.height = (std::max)(2, static_cast<int>(std::floor(requested.height * scale)) & ~1);
    requested.fps = (std::min)(requested.fps, 30);
    requested.bitrate_kbps = (std::min)(requested.bitrate_kbps, 6000);
    return requested;
}

inline int software_threads(unsigned available) {
    // Leave gameplay headroom; x264 accepts threads through its documented x264opts parser.
    return static_cast<int>((std::max)(1u, (std::min)(2u, available > 1 ? available / 2 : 1u)));
}

enum class Attempt { started, retryable_failure, fatal_failure };

// Shared by the real OBS start path and portable lifecycle tests. Every failed
// attempt (including exceptions) is torn down before another profile is applied.
template<typename Start, typename Cleanup>
bool start_candidates(const std::vector<Encoder> &options, Start start, Cleanup cleanup) {
    for (const auto &encoder : options) {
        Attempt outcome;
        try { outcome = start(encoder); }
        catch (...) { cleanup(); throw; }
        if (outcome == Attempt::started) return true;
        cleanup();
        if (outcome == Attempt::fatal_failure) return false;
    }
    return false;
}

} // namespace flick::capture
