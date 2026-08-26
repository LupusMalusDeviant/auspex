namespace Auspex.Control.Services.Extension;

/// <summary>
/// Clears expired exceptions away.
///
/// Without this service "timed" would be a promise nobody keeps — and that
/// would be worse than no time limit at all, because people rely on it.
/// </summary>
public class ExceptionCleanupService(
    IServiceProvider services,
    ILogger<ExceptionCleanupService> log) : BackgroundService
{
    // One minute: the shortest limit the interface offers is a quarter of an
    // hour. It need be no more precise, and more often would only be load.
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var range = services.CreateScope();
                var exceptions = range.ServiceProvider.GetRequiredService<ExceptionService>();
                await exceptions.CleanUpAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Again on the next pass. A resolver that happens to be
                // restarting is no reason to give up.
                log.LogWarning(ex, "Expired exceptions could not be removed");
            }

            try
            {
                await Task.Delay(Gap, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
