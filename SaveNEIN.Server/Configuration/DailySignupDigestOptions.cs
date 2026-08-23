namespace SaveNEIN.Server.Configuration;

public sealed class DailySignupDigestOptions
{
    public const string ConfigurationSection = "DailySignupDigest";

    public bool Enabled { get; set; }
    public string TimeZoneId { get; set; } = "America/Indiana/Indianapolis";
    public string DeliveryLocalTime { get; set; } = "08:00";
    public int PollIntervalMinutes { get; set; } = 15;
    public string SenderAddress { get; set; } = "outreach@savefw.com";
    /// <summary>Comma-delimited delivery addresses supplied by deployment configuration.</summary>
    public string RecipientsCsv { get; set; } = string.Empty;

    // Retained only so existing appsettings-based deployments continue to bind.
    public List<string> Recipients { get; set; } = [];

    public IReadOnlyList<string> GetRecipients() =>
        !string.IsNullOrWhiteSpace(RecipientsCsv)
            ? RecipientsCsv
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Recipients
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Select(recipient => recipient.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}

public sealed class ZohoMailOptions
{
    public const string ConfigurationSection = "ZohoMail";

    public string ApiBaseUrl { get; set; } = "https://mail.zoho.com/api/";
    public string TokenEndpoint { get; set; } = "https://accounts.zoho.com/oauth/v2/token";
    public string AccountId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}
