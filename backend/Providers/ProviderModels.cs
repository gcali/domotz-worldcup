using Worldcup.Api.Models;

namespace Worldcup.Api.Providers;

/// <summary>A team as reported by a data provider (before it is reconciled with the DB).</summary>
public record ProviderTeam(string FifaCode, string Name, string FlagEmoji, string GroupName);

/// <summary>A match as reported by a data provider. Team slots are FIFA codes, or null with a placeholder for undecided knockout slots.</summary>
public record ProviderMatch(
    string ExternalId,
    string Stage,
    string Label,
    DateTime KickoffUtc,
    string? HomeFifaCode,
    string? AwayFifaCode,
    string? HomePlaceholder,
    string? AwayPlaceholder,
    MatchStatus Status,
    int? HomeScore,
    int? AwayScore,
    string? WinnerFifaCode,
    string? Minute);

public interface IResultsProvider
{
    string Name { get; }

    /// <summary>Whether this provider is usable right now (e.g. an API key is configured).</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(CancellationToken ct);
    Task<IReadOnlyList<ProviderMatch>> GetMatchesAsync(CancellationToken ct);

    /// <summary>Cheap fetch of only the in-progress matches. Defaults to the full fetch (callers filter).</summary>
    Task<IReadOnlyList<ProviderMatch>> GetLiveMatchesAsync(CancellationToken ct) => GetMatchesAsync(ct);
}
