using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Data;
using Worldcup.Api.Models;

namespace Worldcup.Api.Services;

// ---- DTOs returned to the frontend ----

public record SideDto(int? TeamId, string? Code, string Name, string? Flag, bool IsPlaceholder);

public record MatchDto(
    int Id, string Stage, string Label, DateTime KickoffUtc, string Status,
    SideDto Home, SideDto Away, int? HomeScore, int? AwayScore, string? Minute);

public record TeamBoardDto(
    int Id, string Code, string Name, string Flag, string Group,
    string Status, string? EliminatedStage, bool IsChampion,
    MatchDto? LiveMatch, MatchDto? NextMatch);

public record PlayerBoardDto(int Id, string Name, int AliveCount, List<TeamBoardDto> Teams);

public record ChampionDto(int TeamId, string Code, string Name, string Flag, List<string> Players);

public record BoardDto(
    int EntryFee, int PlayerCount, int PotTotal, string Currency,
    string? LastSyncUtc, string DataProvider,
    ChampionDto? Champion, List<MatchDto> LiveMatches, List<PlayerBoardDto> Players);

public class BoardService
{
    private readonly AppDbContext _db;
    private readonly SettingsStore _settings;

    public BoardService(AppDbContext db, SettingsStore settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<BoardDto> BuildAsync(CancellationToken ct = default)
    {
        var teams = await _db.Teams.AsNoTracking().ToListAsync(ct);
        var teamsById = teams.ToDictionary(t => t.Id);
        var matches = await _db.Matches.AsNoTracking().ToListAsync(ct);
        var players = await _db.Players.AsNoTracking()
            .Include(p => p.Assignments).OrderBy(p => p.Name).ToListAsync(ct);

        var now = DateTime.UtcNow;

        MatchDto ToDto(Match m) => new(
            m.Id, m.Stage, m.Label, m.KickoffUtc, m.Status.ToString(),
            Side(m.HomeTeamId, m.HomePlaceholder), Side(m.AwayTeamId, m.AwayPlaceholder),
            m.HomeScore, m.AwayScore, m.Minute);

        SideDto Side(int? teamId, string? placeholder)
        {
            if (teamId is { } id && teamsById.TryGetValue(id, out var t))
                return new SideDto(t.Id, t.FifaCode, t.Name, t.FlagEmoji, false);
            return new SideDto(null, null, placeholder ?? "TBD", null, true);
        }

        MatchDto? LiveFor(int teamId) => matches
            .Where(m => m.Status == MatchStatus.InPlay && (m.HomeTeamId == teamId || m.AwayTeamId == teamId))
            .OrderBy(m => m.KickoffUtc).Select(ToDto).FirstOrDefault();

        MatchDto? NextFor(int teamId) => matches
            .Where(m => m.Status != MatchStatus.Finished && m.Status != MatchStatus.InPlay &&
                        m.KickoffUtc >= now && (m.HomeTeamId == teamId || m.AwayTeamId == teamId))
            .OrderBy(m => m.KickoffUtc).Select(ToDto).FirstOrDefault();

        TeamBoardDto TeamDto(Team t) => new(
            t.Id, t.FifaCode, t.Name, t.FlagEmoji, t.GroupName,
            t.Status.ToString(), t.EliminatedStage, t.IsChampion,
            LiveFor(t.Id), NextFor(t.Id));

        var playerDtos = players.Select(p =>
        {
            var owned = p.Assignments
                .Select(a => teamsById.TryGetValue(a.TeamId, out var t) ? t : null)
                .Where(t => t is not null).Cast<Team>()
                .OrderByDescending(t => t.IsChampion)
                .ThenBy(t => t.Status)
                .ThenBy(t => t.Name)
                .Select(TeamDto).ToList();
            var alive = owned.Count(t => t.Status == nameof(TeamStatus.Alive));
            return new PlayerBoardDto(p.Id, p.Name, alive, owned);
        })
        .OrderByDescending(p => p.Teams.Any(t => t.IsChampion))
        .ThenByDescending(p => p.AliveCount)
        .ThenBy(p => p.Name)
        .ToList();

        var entryFee = await _settings.EntryFeeAsync();
        var lastSync = await _settings.GetAsync(SettingKeys.LastSyncUtc);
        var provider = await _settings.GetAsync(SettingKeys.DataProvider) ?? "auto";

        ChampionDto? champion = null;
        var champ = teams.FirstOrDefault(t => t.IsChampion);
        if (champ is not null)
        {
            var owners = players
                .Where(p => p.Assignments.Any(a => a.TeamId == champ.Id))
                .Select(p => p.Name).OrderBy(n => n).ToList();
            champion = new ChampionDto(champ.Id, champ.FifaCode, champ.Name, champ.FlagEmoji, owners);
        }

        var live = matches.Where(m => m.Status == MatchStatus.InPlay)
            .OrderBy(m => m.KickoffUtc).Select(ToDto).ToList();

        return new BoardDto(entryFee, players.Count, entryFee * players.Count, "€",
            lastSync, provider, champion, live, playerDtos);
    }

    public async Task<List<MatchDto>> MatchesAsync(CancellationToken ct = default)
    {
        var teams = await _db.Teams.AsNoTracking().ToListAsync(ct);
        var teamsById = teams.ToDictionary(t => t.Id);
        var matches = await _db.Matches.AsNoTracking().OrderBy(m => m.KickoffUtc).ToListAsync(ct);

        SideDto Side(int? teamId, string? placeholder)
        {
            if (teamId is { } id && teamsById.TryGetValue(id, out var t))
                return new SideDto(t.Id, t.FifaCode, t.Name, t.FlagEmoji, false);
            return new SideDto(null, null, placeholder ?? "TBD", null, true);
        }

        return matches.Select(m => new MatchDto(
            m.Id, m.Stage, m.Label, m.KickoffUtc, m.Status.ToString(),
            Side(m.HomeTeamId, m.HomePlaceholder), Side(m.AwayTeamId, m.AwayPlaceholder),
            m.HomeScore, m.AwayScore, m.Minute)).ToList();
    }
}
