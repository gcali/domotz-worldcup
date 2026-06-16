using System.Text.Json;
using System.Text.RegularExpressions;
using Worldcup.Api.Models;
using Worldcup.Api.Services;

namespace Worldcup.Api.Providers;

/// <summary>
/// api-sports.io (API-Football) v3 provider. Free plan = 100 req/day, but includes live
/// in-play scores. <c>GetLiveMatchesAsync</c> uses the single cheap <c>fixtures?live=all</c>
/// call which returns every live match at once.
/// </summary>
public partial class ApiFootballProvider : IResultsProvider
{
    private readonly HttpClient _http;
    private readonly ApiCallBudget _budget;
    private readonly string? _apiKey;
    private readonly string _leagueId;
    private readonly string _season;
    private readonly ILogger<ApiFootballProvider> _log;

    public ApiFootballProvider(HttpClient http, IConfiguration config, ApiCallBudget budget, ILogger<ApiFootballProvider> log)
    {
        _http = http;
        _budget = budget;
        _log = log;
        _apiKey = config["ApiFootball:ApiKey"];
        _leagueId = config["ApiFootball:LeagueId"] ?? "1"; // 1 = FIFA World Cup
        _season = config["ApiFootball:Season"] ?? "2026";
        var baseUrl = config["ApiFootball:BaseUrl"] ?? "https://v3.football.api-sports.io";
        _http.BaseAddress = new Uri(baseUrl);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("x-apisports-key", _apiKey);
    }

    public string Name => "api-football";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(CancellationToken ct) =>
        // Teams are seeded from openfootball; no need to spend quota here.
        Task.FromResult<IReadOnlyList<ProviderTeam>>(Array.Empty<ProviderTeam>());

    public async Task<IReadOnlyList<ProviderMatch>> GetMatchesAsync(CancellationToken ct) =>
        await FetchAsync($"/fixtures?league={_leagueId}&season={_season}", ct);

    public async Task<IReadOnlyList<ProviderMatch>> GetLiveMatchesAsync(CancellationToken ct)
    {
        var live = await FetchAsync("/fixtures?live=all", ct);
        // live=all spans every competition; keep only our tournament's matches.
        return live;
    }

    private async Task<IReadOnlyList<ProviderMatch>> FetchAsync(string path, CancellationToken ct)
    {
        if (!IsAvailable) return Array.Empty<ProviderMatch>();
        if (!_budget.TryConsume())
        {
            _log.LogWarning("api-football daily request budget exhausted; skipping {Path}", path);
            return Array.Empty<ProviderMatch>();
        }
        using var resp = await _http.GetAsync(path, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("api-football {Path} returned {Status}", path, resp.StatusCode);
            return Array.Empty<ProviderMatch>();
        }
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Array)
            return Array.Empty<ProviderMatch>();

        var result = new List<ProviderMatch>();
        foreach (var item in response.EnumerateArray())
        {
            // For live=all, restrict to our league.
            if (item.TryGetProperty("league", out var lg) &&
                lg.TryGetProperty("id", out var lid) && lid.ValueKind == JsonValueKind.Number &&
                int.TryParse(_leagueId, out var wantLeague) && lid.GetInt32() != wantLeague)
                continue;

            var fixture = item.GetProperty("fixture");
            var id = fixture.GetProperty("id").GetRawText();
            var dateStr = fixture.TryGetProperty("date", out var dt) ? dt.GetString() : null;
            var kickoff = DateTimeOffset.TryParse(dateStr, out var dto) ? dto.UtcDateTime : DateTime.UtcNow;

            var statusShort = fixture.GetProperty("status").TryGetProperty("short", out var ss) ? ss.GetString() ?? "" : "";
            var elapsed = fixture.GetProperty("status").TryGetProperty("elapsed", out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetInt32() : (int?)null;
            var status = MapStatus(statusShort);
            var minute = status == MatchStatus.InPlay
                ? statusShort == "HT" ? "HT" : elapsed is not null ? $"{elapsed}'" : "LIVE"
                : null;

            var roundStr = item.TryGetProperty("league", out var league) && league.TryGetProperty("round", out var rd)
                ? rd.GetString() ?? "" : "";
            var hasGroup = roundStr.Contains("Group", StringComparison.OrdinalIgnoreCase);
            var stage = Stages.FromRoundName(roundStr, hasGroup);
            var label = hasGroup ? ExtractGroupLabel(roundStr) : Stages.LabelOf(stage);

            var teams = item.GetProperty("teams");
            var (homeCode, homePh, homeWinner) = ReadTeam(teams.GetProperty("home"));
            var (awayCode, awayPh, awayWinner) = ReadTeam(teams.GetProperty("away"));

            var goals = item.GetProperty("goals");
            int? homeScore = goals.TryGetProperty("home", out var gh) && gh.ValueKind == JsonValueKind.Number ? gh.GetInt32() : null;
            int? awayScore = goals.TryGetProperty("away", out var ga) && ga.ValueKind == JsonValueKind.Number ? ga.GetInt32() : null;

            string? winnerCode = homeWinner == true ? homeCode : awayWinner == true ? awayCode : null;

            result.Add(new ProviderMatch($"af:{id}", stage, label, kickoff,
                homeCode, awayCode, homePh, awayPh, status, homeScore, awayScore, winnerCode, minute));
        }
        return result;
    }

    private static (string? code, string? placeholder, bool? winner) ReadTeam(JsonElement team)
    {
        var name = team.TryGetProperty("name", out var n) ? n.GetString() : null;
        bool? winner = team.TryGetProperty("winner", out var w) && w.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? w.GetBoolean() : null;
        if (string.IsNullOrWhiteSpace(name)) return (null, null, winner);
        var code = TeamCatalog.CodeForName(name);
        return code is not null ? (code, null, winner) : (null, name, winner);
    }

    private static MatchStatus MapStatus(string s) => s switch
    {
        "1H" or "HT" or "2H" or "ET" or "BT" or "P" or "LIVE" or "INT" => MatchStatus.InPlay,
        "FT" or "AET" or "PEN" => MatchStatus.Finished,
        _ => MatchStatus.Scheduled,
    };

    private static string ExtractGroupLabel(string round)
    {
        var m = GroupRegex().Match(round);
        return m.Success ? $"Group {m.Groups[1].Value}" : "Group stage";
    }

    [GeneratedRegex(@"Group\s+([A-L])", RegexOptions.IgnoreCase)]
    private static partial Regex GroupRegex();
}
