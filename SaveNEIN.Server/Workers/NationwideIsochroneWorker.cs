using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Workers;

/// <summary>
/// Gradually fills the nationwide isochrone cache during a low-traffic local-time
/// window. The cache is written point-by-point, so stopping the worker is safe and
/// the next run resumes where the prior run stopped.
/// </summary>
public sealed class NationwideIsochroneWorker : BackgroundService
{
    private static readonly string[] DefaultPriorityStateFips =
    {
        "18", "39", "26", "17", "21", "55", // Indiana and adjacent casino catchment states.
        "19", "29", "20", "27", "31", "46", "38" // Remaining Midwest states.
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<NationwideIsochroneWorker> _logger;
    private readonly TimeZoneInfo _timeZone;

    public NationwideIsochroneWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<NationwideIsochroneWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _timeZone = IsochroneSchedule.ResolveTimeZone(
            _config["IsochroneSeeding:Schedule:TimeZoneId"],
            logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("IsochroneSeeding:Schedule:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Nationwide isochrone seeding is disabled by configuration.");
            return;
        }

        var startupDelay = ReadInt("IsochroneSeeding:Schedule:StartupDelayMinutes", 15, minimum: 0);
        if (startupDelay > 0)
        {
            await DelayAsync(TimeSpan.FromMinutes(startupDelay), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
            var start = ReadTime("IsochroneSeeding:Schedule:StartLocalTime", new TimeOnly(1, 0));
            var end = ReadTime("IsochroneSeeding:Schedule:EndLocalTime", new TimeOnly(5, 0));

            if (!IsochroneSchedule.IsWithinWindow(TimeOnly.FromDateTime(now.DateTime), start, end))
            {
                await DelayAsync(TimeSpan.FromMinutes(5), stoppingToken);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var seeder = scope.ServiceProvider.GetRequiredService<IsochroneSeedingService>();
                var gridMeters = ReadInt("IsochroneSeeding:Schedule:GridMeters", 2500, minimum: 250);
                var countiesPerBatch = ReadInt("IsochroneSeeding:Schedule:CountiesPerBatch", 1, minimum: 1);
                var priorityStates = _config.GetSection("IsochroneSeeding:Schedule:PriorityStateFips")
                    .Get<string[]>() ?? DefaultPriorityStateFips;

                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                batchCts.CancelAfter(IsochroneSchedule.GetRemainingWindow(
                    TimeOnly.FromDateTime(now.DateTime),
                    end));

                var seededCount = await seeder.RunNationwideBatchAsync(
                    gridMeters,
                    countiesPerBatch,
                    priorityStates,
                    batchCts.Token);

                if (seededCount == 0)
                {
                    _logger.LogInformation("Nationwide isochrone seeding has no remaining counties. Worker will check again on its next interval.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Nationwide isochrone batch stopped at the end of the configured local-time window.");
            }
            catch (Exception ex)
            {
                // Database and Valhalla outages must not stop the long-running queue.
                _logger.LogError(ex, "Nationwide isochrone batch failed; the next interval will retry it.");
            }

            var intervalMinutes = ReadInt("IsochroneSeeding:Schedule:IntervalMinutes", 1, minimum: 1);
            await DelayAsync(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private int ReadInt(string key, int fallback, int minimum)
    {
        return int.TryParse(_config[key], out var configuredValue)
            ? Math.Max(minimum, configuredValue)
            : fallback;
    }

    private TimeOnly ReadTime(string key, TimeOnly fallback)
    {
        return TimeOnly.TryParse(_config[key], out var value) ? value : fallback;
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
