using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

internal sealed class DailySignupDigestWorker : BackgroundService
{
    private const int MaximumCatchUpDaysPerCycle = 14;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DailySignupDigestOptions _options;
    private readonly ILogger<DailySignupDigestWorker> _logger;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeOnly _deliveryTime;

    public DailySignupDigestWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DailySignupDigestOptions> options,
        ILogger<DailySignupDigestWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
        _deliveryTime = TimeOnly.Parse(_options.DeliveryLocalTime);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Daily signup digest is disabled.");
            return;
        }

        ValidateConfiguration();
        _logger.LogInformation("Daily signup digest enabled for {DeliveryTime} in {TimeZone}.", _deliveryTime, _timeZone.Id);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(_options.PollIntervalMinutes, 1, 1440)));
        do
        {
            try
            {
                await RunDueDigestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Daily signup digest cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunDueDigestsAsync(CancellationToken cancellationToken)
    {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
        if (TimeOnly.FromDateTime(localNow.DateTime) < _deliveryTime) return;

        var latestEligibleDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(-1);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var earliestFailed = await db.DailySignupDigestDeliveries
            .Where(delivery => delivery.ReportDateLocal <= latestEligibleDate && delivery.SentAtUtc == null)
            .MinAsync(delivery => (DateOnly?)delivery.ReportDateLocal, cancellationToken);
        var lastSent = await db.DailySignupDigestDeliveries
            .Where(delivery => delivery.SentAtUtc != null)
            .MaxAsync(delivery => (DateOnly?)delivery.ReportDateLocal, cancellationToken);

        var firstDate = earliestFailed ?? (lastSent?.AddDays(1) ?? latestEligibleDate);
        var processed = 0;
        for (var date = firstDate; date <= latestEligibleDate && processed < MaximumCatchUpDaysPerCycle; date = date.AddDays(1), processed++)
        {
            await SendDateAsync(db, scope.ServiceProvider.GetRequiredService<IZohoMailSender>(), date, cancellationToken);
        }
    }

    private async Task SendDateAsync(AppDbContext db, IZohoMailSender sender, DateOnly reportDate, CancellationToken cancellationToken)
    {
        var delivery = await db.DailySignupDigestDeliveries
            .SingleOrDefaultAsync(item => item.ReportDateLocal == reportDate, cancellationToken);
        if (delivery?.SentAtUtc is not null) return;

        var (startUtc, endUtc) = DailySignupDigestContent.GetUtcPeriod(reportDate, _timeZone);
        delivery ??= new DailySignupDigestDelivery
        {
            ReportDateLocal = reportDate,
            PeriodStartUtc = startUtc,
            PeriodEndUtc = endUtc
        };
        if (delivery.Id == 0) db.DailySignupDigestDeliveries.Add(delivery);

        var signups = await db.CoalitionSignups.AsNoTracking()
            .Where(signup => signup.CreatedAtUtc >= startUtc && signup.CreatedAtUtc < endUtc)
            .OrderBy(signup => signup.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var allSignups = signups.Count == 0
            ? new List<CoalitionSignup>()
            : await db.CoalitionSignups.AsNoTracking()
                .OrderBy(signup => signup.LastName)
                .ThenBy(signup => signup.FirstName)
                .ToListAsync(cancellationToken);

        delivery.RegistrationCount = signups.Count;
        delivery.Status = "Sending";
        delivery.Attempts++;
        delivery.LastAttemptAtUtc = DateTime.UtcNow;
        delivery.LastError = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var message = DailySignupDigestContent.Build(reportDate, signups, allSignups, _timeZone);
            delivery.ProviderMessageId = await sender.SendAsync(message, cancellationToken);
            delivery.Status = "Sent";
            delivery.SentAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Sent signup digest for {ReportDate} with {RegistrationCount} registrations.", reportDate, signups.Count);
        }
        catch (Exception exception)
        {
            delivery.Status = "Failed";
            delivery.LastError = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private void ValidateConfiguration()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.SenderAddress)) missing.Add("DailySignupDigest:SenderAddress");
        if (_options.GetRecipients().Count == 0) missing.Add("DailySignupDigest:RecipientsCsv");

        using var scope = _scopeFactory.CreateScope();
        var zoho = scope.ServiceProvider.GetRequiredService<IOptions<ZohoMailOptions>>().Value;
        if (string.IsNullOrWhiteSpace(zoho.AccountId)) missing.Add("ZohoMail:AccountId");
        if (string.IsNullOrWhiteSpace(zoho.ClientId)) missing.Add("ZohoMail:ClientId");
        if (string.IsNullOrWhiteSpace(zoho.ClientSecret)) missing.Add("ZohoMail:ClientSecret");
        if (string.IsNullOrWhiteSpace(zoho.RefreshToken)) missing.Add("ZohoMail:RefreshToken");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Daily signup digest is enabled but required configuration is missing: {string.Join(", ", missing)}.");
        }
    }
}
