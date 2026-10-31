using System.Text.Json;
using System.IO;
using LocalClip.Core.Ipc;

namespace LocalClip.Testing;

/// <summary>Owned stdin/stdout helper that only returns metadata. It never loads
/// OBS, captures pixels/audio, accesses a device, or produces a media file.</summary>
internal sealed class CaptureHostPipeFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Flick-Pipe-Fixture-" + Guid.NewGuid().ToString("N"));
    private readonly string _script;

    public CaptureHostPipeFixture(bool sourceIdentity = true, bool diagnosticNoAudio = true, string? refusal = null)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, ".flick-owned-fixture"), "Metadata-only test helper");
        File.WriteAllText(Path.Combine(Root, "config.json"), JsonSerializer.Serialize(new { sourceIdentity, diagnosticNoAudio, refusal }));
        _script = Path.Combine(Root, "helper.ps1");
        File.WriteAllText(_script, """
            $ErrorActionPreference = 'Stop'
            $config = Get-Content -Raw (Join-Path $PSScriptRoot 'config.json') | ConvertFrom-Json
            $log = Join-Path $PSScriptRoot 'commands.jsonl'
            $encodingProfile = @{ encoder = 'fixture_h264'; encoder_name = 'Metadata fixture'; software_fallback = $false; width = 1280; height = 720; fps = 30 }
            $state = 'Off'
            function Send($value) {
                [Console]::Out.WriteLine(($value | ConvertTo-Json -Depth 12 -Compress))
                [Console]::Out.Flush()
            }
            while ($null -ne ($line = [Console]::In.ReadLine())) {
                $request = $line | ConvertFrom-Json
                Add-Content -LiteralPath $log -Value $line -Encoding UTF8
                $reply = @{ id = $request.id; ok = $true }
                switch ($request.cmd) {
                    'enum_targets' {
                        $reply.result = @{ windows = @(); monitors = @(@{ index = 7; device = 'fixture-monitor-id'; width = 1280; height = 720; primary = $true }) }
                    }
                    'get_capabilities' {
                        $reply.result = @{ protocol_version = 2; diagnostic_no_system_audio = [bool]$config.diagnosticNoAudio; source_identity_binding = [bool]$config.sourceIdentity; exact_window_binding = $true; exact_process_audio = $true }
                    }
                    'start_capture' {
                        if ($config.refusal -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'allow-start'))) { $reply.ok = $false; $reply.error = $config.refusal }
                        else {
                            $state = 'Buffering'
                            Send @{ event = 'state_changed'; data = @{ state = $state } }
                            $reply.result = $encodingProfile
                        }
                    }
                    'get_status' {
                        $reply.result = @{ state = $state; buffered_sec = 0; max_sec = 30; is_armed = $false; target_valid = $true; target_type = 'Monitor' }
                    }
                    'stop_capture' { $state = 'Off'; Send @{ event = 'state_changed'; data = @{ state = $state } } }
                    'shutdown' { Send $reply; return }
                    default { $reply.ok = $false; $reply.error = 'Unexpected fixture command' }
                }
                Send $reply
            }
            """);
    }

    public CaptureHostClient CreateClient() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + _script + "\"");

    public JsonElement[] Requests() => File.Exists(Path.Combine(Root, "commands.jsonl"))
        ? File.ReadAllLines(Path.Combine(Root, "commands.jsonl")).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray()
        : [];

    public void AllowCapture() => File.WriteAllText(Path.Combine(Root, "allow-start"), "Metadata-only startup now accepted");

    public void Dispose()
    {
        if (File.Exists(Path.Combine(Root, ".flick-owned-fixture"))) Directory.Delete(Root, true);
    }
}
