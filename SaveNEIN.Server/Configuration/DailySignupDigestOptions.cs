namespace SaveNEIN.Server.Configuration;

public sealed class DailySignupDigestOptions
{
    public const string ConfigurationSection = "DailySignupDigest";

    public bool Enabled { get; set; }
    public string TimeZoneId { get; set; } = "America/Indiana/Indianapolis";
    public string DeliveryLocalTime { get; set; } = "08:00";
    public int PollIntervalMinutes { get; set; } = 15;
    public string SenderAddress { get; set; } = "outreach@savefw.com";
    public List<string> Recipients { get; set; } = [];
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
