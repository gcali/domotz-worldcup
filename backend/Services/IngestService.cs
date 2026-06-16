using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Data;
using Worldcup.Api.Models;
using Worldcup.Api.Providers;

namespace Worldcup.Api.Services;

/// <summary>
/// Pulls teams/fixtures/results from providers into the DB and triggers status recompute.
/// Teams are always seeded from openfootball (FIFA codes are stable). Matches are driven by a
/// single active provider — api-football when a key is configured, otherwise openfootball — so
/// the two id namespaces ("af:" / "of:") never mix.
/// </summary>
public class IngestService
{
    private readonly AppDbContext _db;
    private readonly OpenFootballProvider _openFootball;
    private readonly ApiFootballProvider _apiFootball;
    private readonly StatusService _status;
    private readonly SettingsStore _settings;
    private readonly ILogger<IngestService> _log;

    public IngestService(
        AppDbContext db,
        OpenFootballProvider openFootball,
        ApiFootballProvider apiFootball,
        StatusService status,
        SettingsStore settings,
        ILogger<IngestService> log)
    {
        _db = db;
        _openFootball = openFootball;
        _apiFootball = apiFootball;
        _status = status;
        _settings = settings;
        _log = log;
    }

    public IResultsProvider ActiveProvider => _apiFootball.IsAvailable ? _apiFootball : _openFootball;
    private string ActivePrefix => _apiFootball.IsAvailable ? "af:" : "of:";

    public async Task SeedTeamsIfEmptyAsync(CancellationToken ct = default)
    {
        if (await _db.Teams.AnyAsync(ct)) return;
        try
        {
            var teams = await _openFootball.GetTeamsAsync(ct);
            foreach (var t in teams)
            {
                _db.Teams.Add(new Team
                {
                    FifaCode = t.FifaCode,
                    Name = t.Name,
                    FlagEmoji = t.FlagEmoji,
                    GroupName = t.GroupName,
                    Status = TeamStatus.Alive,
                });
            }
            await _db.SaveChangesAsync(ct);
            await _settings.SetAsync(SettingKeys.Seeded, "true");
            _log.LogInformation("Seeded {Count} teams from openfootball", teams.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Team seeding from openfootball failed (will retry on next sync)");
        }
    }

    /// <summary>Authoritative refresh of the whole fixture set from the active provider.</summary>
    public async Task FullSyncAsync(CancellationToken ct = default)
    {
        await SeedTeamsIfEmptyAsync(ct);
        IReadOnlyList<ProviderMatch> incoming;
        try
        {
            incoming = await ActiveProvider.GetMatchesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Full sync from {Provider} failed", ActiveProvider.Name);
            // Fall back to openfootball for at least a schedule if the primary failed.
            if (ActiveProvider != _openFootball)
                incoming = await Safe(() => _openFootball.GetMatchesAsync(ct));
            else return;
        }

        // Drop rows from a previously-active provider so namespaces don't mix.
        var stale = await _db.Matches.Where(m => m.ExternalId != null && !m.ExternalId.StartsWith(ActivePrefix)).ToListAsync(ct);
        if (stale.Count > 0) _db.Matches.RemoveRange(stale);

        await UpsertAsync(incoming, ct);
        await _status.RecomputeAsync(ct);
        await _settings.SetAsync(SettingKeys.LastSyncUtc, DateTime.UtcNow.ToString("o"));
    }

    /// <summary>Cheap live-only refresh (api-football live=all). Falls back to a full sync without a key.</summary>
    public async Task LiveSyncAsync(CancellationToken ct = default)
    {
        if (!_apiFootball.IsAvailable)
        {
            await FullSyncAsync(ct);
            return;
        }
        var live = await Safe(() => _apiFootball.GetLiveMatchesAsync(ct));
        await UpsertAsync(live, ct);
        await _status.RecomputeAsync(ct);
        await _settings.SetAsync(SettingKeys.LastSyncUtc, DateTime.UtcNow.ToString("o"));
    }

    private async Task<IReadOnlyList<ProviderMatch>> Safe(Func<Task<IReadOnlyList<ProviderMatch>>> f)
    {
        try { return await f(); }
        catch (Exception ex) { _log.LogWarning(ex, "provider fetch failed"); return Array.Empty<ProviderMatch>(); }
    }

    private async Task UpsertAsync(IReadOnlyList<ProviderMatch> incoming, CancellationToken ct)
    {
        if (incoming.Count == 0) return;
        var teamsByCode = await _db.Teams.ToDictionaryAsync(t => t.FifaCode, ct);
        var existing = await _db.Matches.ToListAsync(ct);
        var byExternal = existing.Where(m => m.ExternalId != null).ToDictionary(m => m.ExternalId!);

        foreach (var pm in incoming)
        {
            if (!byExternal.TryGetValue(pm.ExternalId, out var row))
            {
                row = new Match { ExternalId = pm.ExternalId };
                _db.Matches.Add(row);
                byExternal[pm.ExternalId] = row;
            }

            row.Stage = pm.Stage;
            row.Label = pm.Label;
            row.KickoffUtc = pm.KickoffUtc;
            row.Status = pm.Status;
            row.HomeScore = pm.HomeScore;
            row.AwayScore = pm.AwayScore;
            row.Minute = pm.Minute;

            row.HomeTeamId = ResolveTeamId(teamsByCode, pm.HomeFifaCode);
            row.AwayTeamId = ResolveTeamId(teamsByCode, pm.AwayFifaCode);
            row.HomePlaceholder = row.HomeTeamId is null ? pm.HomePlaceholder : null;
            row.AwayPlaceholder = row.AwayTeamId is null ? pm.AwayPlaceholder : null;
            row.WinnerTeamId = ResolveTeamId(teamsByCode, pm.WinnerFifaCode);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static int? ResolveTeamId(Dictionary<string, Team> teamsByCode, string? code) =>
        code is not null && teamsByCode.TryGetValue(code, out var t) ? t.Id : null;
}
