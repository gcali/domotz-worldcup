using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Data;
using Worldcup.Api.Models;
using Worldcup.Api.Providers;

namespace Worldcup.Api.Services;

/// <summary>
/// Derives team alive/eliminated/champion status purely from match results.
/// Teams flagged <see cref="Team.ManualOverride"/> are left untouched.
/// </summary>
public class StatusService
{
    private readonly AppDbContext _db;
    private readonly ILogger<StatusService> _log;

    public StatusService(AppDbContext db, ILogger<StatusService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task RecomputeAsync(CancellationToken ct = default)
    {
        var teams = await _db.Teams.ToListAsync(ct);
        var matches = await _db.Matches.ToListAsync(ct);

        // Reset auto-managed teams to a clean slate.
        foreach (var t in teams.Where(t => !t.ManualOverride))
        {
            t.Status = TeamStatus.Alive;
            t.EliminatedStage = null;
            t.IsChampion = false;
        }

        // 1) Knockout losses -> loser eliminated at that stage (third-place playoff excluded).
        foreach (var m in matches.Where(m =>
                     m.Status == MatchStatus.Finished &&
                     Stages.AdvancingRounds.Contains(m.Stage) &&
                     m.WinnerTeamId is not null &&
                     m.HomeTeamId is not null && m.AwayTeamId is not null))
        {
            var loserId = m.WinnerTeamId == m.HomeTeamId ? m.AwayTeamId : m.HomeTeamId;
            Eliminate(teams, loserId, Stages.LabelOf(m.Stage));
        }

        // 2) Group non-qualification: once the Round of 32 is fully drawn, any team that never
        //    reached a knockout match is out at the group stage.
        var r32 = matches.Where(m => m.Stage == Stages.R32).ToList();
        var r32FullySet = r32.Count > 0 && r32.All(m => m.HomeTeamId is not null && m.AwayTeamId is not null);
        if (r32FullySet)
        {
            var knockoutTeamIds = matches
                .Where(m => Stages.IsKnockout(m.Stage))
                .SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId })
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .ToHashSet();
            foreach (var t in teams.Where(t => !t.ManualOverride && !knockoutTeamIds.Contains(t.Id)))
            {
                t.Status = TeamStatus.Eliminated;
                t.EliminatedStage = Stages.LabelOf(Stages.Group);
            }
        }

        // 3) Champion = winner of the final.
        var final = matches.FirstOrDefault(m =>
            m.Stage == Stages.Final && m.Status == MatchStatus.Finished && m.WinnerTeamId is not null);
        if (final is not null)
        {
            var champ = teams.FirstOrDefault(t => t.Id == final.WinnerTeamId && !t.ManualOverride);
            if (champ is not null)
            {
                champ.IsChampion = true;
                champ.Status = TeamStatus.Alive;
                champ.EliminatedStage = null;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static void Eliminate(List<Team> teams, int? teamId, string stageLabel)
    {
        var t = teams.FirstOrDefault(x => x.Id == teamId);
        if (t is null || t.ManualOverride) return;
        t.Status = TeamStatus.Eliminated;
        t.EliminatedStage = stageLabel;
    }
}
