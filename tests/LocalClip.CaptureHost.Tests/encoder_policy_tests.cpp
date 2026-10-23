#include "../../src/LocalClip.CaptureHost/encoder_policy.hpp"
#include <iostream>

using namespace flick::capture;

static void require(bool condition, const char *message) {
    if (!condition) throw std::runtime_error(message);
}

int main() {
    try {
        const std::vector<std::string> registered{"obs_x264", "hevc_nvenc", "h264_texture_amf", "obs_qsv11_v2", "obs_nvenc_h264_tex"};
        auto automatic = candidates("auto", true, registered);
        require(automatic.size() == 4, "Only registered known H264 encoders are eligible");
        require(std::string(automatic[0].id) == "obs_nvenc_h264_tex" && std::string(automatic[1].id) == "obs_qsv11_v2" &&
                std::string(automatic[2].id) == "h264_texture_amf" && automatic[3].software, "Hardware order precedes CPU");
        auto explicit_amd = candidates("h264_texture_amf", true, registered);
        require(explicit_amd.size() == 2 && std::string(explicit_amd[0].id) == "h264_texture_amf" && explicit_amd[1].software,
                "Explicit hardware must not silently switch vendors");
        require(candidates("auto", false, {"obs_x264"}).empty(), "Software opt-out is respected");
        require(candidates("obs_nvenc_h264_tex", true, {"obs_x264"}).size() == 1, "Missing requested hardware can use permitted CPU");
        require(candidates("auto", true, {"unknown", "hevc_nvenc"}).empty(), "Unknown or non-H264 registrations never enter policy");
        for (const auto &request : {std::string("not_an_encoder"), std::string("obs_x264")}) {
            bool rejected = false;
            try { (void)candidates(request, false, registered); } catch (const std::invalid_argument &) { rejected = true; }
            require(rejected, "Unknown IDs and forbidden explicit software must fail");
        }
        const auto &cpu = *find_encoder("obs_x264");
        for (const auto &requested : {Profile{3840, 2160, 120, 15000}, Profile{3440, 1440, 60, 12000},
                                     Profile{1080, 1920, 60, 9000}, Profile{640, 480, 15, 3000}}) {
            const auto actual = profile_for(cpu, requested);
            require(actual.width <= 1280 && actual.height <= 720 && actual.width % 2 == 0 && actual.height % 2 == 0,
                    "CPU dimensions are bounded even pixels");
            require(actual.width <= requested.width && actual.height <= requested.height && actual.fps <= 30 && actual.bitrate_kbps <= 6000,
                    "CPU must never upscale or exceed its budget");
            const auto hardware = profile_for(*find_encoder("obs_qsv11_v2"), requested);
            require(hardware.width == requested.width && hardware.height == requested.height && hardware.fps == requested.fps,
                    "Hardware profile remains the requested profile");
            const double aspect_error = std::abs(static_cast<double>(actual.width) / actual.height - static_cast<double>(requested.width) / requested.height);
            require(aspect_error < .01, "CPU geometry preserves aspect ratio within even-pixel rounding");
        }
        require(software_threads(0) == 1 && software_threads(1) == 1 && software_threads(4) == 2 && software_threads(128) == 2,
                "Software thread count is bounded even when CPU count is unavailable");
        bool invalid_profile = false;
        try { (void)profile_for(cpu, {1921, 1080, 30, 15000}); } catch (const std::invalid_argument &) { invalid_profile = true; }
        require(invalid_profile, "Invalid geometry is rejected before attempting OBS");

        // Model encoder creation succeeding but real output start rejecting it:
        // the failed pipeline must be cleaned before the next encoder/profile.
        bool live = false;
        int attempts = 0, cleanups = 0;
        bool started = start_candidates(automatic, [&](const Encoder &encoder) {
            require(!live, "Previous pipeline must be released before retry");
            live = true; ++attempts;
            return encoder.software ? Attempt::started : Attempt::retryable_failure;
        }, [&] { require(live, "Cleanup follows a live failed attempt"); live = false; ++cleanups; });
        require(started && attempts == 4 && cleanups == 3 && live, "Output-start failure retries through CPU and retains success");
        live = false; attempts = cleanups = 0;
        require(!start_candidates(automatic, [&](const Encoder &) { ++attempts; return Attempt::fatal_failure; }, [&] { ++cleanups; }),
                "Source/audio/output configuration failure is fatal");
        require(attempts == 1 && cleanups == 1, "Fatal errors never retry other vendors");
        attempts = cleanups = 0;
        require(!start_candidates(automatic, [&](const Encoder &) { ++attempts; return Attempt::retryable_failure; }, [&] { ++cleanups; }),
                "Exhausted encoders report failure");
        require(attempts == 4 && cleanups == 4, "Every exhausted pipeline is released");
        bool propagated = false;
        try {
            (void)start_candidates(automatic, [](const Encoder &) -> Attempt { throw std::runtime_error("injected creation error"); }, [&] { ++cleanups; });
        } catch (const std::runtime_error &) { propagated = true; }
        require(propagated && cleanups == 5, "Exceptions clean the partial pipeline and propagate");
        std::cout << "PASS: encoder registration/order, explicit/CPU policy, profile limits and retry lifecycle\n";
        return 0;
    } catch (const std::exception &error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
