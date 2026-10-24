#pragma once
// SPDX-License-Identifier: GPL-2.0-or-later
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <string>
#include <utility>
// The OBS 32.2.2 wasapi_process_output_capture source resolves title/class/exe
// and exposes no actual PID. This source uses the same documented Windows
// process-loopback API with an immutable PID and retained process handle.
// It never reconnects to a different process or opens an endpoint fallback.
#include <audioclient.h>

#include <mmdeviceapi.h>
#include <ks.h>
#include <ksmedia.h>
#include <tlhelp32.h>
#include <atomic>
#include <cmath>
#include <mutex>
#include <thread>
#include <vector>
#include "obs.h"

namespace flick::audio {
inline std::wstring wide(const std::string &text) {
    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (count <= 0) return {};
    std::wstring result(count, 0);
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), result.data(), count)) return {};
    return result;
}
inline bool process_matches(HANDLE process, uint64_t birth, const std::wstring &path) {
    if (!process || WaitForSingleObject(process, 0) != WAIT_TIMEOUT) return false;
    FILETIME created{}, exited{}, kernel{}, user{};
    wchar_t image[32768]; DWORD size = 32768;
    if (!GetProcessTimes(process, &created, &exited, &kernel, &user) || !QueryFullProcessImageNameW(process, 0, image, &size)) return false;
    const uint64_t actual = (static_cast<uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime;
    return actual == birth && !path.empty() && CompareStringOrdinal(image, static_cast<int>(size), path.data(), static_cast<int>(path.size()), TRUE) == CSTR_EQUAL;
}
// ABI from the public Windows SDK audioclientactivationparams.h. MinGW's
// distributed headers omit this small activation record; no SDK import is
// required because ActivateAudioInterfaceAsync is resolved dynamically.
struct ProcessLoopbackParameters { DWORD target_process_id; int mode; };
struct ActivationParameters { int type; ProcessLoopbackParameters process; };
static_assert(sizeof(ActivationParameters) == 12, "Windows process-loopback ABI");
inline constexpr GUID audio_client_iid = {0x1cb9ad4c,0xdbfa,0x4c32,{0xb1,0x78,0xc2,0xf5,0x68,0xa7,0x03,0xb2}};
inline constexpr GUID capture_client_iid = {0xc8adbd64,0xe71e,0x48a0,{0xa4,0xde,0x18,0x5c,0x39,0x5c,0xd3,0x17}};
inline constexpr GUID activation_handler_iid = {0x41d949ab,0x9862,0x444a,{0x80,0xf6,0xc2,0x61,0x33,0x4d,0xa5,0xeb}};
inline constexpr GUID agile_iid = {0x94ea2b94,0xe9cc,0x49e0,{0xc0,0xff,0xee,0x64,0xca,0x8f,0x5b,0x90}};
inline constexpr GUID unknown_iid = {0,0,0,{0xc0,0,0,0,0,0,0,0x46}};
class Activation final : public IActivateAudioInterfaceCompletionHandler {
    std::atomic<ULONG> refs{1};
    std::mutex gate;
    IAudioClient *client = nullptr;
public:
    HANDLE done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HRESULT result = E_PENDING;
    ~Activation() { if (client) client->Release(); if (done) CloseHandle(done); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void **out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (IsEqualGUID(iid, activation_handler_iid) || IsEqualGUID(iid, unknown_iid) || IsEqualGUID(iid, agile_iid)) {
            *out = static_cast<IActivateAudioInterfaceCompletionHandler*>(this); AddRef(); return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { const auto value = --refs; if (!value) delete this; return value; }
    HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation *operation) override {
        IUnknown *value = nullptr; HRESULT status = E_FAIL;
        const HRESULT call = operation->GetActivateResult(&status, &value);
        {
            std::lock_guard<std::mutex> lock(gate);
            result = FAILED(call) ? call : status;
            if (SUCCEEDED(result) && value) result = value->QueryInterface(audio_client_iid, reinterpret_cast<void**>(&client));
        }
        if (value) value->Release();
        SetEvent(done); return S_OK;
    }
    IAudioClient *take() { std::lock_guard<std::mutex> lock(gate); auto *value = client; client = nullptr; return value; }
};
using ActivateAudio = HRESULT (WINAPI *)(LPCWSTR, REFIID, PROPVARIANT *, IActivateAudioInterfaceCompletionHandler *, IActivateAudioInterfaceAsyncOperation **);
inline bool supported() {
    // Microsoft's supported minimum is 20348 (OBS experimentally registers on
    // 19041). Use the documented floor and still verify real activation.
    using RtlVersion = LONG (WINAPI *)(OSVERSIONINFOW*);
    auto get = reinterpret_cast<RtlVersion>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));
    OSVERSIONINFOW version{}; version.dwOSVersionInfoSize = sizeof(version);
    return get && get(&version) == 0 && version.dwMajorVersion >= 10 && version.dwBuildNumber >= 20348;
}
class ProcessSource {
    obs_source_t *source;
    HANDLE stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE process = nullptr;
    std::thread worker;
    DWORD pid;
    uint64_t birth;
    std::wstring path;
public:
    std::atomic<bool> attached{false}, failed{false}, signal{false};
    ProcessSource(obs_source_t *source_, DWORD pid_, uint64_t birth_, std::wstring path_)
        : source(source_), pid(pid_), birth(birth_), path(std::move(path_)) {
        process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid);
        if (!stop || !ready || !process_matches(process, birth, path)) { failed = true; return; }
        worker = std::thread([this] { run(); });
        if (WaitForSingleObject(ready, 10000) != WAIT_OBJECT_0) { failed = true; SetEvent(stop); }
    }
    ~ProcessSource() {
        if (stop) SetEvent(stop);
        if (worker.joinable()) worker.join();
        if (process) CloseHandle(process);
        if (ready) CloseHandle(ready);
        if (stop) CloseHandle(stop);
    }
private:
    void run() {
        const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        static HMODULE module = LoadLibraryW(L"Mmdevapi.dll");
        auto activate = module ? reinterpret_cast<ActivateAudio>(GetProcAddress(module, "ActivateAudioInterfaceAsync")) : nullptr;
        auto *completion = new Activation();
        IActivateAudioInterfaceAsyncOperation *operation = nullptr;
        IAudioClient *client = nullptr; IAudioCaptureClient *capture = nullptr;
        HANDLE samples = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        bool started = false;
        do {
            if (FAILED(com) || !activate || !completion->done || !samples || !supported()) break;
            ActivationParameters parameters{};
            parameters.type = 1; // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            parameters.process.target_process_id = pid;
            parameters.process.mode = 0; // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
            PROPVARIANT variant{}; variant.vt = VT_BLOB;
            variant.blob.cbSize = sizeof(parameters); variant.blob.pBlobData = reinterpret_cast<BYTE*>(&parameters);
            HRESULT result = activate(L"VAD\\Process_Loopback", audio_client_iid, &variant, completion, &operation);
            if (FAILED(result)) break;
            HANDLE waits[] = {stop, completion->done};
            if (WaitForMultipleObjects(2, waits, FALSE, 8000) != WAIT_OBJECT_0 + 1) break;
            client = completion->take();
            if (!client || !process_matches(process, birth, path)) break;
            WAVEFORMATEXTENSIBLE format{};
            format.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE; format.Format.nChannels = 2;
            format.Format.nSamplesPerSec = 48000; format.Format.wBitsPerSample = 32;
            format.Format.nBlockAlign = 8; format.Format.nAvgBytesPerSec = 384000;
            format.Format.cbSize = sizeof(format) - sizeof(WAVEFORMATEX);
            format.Samples.wValidBitsPerSample = 32; format.dwChannelMask = 3;
            format.SubFormat = {0x00000003,0x0000,0x0010,{0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71}};
            result = client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                                       0, 0, &format.Format, nullptr);
            if (FAILED(result) || FAILED(client->SetEventHandle(samples)) ||
                FAILED(client->GetService(capture_client_iid, reinterpret_cast<void**>(&capture))) || FAILED(client->Start())) break;
            started = true; attached = true; SetEvent(ready);
            HANDLE events[] = {stop, samples};
            while (WaitForMultipleObjects(2, events, FALSE, 250) != WAIT_OBJECT_0) {
                if (!process_matches(process, birth, path)) break;
                UINT32 packet = 0;
                HRESULT packet_result = capture->GetNextPacketSize(&packet);
                if (FAILED(packet_result)) break;
                while (packet) {
                    BYTE *data = nullptr; UINT32 frames = 0; DWORD flags = 0; UINT64 position = 0, qpc = 0;
                    packet_result = capture->GetBuffer(&data, &frames, &flags, &position, &qpc);
                    if (FAILED(packet_result)) break;
                    std::vector<float> silence;
                    if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) || !data) { silence.resize(static_cast<size_t>(frames) * 2); data = reinterpret_cast<BYTE*>(silence.data()); }
                    const auto *values = reinterpret_cast<float*>(data);
                    if (!signal) for (size_t i = 0; i < static_cast<size_t>(frames) * 2; ++i) if (std::abs(values[i]) > 0.00001f) { signal = true; break; }
                    obs_source_audio audio{}; audio.data[0] = data; audio.frames = frames;
                    audio.speakers = SPEAKERS_STEREO; audio.format = AUDIO_FORMAT_FLOAT;
                    audio.samples_per_sec = 48000; audio.timestamp = qpc * 100;
                    obs_source_output_audio(source, &audio);
                    capture->ReleaseBuffer(frames);
                    packet_result = capture->GetNextPacketSize(&packet);
                    if (FAILED(packet_result)) break;
                }
                if (FAILED(packet_result)) break;
            }
        } while (false);
        attached = false;
        if (WaitForSingleObject(stop, 0) != WAIT_OBJECT_0) failed = true;
        SetEvent(ready);
        if (started) client->Stop();
        if (capture) capture->Release();
        if (client) client->Release();
        if (operation) operation->Release();
        completion->Release();
        if (samples) CloseHandle(samples);
        // Keep Mmdevapi loaded: a late async activation callback can complete
        // after our bounded wait, independently owning its completion object.
        if (SUCCEEDED(com)) CoUninitialize();
    }
};
inline void register_process_audio() {
    obs_source_info info{};
    info.id = "flick_exact_process_audio"; info.type = OBS_SOURCE_TYPE_INPUT; info.output_flags = OBS_SOURCE_AUDIO;
    info.get_name = [](void*) { return "Flick exact process audio"; };
    info.create = [](obs_data_t *settings, obs_source_t *source) -> void* {
        auto *value = new ProcessSource(source, static_cast<DWORD>(obs_data_get_int(settings, "pid")),
            static_cast<uint64_t>(obs_data_get_int(settings, "birth")), wide(obs_data_get_string(settings, "path")));
        if (!value->attached) { delete value; return nullptr; }
        proc_handler_add(obs_source_get_proc_handler(source),
            "void get_audio_status(out bool attached, out bool failed, out bool signal_observed)",
            [](void *data, calldata_t *call) {
                auto *state = static_cast<ProcessSource*>(data);
                calldata_set_bool(call, "attached", state->attached);
                calldata_set_bool(call, "failed", state->failed);
                calldata_set_bool(call, "signal_observed", state->signal);
            }, value);
        return value;
    };
    info.destroy = [](void *data) { delete static_cast<ProcessSource*>(data); };
    obs_register_source(&info);
}
inline bool is_ancestor(DWORD ancestor, DWORD child) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return true; // ambiguous overlap fails closed
    std::vector<std::pair<DWORD, DWORD>> parents;
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    if (!Process32FirstW(snapshot, &entry)) { CloseHandle(snapshot); return true; }
    do { parents.emplace_back(entry.th32ProcessID, entry.th32ParentProcessID); } while (Process32NextW(snapshot, &entry));
    if (GetLastError() != ERROR_NO_MORE_FILES) { CloseHandle(snapshot); return true; }
    CloseHandle(snapshot);
    for (size_t depth = 0; child && depth <= parents.size(); ++depth) {
        if (child == ancestor) return true;
        auto found = std::find_if(parents.begin(), parents.end(), [&](const auto &pair) { return pair.first == child; });
        if (found == parents.end() || found->second == child) break;
        child = found->second;
    }
    return false;
}
}
