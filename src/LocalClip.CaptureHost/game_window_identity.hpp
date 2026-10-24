#pragma once
#include <windows.h>
#include <cstdint>
#include <string>
#include <vector>

namespace flick::game {
struct ProcessIdentity { DWORD pid = 0; uint64_t creation_time = 0; std::wstring executable_path; };
inline bool from_utf8(const std::string &text, std::wstring &wide) {
    if (text.empty() || text.size() > 131068 || text.find('\0') != std::string::npos) return false;
    int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (length <= 0) return false;
    wide.resize(static_cast<size_t>(length));
    return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), wide.data(), length) == length;
}
inline bool process_identity_from_handle(HANDLE process, DWORD pid, ProcessIdentity &identity) {
    std::vector<wchar_t> path(32768);
    DWORD length = static_cast<DWORD>(path.size());
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!process || !pid || !QueryFullProcessImageNameW(process, 0, path.data(), &length) ||
        !GetProcessTimes(process, &created, &exited, &kernel, &user)) return false;
    identity = {pid, (static_cast<uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime,
                std::wstring(path.data(), length)};
    return identity.creation_time != 0 && !identity.executable_path.empty();
}
inline bool query_process_identity(DWORD pid, ProcessIdentity &identity) {
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid);
    if (!process) return false;
    bool valid = WaitForSingleObject(process, 0) == WAIT_TIMEOUT && process_identity_from_handle(process, pid, identity);
    CloseHandle(process);
    return valid;
}
inline bool same_process_identity(const ProcessIdentity &expected, const ProcessIdentity &actual) {
    return expected.pid && expected.creation_time && !expected.executable_path.empty() && expected.pid == actual.pid &&
           expected.creation_time == actual.creation_time &&
           CompareStringOrdinal(expected.executable_path.c_str(), -1, actual.executable_path.c_str(), -1, TRUE) == CSTR_EQUAL;
}
inline bool window_matches_identity(HWND window, const ProcessIdentity &expected) {
    DWORD before = 0, after = 0;
    if (!IsWindow(window) || !GetWindowThreadProcessId(window, &before) || before != expected.pid) return false;
    ProcessIdentity actual;
    return query_process_identity(before, actual) && same_process_identity(expected, actual) &&
           GetWindowThreadProcessId(window, &after) && after == before && IsWindow(window);
}
}
