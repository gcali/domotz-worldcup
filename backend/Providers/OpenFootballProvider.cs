using System.Globalization;
using System.Text.Json;
using Worldcup.Api.Models;

namespace Worldcup.Api.Providers;

/// <summary>
/// Reads the public-domain openfootball/worldcup.json feed. No API key, but not live —
/// scores appear once the maintainers commit them. Used to seed teams/fixtures and as a
/// results fallback.
/// </summary>
public class OpenFootballProvider : IResultsProvider
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly ILogger<OpenFootballProvider> _log;

    public OpenFootballProvider(HttpClient http, IConfiguration config, ILogger<OpenFootballProvider> log)
    {
        _http = http;
        _log = log;
        _url = config["OpenFootball:Url"]
               ?? "https://raw.githubusercontent.com/openfootball/worldcup.json/master/2026/worldcup.json";
    }

    public string Name => "openfootball";
    public bool IsAvailable => true;

    private async Task<JsonElement[]> FetchMatchesAsync(CancellationToken ct)
    {
        using var stream = await _http.GetStreamAsync(_url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("matches", out var matches))
            return Array.Empty<JsonElement>();
        return matches.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    public async Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(CancellationToken ct)
    {
        var matches = await FetchMatchesAsync(ct);
        var seen = new Dictionary<string, ProviderTeam>();
        foreach (var m in matches)
        {
            if (!m.TryGetProperty("group", out var groupEl) || groupEl.ValueKind != JsonValueKind.String)
                continue;
            var group = groupEl.GetString()!;
            foreach (var key in new[] { "team1", "team2" })
            {
                var name = m.TryGetProperty(key, out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var code = TeamCatalog.CodeForName(name);
                if (code is null)
                {
                    _log.LogWarning("openfootball: no FIFA code for team name '{Name}'", name);
                    continue;
                }
                seen[code] = new ProviderTeam(code, TeamCatalog.CanonicalName(code), TeamCatalog.EmojiForCode(code), group);
            }
        }
        return seen.Values.ToList();
    }

    public async Task<IReadOnlyList<ProviderMatch>> GetMatchesAsync(CancellationToken ct)
    {
        var matches = await FetchMatchesAsync(ct);
        var result = new List<ProviderMatch>();
        foreach (var m in matches)
        {
            var round = m.TryGetProperty("round", out var r) ? r.GetString() ?? "" : "";
            var hasGroup = m.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String;
            var stage = Stages.FromRoundName(round, hasGroup);
            var label = hasGroup ? g.GetString()! : Stages.LabelOf(stage);

            var t1 = m.TryGetProperty("team1", out var a) ? a.GetString() : null;
            var t2 = m.TryGetProperty("team2", out var b) ? b.GetString() : null;
            var (homeCode, homePh) = Resolve(t1);
            var (awayCode, awayPh) = Resolve(t2);

            var date = m.TryGetProperty("date", out var d) ? d.GetString() : null;
            var time = m.TryGetProperty("time", out var ti) ? ti.GetString() : null;
            var kickoff = ParseKickoff(date, time);

            int? homeScore = null, awayScore = null;
            string? winnerCode = null;
            var status = MatchStatus.Scheduled;
            if (m.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Object)
            {
                var (hs, as_) = ReadScore(score, "ft");
                if (hs is not null && as_ is not null)
                {
                    homeScore = hs; awayScore = as_;
                    status = MatchStatus.Finished;
                    winnerCode = DecideWinner(score, hs.Value, as_.Value, homeCode, awayCode);
                }
            }

            // Knockout fixtures carry a stable feed match number ("num"); key on it so a slot
            // resolving from a placeholder ("2A") to a real team keeps the same identity. Group
            // matches have no "num" but stable team names, so fall back to the name-based key.
            var num = m.TryGetProperty("num", out var nEl) && nEl.TryGetInt32(out var nv) ? (int?)nv : null;
            var externalId = num is not null ? $"of:{num}" : $"of:{round}:{t1}-vs-{t2}:{date}";
            result.Add(new ProviderMatch(externalId, stage, label, kickoff,
                homeCode, awayCode, homePh, awayPh, status, homeScore, awayScore, winnerCode, null));
        }
        return result;
    }

    private static (string? code, string? placeholder) Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (null, null);
        var code = TeamCatalog.CodeForName(name);
        return code is not null ? (code, null) : (null, name);
    }

    private static (int? home, int? away) ReadScore(JsonElement score, string key)
    {
        if (score.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
        {
            var vals = arr.EnumerateArray().ToArray();
            if (vals[0].TryGetInt32(out var h) && vals[1].TryGetInt32(out var a))
                return (h, a);
        }
        return (null, null);
    }

    private static string? DecideWinner(JsonElement score, int home, int away, string? homeCode, string? awayCode)
    {
        if (home != away) return home > away ? homeCode : awayCode;
        // Draw at full time — check penalties ("p").
        var (ph, pa) = ReadScore(score, "p");
        if (ph is not null && pa is not null && ph != pa) return ph > pa ? homeCode : awayCode;
        return null;
    }

    /// <summary>Parses "2026-06-11" + "13:00 UTC-6" into a UTC DateTime.</summary>
    internal static DateTime ParseKickoff(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date) ||
            !DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day))
            return DateTime.UtcNow;

        var hour = 12; var minute = 0; var offsetHours = 0;
        if (!string.IsNullOrWhiteSpace(time))
        {
            var parts = time.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && TimeOnly.TryParse(parts[0], CultureInfo.InvariantCulture, out var to))
            {
                hour = to.Hour; minute = to.Minute;
            }
            if (parts.Length >= 2)
            {
                var off = parts[1].Replace("UTC", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (int.TryParse(off, out var oh)) offsetHours = oh;
            }
        }
        // Local time at the given UTC offset -> subtract the offset to get UTC.
        return new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Utc).AddHours(-offsetHours);
    }
}
