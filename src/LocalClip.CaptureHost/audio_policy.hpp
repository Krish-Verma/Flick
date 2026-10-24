#pragma once
#include <stdexcept>
#include <string>
#include <vector>
#include <cstdint>

namespace flick::audio {
struct Routing {
    std::vector<std::string> tracks;
    uint32_t system = 0, game = 0, voice = 0, microphone = 0;
};
// Mixer zero is the combined playback track. Every real source has exactly
// one dedicated bus; never synthesize a Game/Voice bus from device loopback.
inline Routing routing(bool separated, bool system, bool game, bool voice, bool mic) {
    if (separated && system) throw std::invalid_argument("Separated audio cannot include whole-device loopback");
    if (!separated && (game || voice)) throw std::invalid_argument("Process audio requires separated mode");
    Routing result;
    if (!system && !game && !voice && !mic) { result.tracks = {"Silent Diagnostic Audio"}; return result; }
    result.tracks = {"Combined Audio"};
    auto add = [&](bool enabled, const char *name, uint32_t &mask) {
        if (!enabled) return;
        mask = 1u | (1u << result.tracks.size());
        result.tracks.emplace_back(name);
    };
    add(system, "System Audio (whole device)", result.system);
    add(game, "Game Audio (process tree)", result.game);
    add(voice, "Voice Audio (process tree)", result.voice);
    add(mic, "Microphone", result.microphone);
    return result;
}
inline bool process_identity_valid(uint32_t pid, uint64_t birth, const std::string &path) {
    return pid != 0 && birth != 0 && !path.empty() && path.size() <= 32768;
}
inline bool distinct_processes(uint32_t game, uint32_t voice) { return !game || !voice || game != voice; }
}
