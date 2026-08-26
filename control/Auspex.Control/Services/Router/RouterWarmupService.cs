namespace Auspex.Control.Services.Router;

/// <summary>
/// Reads the router's catalogue in the background before anybody asks for it.
///
/// Discovery is expensive: close to forty description files, each with up to
/// three attempts, because the Fritz!Box throttles rapid fetching. Whoever
/// opens the router section first would otherwise wait for exactly that —
/// the page stands still with no sign of why.
///
/// So: once at startup and again as soon as the credentials change. Failures
/// are uncritical: the pages fetch the catalogue themselves when needed,
/// just with a wait.
/// </summary>
public class RouterWarmupService(
    Tr064Client client,
    IRouterSettingsStore store,
    ILogger<RouterWarmupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var last = -1;

        while (!stop.IsCancellationRequested)
        {
            var version = store.Version;
            if (version != last && store.Current.Configured)
            {
                last = version;
                try
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var catalogue = await client.GetCatalogAsync(stop);
                    log.LogInformation(
                        "Router catalogue read in the background: {Model}, {Actions} actions in {Duration} ms",
                        catalogue.Model, catalogue.ActionCount, clock.ElapsedMilliseconds);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Unreachable is a state, not a crash. It will be tried again
                    // on the next pass.
                    log.LogWarning(ex, "The router catalogue could not be pre-read");
                    last = -1;
                }
            }
            else if (!store.Current.Configured)
            {
                // With no account there is nothing to fetch. As soon as one is
                // entered, the store counts up and the next pass takes hold.
                last = -1;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
