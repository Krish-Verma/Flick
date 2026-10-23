#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <deque>
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

inline void validate_replay_limits(int duration_seconds, int memory_mib) {
    if (duration_seconds < 1 || duration_seconds > 300 || memory_mib < 16 || memory_mib > 2048)
        throw std::invalid_argument("Replay duration or buffer memory limit is unsupported.");
}

inline size_t select_monitor(const std::vector<std::string> &available, const std::string &id, int index) {
    if (!id.empty()) {
        const auto found = std::find(available.begin(), available.end(), id);
        if (found != available.end()) return static_cast<size_t>(std::distance(available.begin(), found));
    } else if (index >= 0 && static_cast<size_t>(index) < available.size()) {
        return static_cast<size_t>(index);
    }
    throw std::invalid_argument("Selected monitor is unavailable; refusing silent monitor fallback.");
}

inline std::string window_identity(const std::string &title, const std::string &window_class, const std::string &exe) {
    if (title.empty() || window_class.empty() || exe.empty() || exe == "unknown")
        throw std::invalid_argument("Selected window identity is unavailable; reselect the window.");
    // OBS 32.2.2 libobs/util/windows/window-helpers.c encode_dstr format.
    auto encode = [](const std::string &part) {
        std::string result;
        for (const char c : part) {
            if (c == '#') result += "#22";
            else if (c == ':') result += "#3A";
            else result += c;
        }
        return result;
    };
    return encode(title) + ":" + encode(window_class) + ":" + encode(exe);
}

inline bool valid_save_id(const std::string &id) {
    if (id.empty() || id.size() > 128) return false;
    return std::all_of(id.begin(), id.end(), [](unsigned char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
    });
}

// Accessed under the owner's state mutex. An expired mux can never be assigned
// a new request ID on the same output, even if its callback arrives late.
class SaveLifecycle {
public:
    bool begin(const std::string &id) {
        if (phase_ != Phase::idle || !valid_save_id(id)) return false;
        id_ = id; phase_ = Phase::pending; return true;
    }
    bool pending() const { return phase_ == Phase::pending; }
    const std::string &id() const { return id_; }
    bool complete(const std::string &id) {
        if (!pending() || id != id_) return false;
        phase_ = Phase::idle; return true;
    }
    std::string fail_pending() {
        if (!pending()) return {};
        phase_ = Phase::blocked; return id_;
    }
    void reset() { phase_ = Phase::idle; id_.clear(); }
private:
    enum class Phase { idle, pending, blocked };
    Phase phase_ = Phase::idle;
    std::string id_;
};

struct ReplayPacket {
    int64_t dts_us;
    int64_t pts_us;
    uint64_t bytes;
    bool video;
    bool keyframe;
};

struct ReplaySnapshot {
    double seconds = 0;
    bool save_ready = false;
    bool tracking_limited = false;
    size_t packet_count = 0;
};

// Metadata only: mirrors the pinned OBS 32.2.2 replay buffer's whole-GOP
// max_size/max_time purge rules, not a public inspection of its private deque.
// The owner supplies synchronization; callbacks never copy encoded payloads.
class ReplayHistory {
public:
    static constexpr size_t maximum_packets = 131072;

    void reset(int seconds, int memory_mib, int fps) {
        packets_.clear(); bytes_ = keyframes_ = 0; tracking_limited_ = false;
        maximum_us_ = static_cast<int64_t>(seconds) * 1000000;
        maximum_bytes_ = static_cast<uint64_t>(memory_mib) * 1024 * 1024;
        frame_us_ = fps > 0 ? 1000000 / fps : 0;
    }

    void observe(ReplayPacket packet) {
        if (!packet.bytes) return;
        // Never permit metadata growth or a single oversized packet to make
        // this observer unbounded. Rebuild from a subsequent keyframe.
        if (packets_.size() >= maximum_packets || packet.bytes > maximum_bytes_) {
            packets_.clear(); bytes_ = keyframes_ = 0; tracking_limited_ = true;
            if (packet.bytes > maximum_bytes_) return;
        }
        if (!packets_.empty() && keyframes_ > 2) {
            while (!packets_.empty() && bytes_ + packet.bytes > maximum_bytes_) purge();
        }
        if (!packets_.empty() && keyframes_ > 2) {
            while (!packets_.empty() && packet.dts_us - packets_.front().dts_us > maximum_us_) purge();
        }
        packets_.push_back(packet);
        bytes_ += packet.bytes;
        if (packet.video && packet.keyframe) ++keyframes_;
    }

    ReplaySnapshot snapshot() const {
        ReplaySnapshot result;
        result.tracking_limited = tracking_limited_;
        result.packet_count = packets_.size();
        bool found_keyframe = false;
        int64_t first_pts = 0, latest_pts = 0;
        size_t video_count = 0;
        for (const auto &packet : packets_) {
            if (!packet.video) continue;
            if (!found_keyframe) {
                if (!packet.keyframe) continue;
                found_keyframe = true;
                first_pts = latest_pts = packet.pts_us;
            }
            ++video_count;
            latest_pts = (std::max)(latest_pts, packet.pts_us);
        }
        result.save_ready = found_keyframe && video_count >= 2 && latest_pts > first_pts;
        if (result.save_ready)
            result.seconds = (std::max)(0.0, static_cast<double>(latest_pts - first_pts + frame_us_) / 1000000.0);
        return result;
    }

private:
    bool pop_front() {
        const auto packet = packets_.front();
        packets_.pop_front(); bytes_ -= packet.bytes;
        if (packet.video && packet.keyframe) { --keyframes_; return true; }
        return false;
    }
    void purge() {
        if (pop_front()) {
            while (!packets_.empty() && !(packets_.front().video && packets_.front().keyframe)) pop_front();
        }
    }
    std::deque<ReplayPacket> packets_;
    uint64_t bytes_ = 0;
    size_t keyframes_ = 0;
    int64_t maximum_us_ = 30000000, frame_us_ = 33333;
    uint64_t maximum_bytes_ = 256 * 1024 * 1024;
    bool tracking_limited_ = false;
};

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
