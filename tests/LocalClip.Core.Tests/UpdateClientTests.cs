using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LocalClip.Core.Updates;

namespace LocalClip.Core.Tests;

public class UpdateClientTests
{
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("Test installer bytes, never executed.");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Installer));

    [Fact]
    public async Task SelectsHighestNumericStableVersionAndSkipsDraftAndPreview()
    {
        var draft = Release("v9.0.0"); draft["draft"] = true;
        using var http = Metadata(Release("v1.0.9"), Release("v1.0.10"), draft, Release("v2.0.0-preview.1"));
        var update = await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None);
        Assert.Equal("1.0.10", update?.Version);
        Assert.Equal("https://api.github.com/repos/Krish-Verma/Flick/releases/assets/101", update?.InstallerUrl);
    }

    [Theory]
    [InlineData("1.0.3-preview.2", true, "v1.0.3-preview.9", "v1.0.3-preview.10", "1.0.3-preview.10")]
    [InlineData("1.0.2", true, "v1.0.3-preview.9", "v1.0.3-preview.10", "1.0.3-preview.10")]
    [InlineData("1.0.3-preview.10", false, "v1.0.3-preview.11", "v1.0.3", "1.0.3")]
    [InlineData("1.0.2", true, "v1.0.3-1", "v1.0.3-alpha", "1.0.3-alpha")]
    [InlineData("1.0.2", true, "v1.0.3-alpha", "v1.0.3-alpha.1", "1.0.3-alpha.1")]
    [InlineData("1.0.2", false, "v1.0.3+build.1", "v1.0.3-preview.99", "1.0.3+build.1")]
    [InlineData("1.0.2", false, "v1.0.3+ci-123", "v1.0.3-preview.99", "1.0.3+ci-123")]
    [InlineData("1.0.2", false, "v1.999999999999999999999.0", "v2.0.0", "2.0.0")]
    public async Task AppliesSemverPrecedenceAndPreviewChannel(string current, bool includePreviews, string first,
        string second, string expected)
    {
        using var http = Metadata(Release(first), Release(second));
        var update = await new UpdateClient(http).CheckAsync(current, includePreviews, CancellationToken.None);
        Assert.Equal(expected, update?.Version);
    }

    [Theory]
    [InlineData("v1.0.2", "1.0.3")]
    [InlineData("v1.0.3", "1.0.3")]
    [InlineData("v1.0.3-preview.9", "1.0.3")]
    [InlineData("v1.0.3+different", "1.0.3+current")]
    [InlineData("v1.0.3-preview.2", "1.0.3-preview.10")]
    [InlineData("v1.0.4-preview.1", "1.0.3")]
    [InlineData("v1.0.3-preview.11", "1.0.3-preview.10")]
    public async Task NeverOffersDowngradeEqualBuildOrUnrequestedPreview(string available, string current)
    {
        using var http = Metadata(Release(available));
        Assert.Null(await new UpdateClient(http).CheckAsync(current, false, CancellationToken.None));
    }

    [Theory]
    [InlineData("1.0.3", false)]
    [InlineData("1.0.3", true)]
    [InlineData("1.0.3-preview.1", false)]
    [InlineData("1.0.3-preview.1", true)]
    public async Task LatestCataloguePreviewStillRequiresSemanticPreviewOptIn(string current, bool includePreviews)
    {
        var preview = Release("v1.0.4-preview.1");
        preview["prerelease"] = false; // GitHub requires this catalogue flag for its Latest sidebar.
        using var http = Metadata(preview);
        var update = await new UpdateClient(http).CheckAsync(current, includePreviews, CancellationToken.None);
        if (!includePreviews) Assert.Null(update);
        else
        {
            Assert.NotNull(update);
            Assert.Equal("1.0.4-preview.1", update.Version);
            Assert.Equal("v1.0.4-preview.1", update.Tag);
            Assert.True(ReleaseVersion.TryParse(update.Tag, out var semanticVersion));
            Assert.True(semanticVersion.IsPrerelease);
        }
    }

    [Fact]
    public async Task LatestCataloguePreviewDoesNotHideAvailableStableUpdateFromStableChannel()
    {
        var preview = Release("v1.0.4-preview.1");
        preview["prerelease"] = false;
        using var http = Metadata(preview, Release("v1.0.3"));
        var update = await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None);
        Assert.Equal("1.0.3", update?.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsStableSemanticTagMarkedAsCataloguePrereleaseEvenWithOptIn(bool includePreviews)
    {
        var ambiguous = Release("v1.0.4");
        ambiguous["prerelease"] = true;
        using var http = Metadata(ambiguous);
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.3", includePreviews, CancellationToken.None));
    }

    [Fact]
    public async Task LatestCataloguePreviewCannotBypassRepositoryAssetValidation()
    {
        var preview = Release("v1.0.4-preview.1");
        preview["prerelease"] = false;
        preview["assets"]![0]!["url"] = "https://api.github.com/repos/other/Flick/releases/assets/101";
        using var http = Metadata(preview);
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.3", true, CancellationToken.None));
    }

    [Theory]
    [InlineData("ui-refresh-preview-20261023")]
    [InlineData("v01.0.3")]
    [InlineData("v1.0.3-preview.01")]
    [InlineData("v1.0")]
    [InlineData("v1.0.3.0")]
    [InlineData("v1.0.3-")]
    [InlineData("v1.0.3\n")]
    [InlineData("v1.0.3-preview/1")]
    public async Task SkipsMalformedReleaseVersions(string tag)
    {
        using var http = Metadata(Release(tag));
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.2", true, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://evil.example/installer.exe")]
    [InlineData("http://api.github.com/repos/Krish-Verma/Flick/releases/assets/101")]
    [InlineData("https://api.github.com/repos/other/Flick/releases/assets/101")]
    [InlineData("https://api.github.com/repos/Krish-Verma/Flick-evil/releases/assets/101")]
    [InlineData("https://api.github.com/repos/Krish-Verma/Flick/releases/assets/../101")]
    [InlineData("https://api.github.com/repos/Krish-Verma/Flick/releases/assets/101?download=true")]
    [InlineData("https://user@api.github.com/repos/Krish-Verma/Flick/releases/assets/101")]
    [InlineData("https://api.github.com:444/repos/Krish-Verma/Flick/releases/assets/101")]
    [InlineData("https://api.github.com/repos/Krish-Verma/Flick/releases/assets/101#fragment")]
    public async Task RejectsUntrustedAssetUrls(string url)
    {
        var release = Release("v1.0.3");
        release["assets"]![0]!["url"] = url;
        using var http = Metadata(release);
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None));
    }

    [Theory]
    [InlineData("duplicate-installer")]
    [InlineData("duplicate-manifest")]
    [InlineData("missing-manifest")]
    [InlineData("wrong-name")]
    [InlineData("oversize")]
    [InlineData("empty")]
    [InlineData("size-as-string")]
    [InlineData("pending")]
    [InlineData("off-repo-browser-url")]
    [InlineData("off-repo-release-url")]
    [InlineData("wrong-tag-url")]
    [InlineData("inconsistent-preview")]
    [InlineData("same-asset-id")]
    public async Task RejectsMissingDuplicateOrMalformedReleaseAssets(string fault)
    {
        var release = Release("v1.0.3");
        var assets = (JsonArray)release["assets"]!;
        switch (fault)
        {
            case "duplicate-installer": assets.Add(assets[0]!.DeepClone()); break;
            case "duplicate-manifest": assets.Add(assets[1]!.DeepClone()); break;
            case "missing-manifest": assets.RemoveAt(1); break;
            case "wrong-name": assets[0]!["name"] = "other.exe"; break;
            case "oversize": assets[0]!["size"] = UpdateClient.MaximumInstallerBytes + 1; break;
            case "empty": assets[0]!["size"] = 0; break;
            case "size-as-string": assets[0]!["size"] = "123"; break;
            case "pending": assets[0]!["state"] = "new"; break;
            case "off-repo-browser-url": assets[0]!["browser_download_url"] = "https://github.com/other/Flick/releases/download/v1.0.3/Flick-Setup.exe"; break;
            case "off-repo-release-url": release["html_url"] = "https://evil.example/releases/tag/v1.0.3"; break;
            case "wrong-tag-url": release["html_url"] = "https://github.com/Krish-Verma/Flick/releases/tag/v8.0.0"; break;
            case "inconsistent-preview": release["prerelease"] = true; break;
            case "same-asset-id": assets[1]!["url"] = assets[0]!["url"]!.GetValue<string>(); break;
        }
        using var http = Metadata(release);
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None));
    }

    [Fact]
    public async Task SkipsDuplicateReleaseTags()
    {
        using var http = Metadata(Release("v1.0.3"), Release("v1.0.3"));
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None));
    }

    [Fact]
    public async Task MissingAssetOnNewestReleaseDoesNotHidePreviousCompleteRelease()
    {
        var incomplete = Release("v1.0.4");
        incomplete["assets"] = new JsonArray();
        using var http = Metadata(incomplete, Release("v1.0.3"));
        Assert.Equal("1.0.3", (await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None))?.Version);
    }

    [Fact]
    public async Task ChecksUseOnlyPublicRepositoryMetadataAndNoCredentials()
    {
        using var http = Client((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/Krish-Verma/Flick/releases?per_page=100", request.RequestUri!.AbsoluteUri);
            Assert.Equal("application/vnd.github+json", request.Headers.Accept.Single().MediaType);
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("Flick-Updater", request.Headers.UserAgent.ToString());
            return Task.FromResult(Response("[]"));
        });
        Assert.Null(await new UpdateClient(http).CheckAsync("1.0.2", false, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOversizedAndMalformedMetadata()
    {
        using var oversized = Client((_, _) => Task.FromResult(Response(new byte[UpdateClient.MaximumMetadataBytes + 1])));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(oversized).CheckAsync("1.0.2", false, CancellationToken.None));
        using var malformed = Client((_, _) => Task.FromResult(Response("{}")));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(malformed).CheckAsync("1.0.2", false, CancellationToken.None));
        using var streamOversized = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[UpdateClient.MaximumMetadataBytes + 1])
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(streamOversized).CheckAsync("1.0.2", false, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadVerifiesHashReportsProgressAndUsesUniqueStagingFolders()
    {
        using var directory = new TemporaryDirectory();
        var unrelated = Path.Combine(directory.Path, "keep.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        using var http = Downloads();
        var client = new UpdateClient(http);
        var progress = new ImmediateProgress();
        var first = await client.DownloadAsync(Update(), directory.Path, progress, CancellationToken.None);
        var second = await client.DownloadAsync(Update(), directory.Path, null, CancellationToken.None);
        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(first.FilePath));
        Assert.Equal(Hash, first.Sha256);
        Assert.Equal(Update(), first.Release);
        Assert.Equal(new[] { 0d, 1d }, progress.Values);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }

    [Theory]
    [InlineData("bad-hash")]
    [InlineData("truncated")]
    [InlineData("extra-data")]
    [InlineData("wrong-content-length")]
    [InlineData("duplicate-hash")]
    [InlineData("missing-hash")]
    [InlineData("malformed-hash")]
    [InlineData("invalid-utf8")]
    [InlineData("oversized-manifest")]
    public async Task FailedDownloadLeavesNoInstallerOrPartialFile(string fault)
    {
        using var directory = new TemporaryDirectory();
        var manifest = Encoding.UTF8.GetBytes($"{Hash}  Flick-Setup.exe\n");
        var installer = Installer;
        var declaredLength = (long)Installer.Length;
        switch (fault)
        {
            case "bad-hash": manifest = Encoding.UTF8.GetBytes($"{new string('0', 64)}  Flick-Setup.exe\n"); break;
            case "truncated": installer = Installer[..^1]; break;
            case "extra-data": installer = [.. Installer, 0]; break;
            case "wrong-content-length": declaredLength++; break;
            case "duplicate-hash": manifest = Encoding.UTF8.GetBytes($"{Hash}  Flick-Setup.exe\n{Hash}  Flick-Setup.exe\n"); break;
            case "missing-hash": manifest = Encoding.UTF8.GetBytes($"{Hash}  Flick-Portable.zip\n"); break;
            case "malformed-hash": manifest = Encoding.UTF8.GetBytes($"{new string('z', 64)}  Flick-Setup.exe\n"); break;
            case "invalid-utf8": manifest = [0xff]; break;
            case "oversized-manifest": manifest = new byte[UpdateClient.MaximumChecksumBytes + 1]; break;
        }
        using var http = Client((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/102")) return Task.FromResult(Response(manifest));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(installer) };
            if (fault == "wrong-content-length") response.Content.Headers.ContentLength = declaredLength;
            return Task.FromResult(response);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(http).DownloadAsync(Update(), directory.Path, null, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task CancellationDuringBodyTransferRemovesPartiallyWrittenInstaller()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        using var http = Downloads();
        var progress = new ImmediateProgress(value => { if (value > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateClient(http)
            .DownloadAsync(Update(), directory.Path, progress, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task FollowsOnlyPermittedGithubAssetCdnRedirects()
    {
        using var directory = new TemporaryDirectory();
        var locations = new List<Uri>();
        using var http = Client((request, _) =>
        {
            locations.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath.EndsWith("/102")) return Task.FromResult(Response($"{Hash} *Flick-Setup.exe\n"));
            if (request.RequestUri.Host == "api.github.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/test?signature=abc");
                return Task.FromResult(redirect);
            }
            Assert.Equal("release-assets.githubusercontent.com", request.RequestUri.Host);
            return Task.FromResult(Response(Installer));
        });
        var update = await new UpdateClient(http).DownloadAsync(Update(), directory.Path, null, CancellationToken.None);
        Assert.True(File.Exists(update.FilePath));
        Assert.Equal(3, locations.Count);
    }

    [Theory]
    [InlineData("https://evil.example/Flick-Setup.exe")]
    [InlineData("http://release-assets.githubusercontent.com/file")]
    [InlineData("https://release-assets.githubusercontent.com.evil.example/file")]
    [InlineData("https://api.github.com/repos/other/Flick/releases/assets/101")]
    public async Task RejectsUnsafeRedirectBeforeSendingRequest(string location)
    {
        using var directory = new TemporaryDirectory();
        var requests = 0;
        using var http = Client((request, _) =>
        {
            requests++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/102")) return Task.FromResult(Response($"{Hash}  Flick-Setup.exe\n"));
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(http).DownloadAsync(Update(), directory.Path, null, CancellationToken.None));
        Assert.Equal(2, requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task RejectsInjectedHandlersFinalUntrustedLocation()
    {
        using var directory = new TemporaryDirectory();
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/file"),
            Content = new ByteArrayContent(Installer)
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(http).DownloadAsync(Update(), directory.Path, null, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task InvalidCallerSuppliedUpdateDoesNotSendRequests()
    {
        using var directory = new TemporaryDirectory();
        using var http = Client((_, _) => throw new InvalidOperationException("Must not access network."));
        var client = new UpdateClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Update() with { InstallerUrl = "https://evil.example/file" },
            directory.Path, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Update() with { InstallerSize = UpdateClient.MaximumInstallerBytes + 1 },
            directory.Path, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Update() with { Version = "9.0.0" },
            directory.Path, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAsync("nonsense", false, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    private static JsonObject Release(string tag)
    {
        var encodedTag = Uri.EscapeDataString(tag);
        return new JsonObject
        {
            ["tag_name"] = tag, ["draft"] = false,
            ["prerelease"] = ReleaseVersion.TryParse(tag, out var version) && version.IsPrerelease,
            ["html_url"] = $"https://github.com/Krish-Verma/Flick/releases/tag/{encodedTag}",
            ["assets"] = new JsonArray(Asset("Flick-Setup.exe", "101", Installer.Length), Asset("SHA256SUMS.txt", "102", 150))
        };

        JsonObject Asset(string name, string id, long size) => new()
        {
            ["name"] = name, ["url"] = $"https://api.github.com/repos/Krish-Verma/Flick/releases/assets/{id}",
            ["browser_download_url"] = $"https://github.com/Krish-Verma/Flick/releases/download/{encodedTag}/{name}",
            ["state"] = "uploaded", ["size"] = size
        };
    }

    private static AvailableUpdate Update() => new("1.0.3-preview.1",
        "https://github.com/Krish-Verma/Flick/releases/tag/v1.0.3-preview.1",
        "https://api.github.com/repos/Krish-Verma/Flick/releases/assets/101",
        "https://api.github.com/repos/Krish-Verma/Flick/releases/assets/102", Installer.Length, "v1.0.3-preview.1");

    private static HttpClient Metadata(params JsonObject[] releases) => Client((_, _) =>
        Task.FromResult(Response(new JsonArray(releases.Select(release => (JsonNode)release).ToArray()).ToJsonString())));

    private static HttpClient Downloads() => Client((request, _) =>
    {
        Assert.Equal("application/octet-stream", request.Headers.Accept.Single().MediaType);
        return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/102")
            ? Response($"\uFEFF# Flick SHA-256 Checksums\r\n{Hash}  Flick-v1.0.3-win-x64.zip\r\n{Hash}  Flick-Setup.exe\r\n") : Response(Installer));
    });

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => new(new Handler(responder));
    private static HttpResponseMessage Response(string text) => Response(Encoding.UTF8.GetBytes(text));
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await responder(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private sealed class ImmediateProgress(Action<double>? action = null) : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) { Values.Add(value); action?.Invoke(value); }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flick-update-test-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
