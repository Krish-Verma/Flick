using System.Text.Json;

namespace LocalClip.Core.Ipc;

/// <summary>The native host's negotiated encoder/profile, never a requested quality preset.</summary>
public sealed record CaptureEncodingProfile(string EncoderId, string EncoderName,
    bool IsSoftwareFallback, int Width, int Height, int Fps)
{
    public bool UsesSoftwareEncoding => IsSoftwareFallback || EncoderId == "obs_x264";
    public string DisplayText => $"{EncoderName} · {Width}×{Height} · {Fps} FPS";
    public string SoftwareWarning => UsesSoftwareEncoding
        ? "CPU compatibility mode · 720p / 30 FPS maximum. May reduce game performance."
        : "";

    public static bool TryParse(JsonElement? value, out CaptureEncodingProfile? profile)
    {
        profile = null;
        if (value is not JsonElement data || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("encoder", out var encoder) || encoder.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(encoder.GetString()) ||
            !data.TryGetProperty("encoder_name", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()) ||
            !data.TryGetProperty("software_fallback", out var software) ||
            software.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !PositiveInteger(data, "width", out int width) || !PositiveInteger(data, "height", out int height) ||
            !PositiveInteger(data, "fps", out int fps)) return false;
        profile = new CaptureEncodingProfile(encoder.GetString()!, name.GetString()!,
            software.GetBoolean(), width, height, fps);
        return true;
    }

    private static bool PositiveInteger(JsonElement value, string property, out int number)
    {
        number = 0;
        return value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out number) && number > 0;
    }
}

public sealed record CaptureStartResult(bool Success, string Error, CaptureEncodingProfile? Profile)
{
    public static CaptureStartResult FromResponse(IpcResponse response)
    {
        if (!response.Ok)
            return new(false, string.IsNullOrWhiteSpace(response.Error)
                ? "Capture host refused to start. Check the selected source and available encoders."
                : response.Error, null);
        CaptureEncodingProfile.TryParse(response.Result, out var profile);
        return new(true, "", profile);
    }
}
