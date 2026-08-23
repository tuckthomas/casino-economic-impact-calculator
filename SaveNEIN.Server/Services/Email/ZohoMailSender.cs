using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;

namespace SaveNEIN.Server.Services.Email;

internal interface IZohoMailSender
{
    Task<string?> SendAsync(SignupDigestMessage message, CancellationToken cancellationToken);
}

internal sealed class ZohoMailSender : IZohoMailSender
{
    private readonly HttpClient _httpClient;
    private readonly ZohoMailOptions _zoho;
    private readonly DailySignupDigestOptions _digest;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public ZohoMailSender(
        HttpClient httpClient,
        IOptions<ZohoMailOptions> zoho,
        IOptions<DailySignupDigestOptions> digest)
    {
        _httpClient = httpClient;
        _zoho = zoho.Value;
        _digest = digest.Value;
    }

    public async Task<string?> SendAsync(SignupDigestMessage message, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"accounts/{Uri.EscapeDataString(_zoho.AccountId)}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token);
        request.Content = JsonContent.Create(new
        {
            fromAddress = _digest.SenderAddress,
            toAddress = _digest.SenderAddress,
            bccAddress = string.Join(',', _digest.GetRecipients()),
            subject = message.Subject,
            content = message.Content,
            mailFormat = message.MailFormat,
            encoding = "UTF-8"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Zoho Mail send failed with HTTP {(int)response.StatusCode}: {Limit(responseBody)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        return FindString(document.RootElement, "messageId") ?? FindString(document.RootElement, "messageID");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _zoho.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["refresh_token"] = _zoho.RefreshToken,
                    ["client_id"] = _zoho.ClientId,
                    ["client_secret"] = _zoho.ClientSecret,
                    ["grant_type"] = "refresh_token"
                })
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Zoho OAuth refresh failed with HTTP {(int)response.StatusCode}: {Limit(responseBody)}");
            }

            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("access_token", out var tokenElement) || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                throw new InvalidOperationException($"Zoho OAuth response did not contain an access token: {Limit(responseBody)}");
            }

            _accessToken = tokenElement.GetString();
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var seconds)
                ? seconds
                : 3600;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string? FindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindString(property.Value, propertyName);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, propertyName);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    private static string Limit(string value) => value.Length <= 1000 ? value : value[..1000];
}
