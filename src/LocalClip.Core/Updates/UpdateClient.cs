using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalClip.Core.Updates;

public sealed record AvailableUpdate(string Version, string ReleaseUrl, string InstallerUrl,
    string ChecksumUrl, long InstallerSize, string Tag);

public sealed record DownloadedUpdate(string FilePath, string Sha256, AvailableUpdate Release);

/// <summary>
/// On-demand updates from the public Flick GitHub repository. SHA256SUMS verifies transfer consistency;
/// the installer must also pass the app's publisher/signature policy before being run.
/// </summary>
public sealed class UpdateClient
{
    public const long MaximumInstallerBytes = 1024L * 1024 * 1024;
    public const int MaximumMetadataBytes = 4 * 1024 * 1024;
    public const int MaximumChecksumBytes = 64 * 1024;
    public const string InstallerName = "Flick-Setup.exe";
    public const string ChecksumName = "SHA256SUMS.txt";
    private const string RepositoryPath = "/Krish-Verma/Flick";
    private const string ApiRepositoryPath = "/repos" + RepositoryPath;
    private static readonly Uri ReleasesEndpoint = new("https://api.github.com" + ApiRepositoryPath + "/releases?per_page=100");
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly HttpClient _httpClient;

    public UpdateClient() : this(DefaultClient) { }

    /// <summary>The caller retains ownership of an injected client; automatic redirects should be disabled.</summary>
    public UpdateClient(HttpClient httpClient) => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>Checks the latest 100 releases. The app chooses whether the preview channel is enabled.</summary>
    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, bool includePrereleases, CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current))
            throw new ArgumentException("The installed version is not a semantic version.", nameof(currentVersion));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendAsync(ReleasesEndpoint, false, timeout.Token).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(response.Content, MaximumMetadataBytes, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub did not return a release list.");
        if (document.RootElement.GetArrayLength() > 100)
            throw new InvalidDataException("GitHub returned too many releases.");

        AvailableUpdate? selected = null;
        ReleaseVersion? selectedVersion = null;
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var tag = GetString(element, "tag_name");
            if (tag is not null && !seen.Add(tag)) duplicates.Add(tag);
        }
        foreach (var element in document.RootElement.EnumerateArray())
        {
            timeout.Token.ThrowIfCancellationRequested();
            var tag = GetString(element, "tag_name");
            // GitHub's Latest catalogue requires prerelease=false. A semantic
            // preview tag still requires explicit preview-channel opt-in.
            if (tag is null || duplicates.Contains(tag) || !ReleaseVersion.TryParse(tag, out var version)
                || GetBoolean(element, "draft") != false || GetBoolean(element, "prerelease") is not bool githubPrerelease
                || (githubPrerelease && !version.IsPrerelease)
                || (version.IsPrerelease && !includePrereleases)
                || version.CompareTo(current) <= 0
                || (selectedVersion is not null && version.CompareTo(selectedVersion) <= 0)) continue;

            var release = ParseRelease(element, tag, version);
            if (release is null) continue;
            selected = release;
            selectedVersion = version;
        }
        return selected;
    }

    public async Task<DownloadedUpdate> DownloadAsync(AvailableUpdate release, string directory,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!ReleaseVersion.TryParse(release.Tag, out var version) || release.Version != version.Text
            || !IsReleaseUrl(release.ReleaseUrl, release.Tag)
            || !IsApiAssetUrl(release.InstallerUrl) || !IsApiAssetUrl(release.ChecksumUrl)
            || release.InstallerUrl == release.ChecksumUrl
            || release.InstallerSize <= 0 || release.InstallerSize > MaximumInstallerBytes)
            throw new InvalidDataException("The update does not have valid Flick release assets.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(Path.GetFullPath(directory), "flick-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var partial = Path.Combine(staging, InstallerName + ".partial");
        var destination = Path.Combine(staging, InstallerName);
        try
        {
            string expectedHash;
            using (var checksumResponse = await SendAsync(new Uri(release.ChecksumUrl), true, token).ConfigureAwait(false))
            {
                var bytes = await ReadBoundedAsync(checksumResponse.Content, MaximumChecksumBytes, token).ConfigureAwait(false);
                expectedHash = ParseInstallerChecksum(bytes);
            }

            using var response = await SendAsync(new Uri(release.InstallerUrl), true, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != release.InstallerSize)
                throw new InvalidDataException("The installer size differs from the GitHub release.");
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            string actualHash;
            try
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long received = 0;
                    progress?.Report(0);
                    while (true)
                    {
                        var count = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                        if (count == 0) break;
                        received = checked(received + count);
                        if (received > release.InstallerSize || received > MaximumInstallerBytes)
                            throw new InvalidDataException("The installer exceeds its declared size.");
                        hasher.AppendData(buffer, 0, count);
                        await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        progress?.Report((double)received / release.InstallerSize);
                    }
                    if (received != release.InstallerSize)
                        throw new InvalidDataException("The installer download was incomplete.");
                    await target.FlushAsync(token).ConfigureAwait(false);
                }
                actualHash = Convert.ToHexString(hasher.GetHashAndReset());
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }

            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
                throw new InvalidDataException("The installer checksum does not match SHA256SUMS.txt.");
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination);
            return new DownloadedUpdate(destination, actualHash, release);
        }
        catch
        {
            // All files live in a newly-created, uniquely-named directory, never in user clip storage.
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static AvailableUpdate? ParseRelease(JsonElement element, string tag, ReleaseVersion version)
    {
        var releaseUrl = GetString(element, "html_url");
        if (!IsReleaseUrl(releaseUrl, tag) || !element.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 100) return null;
        JsonElement? installer = null;
        JsonElement? checksums = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            if (name == InstallerName)
            {
                if (installer is not null) return null;
                installer = asset;
            }
            else if (name == ChecksumName)
            {
                if (checksums is not null) return null;
                checksums = asset;
            }
        }
        if (installer is not JsonElement setup || checksums is not JsonElement manifest
            || !ValidAsset(setup, tag, InstallerName, MaximumInstallerBytes, out var installerUrl, out var size)
            || !ValidAsset(manifest, tag, ChecksumName, MaximumChecksumBytes, out var checksumUrl, out _)
            || installerUrl == checksumUrl) return null;
        return new AvailableUpdate(version.Text, releaseUrl!, installerUrl, checksumUrl, size, tag);
    }

    private static bool ValidAsset(JsonElement asset, string tag, string name, long maximum, out string url, out long size)
    {
        url = GetString(asset, "url") ?? "";
        size = 0;
        var browserUrl = GetString(asset, "browser_download_url");
        return IsApiAssetUrl(url)
            && IsExactGitHubUrl(browserUrl, RepositoryPath + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name)
            && GetString(asset, "state") == "uploaded"
            && asset.TryGetProperty("size", out var length) && length.ValueKind == JsonValueKind.Number
            && length.TryGetInt64(out size) && size > 0 && size <= maximum;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri initial, bool asset, CancellationToken token)
    {
        var uri = initial;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Flick-Updater/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            try
            {
                // Also check an injected handler's final location in case it follows redirects automatically.
                if (response.RequestMessage?.RequestUri is Uri actual && !(asset ? IsAssetLocation(actual) : actual == ReleasesEndpoint))
                    throw new InvalidDataException("GitHub redirected outside the permitted update hosts.");
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (!asset || redirects == 5 || response.Headers.Location is not Uri location)
                        throw new InvalidDataException("The update redirect is invalid.");
                    var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (!IsAssetLocation(next))
                        throw new InvalidDataException("GitHub redirected outside the permitted update hosts.");
                    uri = next;
                    response.Dispose();
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    private static bool IsAssetLocation(Uri uri) => IsHttps(uri)
        && (IsApiAssetUrl(uri.AbsoluteUri)
            || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsApiAssetUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsHttps(uri)
            || !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        const string prefix = ApiRepositoryPath + "/releases/assets/";
        var path = uri.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var identifier = path[prefix.Length..];
        return identifier.Length is > 0 and <= 20 && identifier.All(character => character is >= '0' and <= '9');
    }

    private static bool IsReleaseUrl(string? value, string tag) =>
        IsExactGitHubUrl(value, RepositoryPath + "/releases/tag/" + Uri.EscapeDataString(tag));

    private static bool IsExactGitHubUrl(string? value, string path) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && IsHttps(uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.Replace("%2B", "+", StringComparison.OrdinalIgnoreCase)
            .Equals(path.Replace("%2B", "+", StringComparison.OrdinalIgnoreCase), StringComparison.Ordinal)
        && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static bool IsHttps(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        if (content.Headers.ContentLength > maximum)
            throw new InvalidDataException("The update response exceeds its size limit.");
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)result.Length)), token).ConfigureAwait(false);
                if (count == 0) break;
                if (result.Length + count > maximum) throw new InvalidDataException("The update response exceeds its size limit.");
                result.Write(buffer, 0, count);
            }
            return result.ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static string ParseInstallerChecksum(byte[] bytes)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("SHA256SUMS.txt is not valid UTF-8.", exception); }
        string? hash = null;
        foreach (var rawLine in text.TrimStart('\uFEFF').Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            if (line.Length < 67 || line[64] != ' ' || (line[65] != ' ' && line[65] != '*')
                || !line.AsSpan(0, 64).ToString().All(Uri.IsHexDigit))
                throw new InvalidDataException("SHA256SUMS.txt has an invalid checksum entry.");
            if (line[66..] != InstallerName) continue;
            if (hash is not null) throw new InvalidDataException("SHA256SUMS.txt has duplicate installer entries.");
            hash = line[..64];
        }
        return hash ?? throw new InvalidDataException("SHA256SUMS.txt does not list Flick-Setup.exe.");
    }

    private static string? GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static bool? GetBoolean(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
}
