namespace Worldcup.Api.Models;

public enum TeamStatus { Alive, Eliminated }

public enum MatchStatus { Scheduled, InPlay, Finished }

public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Assignment> Assignments { get; set; } = new();
}

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string FifaCode { get; set; } = "";
    public string FlagEmoji { get; set; } = "";
    public string GroupName { get; set; } = "";
    public TeamStatus Status { get; set; } = TeamStatus.Alive;

    /// <summary>Human-readable stage where the team went out, e.g. "Group stage", "Round of 16". Null while alive.</summary>
    public string? EliminatedStage { get; set; }

    /// <summary>True for the team that won the final — its owner takes the pot.</summary>
    public bool IsChampion { get; set; }

    /// <summary>Provider-specific id used to reconcile incoming results with this row.</summary>
    public string? ExternalId { get; set; }

    /// <summary>When true, status/champion were set by the admin and the auto-recompute leaves them alone.</summary>
    public bool ManualOverride { get; set; }
}

public class Assignment
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;
}

public class Match
{
    public int Id { get; set; }
    public string? ExternalId { get; set; }

    // Nullable: knockout slots exist before their teams are decided ("Winner Group A").
    public int? HomeTeamId { get; set; }
    public Team? HomeTeam { get; set; }
    public int? AwayTeamId { get; set; }
    public Team? AwayTeam { get; set; }

    // Placeholder labels shown when a knockout slot has no concrete team yet.
    public string? HomePlaceholder { get; set; }
    public string? AwayPlaceholder { get; set; }

    public DateTime KickoffUtc { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    /// <summary>Canonical stage key: group, r32, r16, qf, sf, third, final.</summary>
    public string Stage { get; set; } = "group";

    /// <summary>Display label, e.g. "Group A", "Round of 16".</summary>
    public string Label { get; set; } = "";

    /// <summary>Set by the provider (handles penalties) or derived from score for non-draws.</summary>
    public int? WinnerTeamId { get; set; }

    /// <summary>Live minute display while InPlay, e.g. "67'", "HT".</summary>
    public string? Minute { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public static class SettingKeys
{
    public const string EntryFee = "EntryFee";
    public const string AdminToken = "AdminToken";
    public const string DataProvider = "DataProvider";
    public const string LivePollSeconds = "LivePollSeconds";
    public const string IdlePollSeconds = "IdlePollSeconds";
    public const string LastSyncUtc = "LastSyncUtc";
    public const string Seeded = "Seeded";
}
