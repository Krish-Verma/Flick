namespace Flick.RecordingHarness;

internal sealed record HarnessOptions(string Host, string Ffmpeg, string Ffprobe, string Output, string Encoder)
{
    public const string OutputPrefix = "Flick-RecordingQualification-";
    internal static readonly string[] Encoders =
    [
        "obs_nvenc_h264_tex", "obs_qsv11_v2", "h264_texture_amf", "obs_nvenc_h264_soft",
        "obs_qsv11_soft_v2", "h264_fallback_amf", "ffmpeg_nvenc", "obs_qsv11",
        "obs_qsv11_soft", "jim_nvenc", "obs_x264"
    ];

    public static HarnessOptions Parse(string[] arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool confirmed = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            string key = arguments[i];
            if (key == "--run-capture")
            {
                if (confirmed) throw new ArgumentException("Duplicate --run-capture.");
                confirmed = true;
                continue;
            }
            if (key is not ("--host" or "--ffmpeg" or "--ffprobe" or "--output" or "--encoder"))
                throw new ArgumentException("Unknown argument: " + key);
            if (++i == arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + key);
            if (!values.TryAdd(key, arguments[i])) throw new ArgumentException("Duplicate argument: " + key);
        }
        if (!confirmed) throw new ArgumentException("Capture is refused without explicit --run-capture.");
        foreach (string key in new[] { "--host", "--ffmpeg", "--ffprobe", "--output", "--encoder" })
            if (!values.ContainsKey(key)) throw new ArgumentException("Required argument: " + key);
        string PathArgument(string key)
        {
            string value = values[key];
            if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal))
                throw new ArgumentException(key + " must be an absolute local path.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        string encoder = values["--encoder"];
        if (!Encoders.Contains(encoder, StringComparer.Ordinal))
            throw new ArgumentException("Choose an explicit supported encoder ID; auto is not qualification.");
        var options = new HarnessOptions(PathArgument("--host"), PathArgument("--ffmpeg"),
            PathArgument("--ffprobe"), PathArgument("--output"), encoder);
        if (!HasOutputName(options.Output))
            throw new ArgumentException("Output must be a new " + OutputPrefix + "<32hex GUID> directory.");
        return options;
    }

    internal static bool HasOutputName(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith(OutputPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(name[OutputPrefix.Length..], "N", out _);
    }

    public void ValidateExistingInputs()
    {
        foreach (string path in new[] { Host, Ffmpeg, Ffprobe })
        {
            OwnedOutput.RefuseReparseComponents(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0 || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Each runtime tool must be an explicit regular .exe file.");
        }
        string parent = Path.GetDirectoryName(Output) ?? throw new ArgumentException("Output needs an existing parent.");
        OwnedOutput.RefuseReparseComponents(parent);
        if (!Directory.Exists(parent)) throw new ArgumentException("Output parent must already exist.");
        if (Path.Exists(Output)) throw new ArgumentException("Output already exists; prior evidence is preserved.");
    }
}
