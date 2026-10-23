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
}
