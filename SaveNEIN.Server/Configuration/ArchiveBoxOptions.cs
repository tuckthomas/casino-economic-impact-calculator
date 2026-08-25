namespace SaveNEIN.Server.Configuration;

public sealed class ArchiveBoxOptions
{
    public const string ConfigurationSection = "ArchiveBox";

    public bool Enabled { get; init; }
    public string InternalBaseUrl { get; init; } = "http://savenein-archivebox:8000";
    public string ApiToken { get; init; } = string.Empty;
    public string CaptureAdminToken { get; init; } = string.Empty;
    public string DataPath { get; init; } = "/var/lib/savenein/archivebox";
    public string ArchiveUserName { get; init; } = "savenein";
    public int CaptureTimeoutSeconds { get; init; } = 600;
    public int CrawlDepth { get; init; }
    public string[] AllowedSourceHosts { get; init; } = [];
    public ArchiveSourceOptions[] Sources { get; init; } = [];
}

public sealed class ArchiveSourceOptions
{
    public string Key { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public DateTime? ObservedAtUtc { get; init; }
    public string? ObservationType { get; init; }
    public string[] RequiredTexts { get; init; } = [];
}
