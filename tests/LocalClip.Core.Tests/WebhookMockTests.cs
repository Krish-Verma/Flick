using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Sharing;
using Xunit;

namespace LocalClip.Core.Tests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_responseFactory(request));
    }
}

public class WebhookMockTests
{
    private const string ValidWebhookUrl = "https://discord.com/api/webhooks/1234567890/abc123XYZ_-";

    [Fact]
    public async Task TestSuccessfulWebhookPost()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            // Verify query param ?wait=true was appended
            Assert.Contains("wait=true", req.RequestUri!.Query);

            // Read multipart content
            var content = req.Content as MultipartFormDataContent;
            Assert.NotNull(content);

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\": \"987654321012345678\"}", Encoding.UTF8, "application/json")
            };
            return resp;
        });

        using var client = new HttpClient(handler);
        var webhook = new DiscordWebhookClient(client);

        string tempFile = Path.Combine(Path.GetTempPath(), $"test_clip_{Guid.NewGuid():N}.mp4");
        File.WriteAllText(tempFile, "fake-mp4-payload");

        try
        {
            var result = await webhook.PostClipAsync(ValidWebhookUrl, tempFile, "Test clip comment");
            Assert.True(result.Success);
            Assert.Equal("987654321012345678", result.MessageId);
            Assert.Equal(200, result.StatusCode);
            Assert.False(result.IsRateLimited);
            Assert.False(result.DeliveryAmbiguous);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TestWebhookPostRateLimited429()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"message\": \"You are being rate limited.\", \"retry_after\": 3.5, \"global\": false}", Encoding.UTF8, "application/json")
            };
            return resp;
        });

        using var client = new HttpClient(handler);
        var webhook = new DiscordWebhookClient(client);

        string tempFile = Path.Combine(Path.GetTempPath(), $"test_clip_{Guid.NewGuid():N}.mp4");
        File.WriteAllText(tempFile, "fake-mp4-payload");

        try
        {
            var result = await webhook.PostClipAsync(ValidWebhookUrl, tempFile, "Test rate limit");
            Assert.False(result.Success);
            Assert.Equal(429, result.StatusCode);
            Assert.True(result.IsRateLimited);
            Assert.Equal(3.5, result.RetryAfterSeconds);
            Assert.Contains("Rate limited by Discord", result.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, 413)]
    public async Task TestWebhookPostHttpErrors(HttpStatusCode code, int expectedStatus)
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(code)
            {
                Content = new StringContent($"{{\"code\": {expectedStatus}, \"message\": \"Error\"}}", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var webhook = new DiscordWebhookClient(client);

        string tempFile = Path.Combine(Path.GetTempPath(), $"test_clip_{Guid.NewGuid():N}.mp4");
        File.WriteAllText(tempFile, "fake-mp4-payload");

        try
        {
            var result = await webhook.PostClipAsync(ValidWebhookUrl, tempFile, "Test error");
            Assert.False(result.Success);
            Assert.Equal(expectedStatus, result.StatusCode);
            Assert.False(result.IsRateLimited);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TestWebhookPostCancellationMarkedAmbiguous()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            throw new OperationCanceledException();
        });

        using var client = new HttpClient(handler);
        var webhook = new DiscordWebhookClient(client);

        string tempFile = Path.Combine(Path.GetTempPath(), $"test_clip_{Guid.NewGuid():N}.mp4");
        File.WriteAllText(tempFile, "fake-mp4-payload");

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = await webhook.PostClipAsync(ValidWebhookUrl, tempFile, "Test cancel", cts.Token);
            Assert.False(result.Success);
            Assert.True(result.DeliveryAmbiguous);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TestWebhookNonexistentFile()
    {
        var webhook = new DiscordWebhookClient();
        var result = await webhook.PostClipAsync(ValidWebhookUrl, @"D:\non_existent_folder_xyz\file.mp4");
        Assert.False(result.Success);
        Assert.Contains("File does not exist", result.ErrorMessage);
    }
}
