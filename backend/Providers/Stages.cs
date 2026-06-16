namespace Worldcup.Api.Providers;

/// <summary>Canonical tournament stages, ordering, and display labels.</summary>
public static class Stages
{
    public const string Group = "group";
    public const string R32 = "r32";
    public const string R16 = "r16";
    public const string QF = "qf";
    public const string SF = "sf";
    public const string Third = "third";
    public const string Final = "final";

    private static readonly Dictionary<string, int> Order = new()
    {
        [Group] = 0, [R32] = 1, [R16] = 2, [QF] = 3, [SF] = 4, [Third] = 5, [Final] = 6,
    };

    private static readonly Dictionary<string, string> Labels = new()
    {
        [Group] = "Group stage",
        [R32] = "Round of 32",
        [R16] = "Round of 16",
        [QF] = "Quarter-final",
        [SF] = "Semi-final",
        [Third] = "Third place",
        [Final] = "Final",
    };

    /// <summary>Knockout rounds where winning means advancing (third-place playoff excluded).</summary>
    public static readonly string[] AdvancingRounds = { R32, R16, QF, SF, Final };

    public static int OrderOf(string stage) => Order.TryGetValue(stage, out var o) ? o : -1;

    public static string LabelOf(string stage) => Labels.TryGetValue(stage, out var l) ? l : stage;

    public static bool IsKnockout(string stage) => stage != Group && Order.ContainsKey(stage);

    /// <summary>Maps a feed round string ("Round of 32", "Quarter-final", "Matchday 3"…) to a canonical stage.</summary>
    public static string FromRoundName(string round, bool hasGroup)
    {
        if (hasGroup) return Group;
        var r = round.Trim().ToLowerInvariant();
        if (r.Contains("round of 32")) return R32;
        if (r.Contains("round of 16")) return R16;
        if (r.Contains("quarter")) return QF;
        if (r.Contains("semi")) return SF;
        if (r.Contains("third")) return Third;
        if (r.Contains("final")) return Final;
        return Group; // "Matchday N" without an explicit group
    }
}
