using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Data;
using Worldcup.Api.Models;

namespace Worldcup.Api.Services;

/// <summary>
/// Background poller with schedule-driven adaptive cadence:
///  - Live mode  (a match is in progress): poll often (LivePollSeconds).
///  - Idle mode  (no match on): full-sync sparsely and sleep until near the next kickoff.
/// The <see cref="ApiCallBudget"/> separately hard-caps daily api-football usage.
/// </summary>
public class PollerService : BackgroundService
{
    private static readonly TimeSpan MatchDuration = TimeSpan.FromMinutes(130); // 90' + half-time + stoppage/ET headroom
    private readonly IServiceScopeFactory _scopes;
    private readonly ApiCallBudget _budget;
    private readonly ILogger<PollerService> _log;
    private DateTime _lastFullSync = DateTime.MinValue;

    public PollerService(IServiceScopeFactory scopes, ApiCallBudget budget, ILogger<PollerService> log)
    {
        _scopes = scopes;
        _budget = budget;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial schedule load so the board is populated on first boot.
        await Tick(forceFull: true, ct);

        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await Tick(forceFull: false, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "poller tick failed");
                delay = TimeSpan.FromMinutes(5);
            }

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<TimeSpan> Tick(bool forceFull, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ingest = scope.ServiceProvider.GetRequiredService<IngestService>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsStore>();

        var liveSeconds = await settings.LivePollSecondsAsync();
        var idleSeconds = await settings.IdlePollSecondsAsync();
        var now = DateTime.UtcNow;

        if (forceFull)
        {
            await ingest.FullSyncAsync(ct);
            _lastFullSync = now;
        }

        var (liveNow, secondsToNextKickoff) = await ScheduleWindow(db, now, ct);

        if (liveNow)
        {
            if (_budget.Exhausted && ingest.ActiveProvider.Name == "api-football")
            {
                _log.LogInformation("live window but api-football budget exhausted; backing off");
                return TimeSpan.FromSeconds(Math.Max(liveSeconds, 300));
            }
            await ingest.LiveSyncAsync(ct);
            return TimeSpan.FromSeconds(liveSeconds);
        }

        // Idle: full-sync on the idle cadence, then sleep until ~the next kickoff.
        if (!forceFull && (now - _lastFullSync).TotalSeconds >= idleSeconds)
        {
            await ingest.FullSyncAsync(ct);
            _lastFullSync = now;
        }

        var until = secondsToNextKickoff is { } s ? Math.Clamp(s, liveSeconds, idleSeconds) : idleSeconds;
        return TimeSpan.FromSeconds(until);
    }

    /// <summary>Is a match in progress now? If not, how many seconds until the next kickoff?</summary>
    private static async Task<(bool liveNow, double? secondsToNextKickoff)> ScheduleWindow(
        AppDbContext db, DateTime now, CancellationToken ct)
    {
        var liveNow = await db.Matches.AnyAsync(m =>
            m.Status != MatchStatus.Finished &&
            m.KickoffUtc <= now.AddMinutes(2) &&
            m.KickoffUtc >= now - MatchDuration, ct);

        double? secondsToNext = null;
        var next = await db.Matches
            .Where(m => m.Status != MatchStatus.Finished && m.KickoffUtc > now)
            .OrderBy(m => m.KickoffUtc)
            .Select(m => (DateTime?)m.KickoffUtc)
            .FirstOrDefaultAsync(ct);
        if (next is { } k) secondsToNext = (k - now).TotalSeconds;

        return (liveNow, secondsToNext);
    }
}
