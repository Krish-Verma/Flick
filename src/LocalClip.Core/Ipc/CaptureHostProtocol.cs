using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalClip.Core.Ipc;

public class IpcRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = "";

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; set; }
}

public class IpcResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class IpcEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

public class CaptureHostStatus
{
    public string State { get; set; } = "Off";
    public double BufferedSec { get; set; }
    public double MaxSec { get; set; }
    public bool IsArmed { get; set; }
    public string TargetType { get; set; } = "";
    public bool TargetValid { get; set; } = true;
    public long PrivateBytes { get; set; }
    public long WorkingSet { get; set; }
    public CaptureEncodingProfile? Profile { get; set; }
    public string FaultReason { get; set; } = "";
    public bool BufferedDurationEstimated { get; set; } = true;
    public bool BufferTrackingLimited { get; set; }
    public IReadOnlyList<string> Tracks { get; set; } = Array.Empty<string>();

    public static CaptureHostStatus FromResponse(IpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Ok)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Error)
                ? "The capture helper could not report recording status."
                : response.Error);

        if (response.Result is not JsonElement data || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String ||
            state.GetString() is not ("Off" or "Starting" or "Buffering" or "Armed" or "Stopping" or "Paused" or "Faulted"))
            throw InvalidStatus();

        CaptureEncodingProfile.TryParse(data, out var profile);
        return new CaptureHostStatus
        {
            State = state.GetString()!,
            BufferedSec = ReadNumber(data, "buffered_sec", 0),
            MaxSec = ReadNumber(data, "max_sec", 30),
            IsArmed = ReadBoolean(data, "is_armed", false),
            TargetType = ReadString(data, "target_type"),
            TargetValid = ReadBoolean(data, "target_valid", false),
            PrivateBytes = ReadMemory(data, "private_bytes"),
            WorkingSet = ReadMemory(data, "working_set"),
            Profile = profile,
            FaultReason = ReadString(data, "fault_reason"),
            BufferedDurationEstimated = ReadBoolean(data, "buffered_duration_estimated", true),
            BufferTrackingLimited = ReadBoolean(data, "buffer_tracking_limited", false),
            Tracks = ReadTracks(data)
        };
    }

    private static InvalidDataException InvalidStatus() =>
        new("The capture helper returned invalid recording status.");

    private static string ReadString(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw InvalidStatus();
    }

    private static bool ReadBoolean(JsonElement data, string name, bool fallback)
    {
        if (!data.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw InvalidStatus();
    }

    private static double ReadNumber(JsonElement data, string name, double fallback)
    {
        if (!data.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) ||
            !double.IsFinite(number) || number < 0) throw InvalidStatus();
        return number;
    }

    private static long ReadMemory(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long bytes) || bytes < 0)
            throw InvalidStatus();
        return bytes;
    }

    private static IReadOnlyList<string> ReadTracks(JsonElement data)
    {
        if (!data.TryGetProperty("tracks", out var tracks)) return Array.Empty<string>();
        if (tracks.ValueKind != JsonValueKind.Array) throw InvalidStatus();
        var names = new List<string>();
        foreach (var item in tracks.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw InvalidStatus();
            names.Add(item.GetString()!);
        }
        return names.AsReadOnly();
    }
}
