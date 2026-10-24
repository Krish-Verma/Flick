#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace flick::capture {
struct AudioRouting {
    std::vector<std::string> tracks;
    uint32_t system_mixers = 0;
    uint32_t microphone_mixers = 0;
};

inline AudioRouting audio_routing(bool system_audio, bool microphone) {
    AudioRouting result;
    result.tracks.push_back(system_audio || microphone ? "Combined Mix" : "Silent Diagnostic Audio");
    if (system_audio) {
        result.system_mixers = 1u | (1u << result.tracks.size());
        result.tracks.push_back("System Audio");
    }
    if (microphone) {
        result.microphone_mixers = 1u | (1u << result.tracks.size());
        result.tracks.push_back("Microphone");
    }
    return result;
}

inline bool expected_window_matches(uint32_t expected_pid, const std::string &expected_title,
                                    const std::string &expected_exe, uint32_t actual_pid,
                                    const std::string &actual_title, const std::string &actual_exe) {
    return expected_pid != 0 && !expected_title.empty() && !expected_exe.empty() &&
           expected_pid == actual_pid && expected_title == actual_title && expected_exe == actual_exe;
}
}
