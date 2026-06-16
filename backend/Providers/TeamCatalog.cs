namespace Worldcup.Api.Providers;

/// <summary>
/// Static lookup mapping country names (as they appear in feeds) to a FIFA code and flag emoji.
/// Covers the 48 World Cup 2026 participants plus common aliases used by api-sports.io so that
/// the live provider's slightly different names still reconcile to the seeded teams.
/// </summary>
public static class TeamCatalog
{
    public record Entry(string Code, string Emoji, string Canonical);

    // code -> (emoji, canonical display name)
    private static readonly Dictionary<string, (string Emoji, string Name)> ByCode = new()
    {
        ["MEX"] = ("🇲🇽", "Mexico"),
        ["RSA"] = ("🇿🇦", "South Africa"),
        ["CZE"] = ("🇨🇿", "Czech Republic"),
        ["KOR"] = ("🇰🇷", "South Korea"),
        ["CAN"] = ("🇨🇦", "Canada"),
        ["QAT"] = ("🇶🇦", "Qatar"),
        ["SUI"] = ("🇨🇭", "Switzerland"),
        ["BIH"] = ("🇧🇦", "Bosnia & Herzegovina"),
        ["BRA"] = ("🇧🇷", "Brazil"),
        ["HAI"] = ("🇭🇹", "Haiti"),
        ["MAR"] = ("🇲🇦", "Morocco"),
        ["SCO"] = ("🏴\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007F", "Scotland"),
        ["USA"] = ("🇺🇸", "USA"),
        ["AUS"] = ("🇦🇺", "Australia"),
        ["PAR"] = ("🇵🇾", "Paraguay"),
        ["TUR"] = ("🇹🇷", "Turkey"),
        ["GER"] = ("🇩🇪", "Germany"),
        ["CUW"] = ("🇨🇼", "Curaçao"),
        ["ECU"] = ("🇪🇨", "Ecuador"),
        ["CIV"] = ("🇨🇮", "Ivory Coast"),
        ["NED"] = ("🇳🇱", "Netherlands"),
        ["JPN"] = ("🇯🇵", "Japan"),
        ["SWE"] = ("🇸🇪", "Sweden"),
        ["TUN"] = ("🇹🇳", "Tunisia"),
        ["BEL"] = ("🇧🇪", "Belgium"),
        ["EGY"] = ("🇪🇬", "Egypt"),
        ["IRN"] = ("🇮🇷", "Iran"),
        ["NZL"] = ("🇳🇿", "New Zealand"),
        ["ESP"] = ("🇪🇸", "Spain"),
        ["CPV"] = ("🇨🇻", "Cape Verde"),
        ["KSA"] = ("🇸🇦", "Saudi Arabia"),
        ["URU"] = ("🇺🇾", "Uruguay"),
        ["FRA"] = ("🇫🇷", "France"),
        ["IRQ"] = ("🇮🇶", "Iraq"),
        ["NOR"] = ("🇳🇴", "Norway"),
        ["SEN"] = ("🇸🇳", "Senegal"),
        ["ARG"] = ("🇦🇷", "Argentina"),
        ["ALG"] = ("🇩🇿", "Algeria"),
        ["AUT"] = ("🇦🇹", "Austria"),
        ["JOR"] = ("🇯🇴", "Jordan"),
        ["POR"] = ("🇵🇹", "Portugal"),
        ["COL"] = ("🇨🇴", "Colombia"),
        ["COD"] = ("🇨🇩", "DR Congo"),
        ["UZB"] = ("🇺🇿", "Uzbekistan"),
        ["ENG"] = ("🏴\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F", "England"),
        ["CRO"] = ("🇭🇷", "Croatia"),
        ["GHA"] = ("🇬🇭", "Ghana"),
        ["PAN"] = ("🇵🇦", "Panama"),
    };

    // normalized name (incl. aliases) -> code
    private static readonly Dictionary<string, string> NameToCode = BuildNameIndex();

    private static Dictionary<string, string> BuildNameIndex()
    {
        var map = new Dictionary<string, string>();
        foreach (var (code, val) in ByCode)
            map[Normalize(val.Name)] = code;

        // Aliases used by api-sports.io / other feeds.
        void Alias(string name, string code) => map[Normalize(name)] = code;
        Alias("United States", "USA");
        Alias("United States of America", "USA");
        Alias("Korea Republic", "KOR");
        Alias("Republic of Korea", "KOR");
        Alias("IR Iran", "IRN");
        Alias("Iran (Islamic Republic of)", "IRN");
        Alias("Côte d'Ivoire", "CIV");
        Alias("Cote d'Ivoire", "CIV");
        Alias("Czechia", "CZE");
        Alias("Türkiye", "TUR");
        Alias("Turkiye", "TUR");
        Alias("Cabo Verde", "CPV");
        Alias("Bosnia and Herzegovina", "BIH");
        Alias("Bosnia-Herzegovina", "BIH");
        Alias("DR Congo", "COD");
        Alias("Congo DR", "COD");
        Alias("Democratic Republic of the Congo", "COD");
        Alias("Curacao", "CUW");
        return map;
    }

    public static string Normalize(string s) =>
        new string(s.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-' && c != '&').ToArray())
            .ToLowerInvariant()
            .Replace("'", "");

    public static string? CodeForName(string name) =>
        NameToCode.TryGetValue(Normalize(name), out var code) ? code : null;

    public static string EmojiForCode(string code) =>
        ByCode.TryGetValue(code, out var v) ? v.Emoji : "🏳️";

    public static string CanonicalName(string code) =>
        ByCode.TryGetValue(code, out var v) ? v.Name : code;
}
