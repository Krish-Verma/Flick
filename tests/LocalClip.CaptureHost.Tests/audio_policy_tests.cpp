#include "../../src/LocalClip.CaptureHost/audio_policy.hpp"
#include <iostream>
static void require(bool value) { if (!value) throw std::runtime_error("audio policy assertion failed"); }
int main() {
    using namespace flick::audio;
    auto off = routing(false, false, false, false, false);
    require(off.tracks.size() == 1 && !off.system && !off.microphone);
    auto system = routing(false, true, false, false, true);
    require(system.tracks.size() == 3 && system.system == 3 && system.microphone == 5);
    auto separated = routing(true, false, true, true, true);
    require(separated.tracks.size() == 4 && separated.game == 3 && separated.voice == 5 && separated.microphone == 9 && !separated.system);
    bool refused = false;
    try { routing(true, true, true, false, false); } catch (const std::invalid_argument&) { refused = true; }
    require(refused);
    require(!process_identity_valid(42, 0, "path") && !process_identity_valid(0, 123, "path") && !process_identity_valid(42, 123, ""));
    require(process_identity_valid(42, 123, "path"));
    require(!distinct_processes(42, 42) && distinct_processes(42, 43));
    std::cout << "Audio policy checks passed\n";
}
