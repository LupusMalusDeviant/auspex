namespace Auspex.Control.Services;

/// <summary>
/// Lets go again.
///
/// <para>
/// Without this service the time limit on a quarantine would be a promise
/// nobody keeps, and that is worse than no limit at all: a quarantine is
/// something you switch on in a hurry and then have every reason to forget
/// about. A device that stays off the network because a browser tab was
/// closed is the failure this exists to prevent.
/// </para>
/// <para>
/// It runs on start as well as on a timer. The interesting moment is exactly
/// the restart: whatever expired while the control plane was down has to be
/// let go the moment it comes back, not an hour later.
/// </para>
/// </summary>
public class QuarantineExpiryService(
    IServiceProvider services,
    ILogger<QuarantineExpiryService> log) : BackgroundService
{
    // A minute. The shortest span the interface offers is a quarter of an
    // hour, so this is precise enough and cheap: with nothing quarantined it
    // reads an empty list and goes back to sleep.
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var range = services.CreateScope();
                var quarantine = range.ServiceProvider.GetRequiredService<QuarantineService>();
                var lifted = await quarantine.LiftExpiredAsync(stop);
                if (lifted > 0)
                {
                    log.LogInformation("{Count} quarantine(s) expired and were lifted", lifted);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Again on the next pass, and the record stays in place until
                // it works. A resolver that happens to be restarting must not
                // leave a device locked out for good.
                log.LogWarning(ex, "expired quarantines could not be lifted");
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
