using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LocalClip.Core.Sharing;

public class WebhookPostResult
{
    public bool Success { get; set; }
    public string MessageId { get; set; } = "";
    public int StatusCode { get; set; }
    public string ErrorMessage { get; set; } = "";
    public bool IsRateLimited { get; set; }
    public double RetryAfterSeconds { get; set; }
    public bool DeliveryAmbiguous { get; set; }
}

public class DiscordWebhookClient
{
    private readonly HttpClient _httpClient;
    private static readonly Regex DiscordWebhookRegex = new(
        @"^https:\/\/(?:ptb\.|canary\.)?discord(?:app)?\.com\/api(?:\/v\d+)?\/webhooks\/(\d+)\/([A-Za-z0-9_-]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public DiscordWebhookClient(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false // Strict: Do not follow arbitrary redirects disclosing secrets
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
    }

    public static bool ValidateWebhookUrl(string url, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "Webhook URL cannot be empty.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = "Webhook URL must be a valid HTTPS URI.";
            return false;
        }

        if (!DiscordWebhookRegex.IsMatch(url))
        {
            errorMessage = "URL is not a recognized Discord webhook endpoint (https://discord.com/api/webhooks/ID/TOKEN).";
            return false;
        }

        return true;
    }

    public static string ProtectSecret(string plainSecret)
    {
        if (string.IsNullOrEmpty(plainSecret)) return "";
        byte[] bytes = Encoding.UTF8.GetBytes(plainSecret);
        byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string UnprotectSecret(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return "";
        try
        {
            byte[] encrypted = Convert.FromBase64String(encryptedBase64);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return "";
        }
    }

    public async Task<WebhookPostResult> PostClipAsync(
        string webhookUrl,
        string filePath,
        string message = "",
        CancellationToken ct = default)
    {
        if (!ValidateWebhookUrl(webhookUrl, out string urlErr))
        {
            return new WebhookPostResult { Success = false, ErrorMessage = urlErr };
        }

        if (!File.Exists(filePath))
        {
            return new WebhookPostResult { Success = false, ErrorMessage = "File does not exist: " + filePath };
        }

        string uploadUrl = webhookUrl.Contains("?") ? $"{webhookUrl}&wait=true" : $"{webhookUrl}?wait=true";

        try
        {
            using var form = new MultipartFormDataContent();

            // Empty allowed mentions to prevent accidental user/role pings
            var payloadJson = new
            {
                content = message,
                allowed_mentions = new
                {
                    parse = Array.Empty<string>()
                }
            };

            var payloadContent = new StringContent(JsonSerializer.Serialize(payloadJson), Encoding.UTF8, "application/json");
            form.Add(payloadContent, "payload_json");

            // Attach video file
            await using var fileStream = File.OpenRead(filePath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            form.Add(fileContent, "files[0]", Path.GetFileName(filePath));

            var response = await _httpClient.PostAsync(uploadUrl, form, ct);
            int statusCode = (int)response.StatusCode;
            string responseText = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                string msgId = "";
                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    if (doc.RootElement.TryGetProperty("id", out var idProp))
                    {
                        msgId = idProp.GetString() ?? "";
                    }
                }
                catch { }

                return new WebhookPostResult
                {
                    Success = true,
                    MessageId = msgId,
                    StatusCode = statusCode
                };
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                double retryAfter = 5.0;
                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    if (doc.RootElement.TryGetProperty("retry_after", out var raProp))
                    {
                        retryAfter = raProp.GetDouble();
                    }
                }
                catch { }

                return new WebhookPostResult
                {
                    Success = false,
                    StatusCode = 429,
                    IsRateLimited = true,
                    RetryAfterSeconds = retryAfter,
                    ErrorMessage = $"Rate limited by Discord. Retry after {retryAfter:F1}s."
                };
            }

            return new WebhookPostResult
            {
                Success = false,
                StatusCode = statusCode,
                ErrorMessage = $"Discord returned HTTP {statusCode}: {responseText}"
            };
        }
        catch (OperationCanceledException)
        {
            return new WebhookPostResult
            {
                Success = false,
                ErrorMessage = "Webhook post request was cancelled or timed out.",
                DeliveryAmbiguous = true
            };
        }
        catch (Exception ex)
        {
            return new WebhookPostResult
            {
                Success = false,
                ErrorMessage = "Webhook error: " + ex.Message,
                DeliveryAmbiguous = true
            };
        }
    }
}
