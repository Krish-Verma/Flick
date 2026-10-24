#include "../../src/LocalClip.CaptureHost/encoder_policy.hpp"
#include "../../src/LocalClip.CaptureHost/diagnostic_policy.hpp"
#include <iostream>

using namespace flick::capture;

static void require(bool condition, const char *message) {
    if (!condition) throw std::runtime_error(message);
}

int main() {
    try {
        const auto silent = audio_routing(false, false);
        require(silent.tracks == std::vector<std::string>{"Silent Diagnostic Audio"} &&
                silent.system_mixers == 0 && silent.microphone_mixers == 0,
                "A no-audio diagnostic keeps one silent AAC mix without a capture source");
        const auto mic_only = audio_routing(false, true);
        require(mic_only.tracks == std::vector<std::string>{"Combined Mix", "Microphone"} &&
                mic_only.system_mixers == 0 && mic_only.microphone_mixers == 3,
                "Mic-only capture routes to the actual second mix rather than absent mix two");
        const auto normal = audio_routing(true, true);
        require(normal.tracks == std::vector<std::string>{"Combined Mix", "System Audio", "Microphone"} &&
                normal.system_mixers == 3 && normal.microphone_mixers == 5,
                "Existing normal three-track routing remains unchanged");
        require(expected_window_matches(23, "Test", "fixture.exe", 23, "Test", "fixture.exe") &&
                !expected_window_matches(23, "Test", "fixture.exe", 24, "Test", "fixture.exe") &&
                !expected_window_matches(23, "Test", "fixture.exe", 23, "Other", "fixture.exe") &&
                !expected_window_matches(23, "Test", "fixture.exe", 23, "Test", "other.exe") &&
                !expected_window_matches(0, "", "", 23, "Test", "fixture.exe"),
                "Consented window identity rejects reused handles and incomplete expectations");
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

        require(select_monitor({"display-b", "display-a"}, "display-a", 0) == 1,
                "A supplied monitor identity wins over a stale enumeration index");
        for (const auto &request : {std::pair<std::string, int>{"missing-display", 0}, {"", -1}, {"", 2}}) {
            bool rejected = false;
            try { (void)select_monitor({"display-a", "display-b"}, request.first, request.second); }
            catch (const std::invalid_argument &) { rejected = true; }
            require(rejected, "Unavailable monitors must never select the first available display");
        }
        require(window_identity("Game: #3A — 日本語", "Class:#", "game.exe") ==
                    "Game#3A #223A — 日本語:Class#3A#22:game.exe", "OBS window identity escapes separators without corrupting Unicode or escape-like text");
        require(window_identity(std::string(4096, 'x'), "Class", "game.exe").size() > 4096,
                "Long window identities are dynamically sized");
        bool missing_identity = false;
        try { (void)window_identity("game", "Class", "unknown"); } catch (const std::invalid_argument &) { missing_identity = true; }
        require(missing_identity, "An inaccessible window executable cannot become an approximate target");
        for (const auto &limits : {std::pair<int, int>{0, 256}, {301, 256}, {30, 15}, {30, 2049}}) {
            bool rejected = false;
            try { validate_replay_limits(limits.first, limits.second); } catch (const std::invalid_argument &) { rejected = true; }
            require(rejected, "Replay duration and memory are bounded before engine initialization");
        }
        validate_replay_limits(1, 16); validate_replay_limits(300, 2048);

        SaveLifecycle save;
        require(!save.begin("../unsafe") && !save.begin("") && !save.begin(std::string(129, 'x')),
                "Invalid save identities are rejected before starting a mux");
        require(save.begin("first-id") && !save.begin("second-id") && !save.complete("second-id"),
                "An accepted save owns its ID through the terminal callback");
        require(save.fail_pending() == "first-id" && !save.pending() && !save.begin("second-id") && !save.complete("first-id"),
                "A timed out mux blocks new requests and rejects its late completion");
        require(save.fail_pending().empty(), "A timeout emits exactly one terminal failure");
        save.reset();
        require(save.begin("new-output") && save.complete("new-output") && !save.pending(),
                "Only a fresh output reset permits saving after timeout");

        ReplayHistory history;
        history.reset(300, 16, 30);
        history.observe({0, 0, 100, true, false});
        require(!history.snapshot().save_ready && history.snapshot().seconds == 0, "No keyframe means no decodable retained history");
        history.observe({33333, 33333, 100, true, true});
        require(!history.snapshot().save_ready, "A single keyframe is not a complete replay");
        history.observe({66666, 66666, 100, true, false});
        require(history.snapshot().save_ready && history.snapshot().seconds > 0, "Keyframe and following video make partial replay saveable");
        history.reset(300, 16, 30);
        for (int frame = 0; frame < 30 * 600; ++frame) {
            const auto stamp = static_cast<int64_t>(frame) * 1000000 / 30;
            history.observe({stamp, stamp, 100000, true, frame % 60 == 0});
            history.observe({stamp, stamp, 1000, false, false});
        }
        auto retained = history.snapshot();
        require(retained.save_ready && retained.seconds > 1 && retained.seconds < 10 && !retained.tracking_limited,
                "Ten minutes elapsed never becomes a claimed300s replay when a16MiB memory cap retains only a few GOPs");
        history.reset(4, 2048, 30);
        for (int frame = 0; frame < 30 * 30; ++frame) {
            const auto stamp = static_cast<int64_t>(frame) * 1000000 / 30;
            history.observe({stamp, stamp, 100, true, frame % 60 == 0});
        }
        require(history.snapshot().save_ready && history.snapshot().seconds <= 4.1,
                "Time-based whole-GOP trimming measures retained video rather than total encoded uptime");
        history.reset(300, 2048, 30);
        for (size_t n = 0; n <= ReplayHistory::maximum_packets; ++n)
            history.observe({static_cast<int64_t>(n), static_cast<int64_t>(n), 1, false, false});
        require(history.snapshot().packet_count < ReplayHistory::maximum_packets && history.snapshot().tracking_limited && !history.snapshot().save_ready,
                "Metadata exhaustion is bounded and cannot invent retained decodable video");
        history.reset(30, 16, 30);
        history.observe({0, 0, 17 * 1024 * 1024, true, true});
        require(history.snapshot().tracking_limited && history.snapshot().packet_count == 0,
                "A single oversized packet cannot cause a purge loop or unbounded observer memory");

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
        std::cout << "PASS: encoder policy, target identity, replay retention, save ownership and retry lifecycle\n";
        return 0;
    } catch (const std::exception &error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
