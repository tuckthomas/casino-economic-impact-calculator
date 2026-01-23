using SaveFW.Server.Services;

namespace SaveFW.Server.Workers;

public class ScoringWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScoringWorker> _logger;

    public ScoringWorker(IServiceScopeFactory scopeFactory, ILogger<ScoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Simple loop or one-off run
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunScoringBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScoringWorker");
            }

            // Run every hour or just sleep for now
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunScoringBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IsochroneSeedingService>();
        // Default to Allen County
        await seeder.RunSeedingJobAsync(new[] { "Allen" }, 2500, ct);
    }
}
