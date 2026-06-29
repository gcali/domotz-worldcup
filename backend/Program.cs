using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Data;
using Worldcup.Api.Models;
using Worldcup.Api.Providers;
using Worldcup.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=worldcup.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connString));

builder.Services.AddHttpClient<OpenFootballProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<ApiFootballProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ApiCallBudget>();
builder.Services.AddScoped<SettingsStore>();
builder.Services.AddScoped<StatusService>();
builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddHostedService<PollerService>();

builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// --- Startup: create schema + seed admin token ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    var settings = scope.ServiceProvider.GetRequiredService<SettingsStore>();
    if (await settings.GetAsync(SettingKeys.AdminToken) is null)
    {
        var token = app.Configuration["Admin:Token"] ?? "changeme";
        await settings.SetAsync(SettingKeys.AdminToken, token);
        app.Logger.LogInformation("Seeded admin token from config (Admin:Token, env Admin__Token).");
    }
}

if (app.Environment.IsDevelopment())
    app.UseCors("dev");

// Serve the built React SPA (wwwroot) for non-API routes.
app.UseDefaultFiles();
app.UseStaticFiles();

// ---- Admin auth helper ----
async Task<bool> IsAdmin(HttpContext ctx, SettingsStore settings)
{
    var token = ctx.Request.Headers["X-Admin-Token"].ToString();
    var expected = await settings.GetAsync(SettingKeys.AdminToken);
    return !string.IsNullOrEmpty(expected) && CryptoEquals(token, expected);
}

static bool CryptoEquals(string a, string b) =>
    System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

// ================= Public API =================
var api = app.MapGroup("/api");

api.MapGet("/board", async (BoardService board, CancellationToken ct) =>
    Results.Ok(await board.BuildAsync(ct)));

api.MapGet("/matches", async (BoardService board, CancellationToken ct) =>
    Results.Ok(await board.MatchesAsync(ct)));

api.MapGet("/teams", async (AppDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Teams.AsNoTracking().OrderBy(t => t.GroupName).ThenBy(t => t.Name)
        .Select(t => new { t.Id, t.FifaCode, t.Name, t.FlagEmoji, t.GroupName, Status = t.Status.ToString(), t.IsChampion, t.ManualOverride })
        .ToListAsync(ct)));

api.MapGet("/players", async (AppDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Players.AsNoTracking().Include(p => p.Assignments)
        .OrderBy(p => p.Name)
        .Select(p => new { p.Id, p.Name, TeamIds = p.Assignments.Select(a => a.TeamId).ToList() })
        .ToListAsync(ct)));

// ================= Admin API =================
var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
{
    var settings = ctx.HttpContext.RequestServices.GetRequiredService<SettingsStore>();
    if (!await IsAdmin(ctx.HttpContext, settings))
        return Results.Unauthorized();
    return await next(ctx);
});

admin.MapPost("/players", async (CreatePlayerReq req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name required");
    var p = new Player { Name = req.Name.Trim() };
    db.Players.Add(p);
    await db.SaveChangesAsync();
    return Results.Ok(new { p.Id, p.Name });
});

admin.MapDelete("/players/{id:int}", async (int id, AppDbContext db) =>
{
    var p = await db.Players.FindAsync(id);
    if (p is null) return Results.NotFound();
    db.Players.Remove(p);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

admin.MapPost("/assignments", async (CreateAssignmentReq req, AppDbContext db) =>
{
    var player = await db.Players.FindAsync(req.PlayerId);
    if (player is null) return Results.BadRequest("Unknown player");

    var team = req.TeamId is { } tid
        ? await db.Teams.FindAsync(tid)
        : req.TeamCode is { } code
            ? await db.Teams.FirstOrDefaultAsync(t => t.FifaCode == code)
            : null;
    if (team is null) return Results.BadRequest("Unknown team");

    if (await db.Assignments.AnyAsync(a => a.PlayerId == player.Id && a.TeamId == team.Id))
        return Results.Conflict("Already assigned");

    var a = new Assignment { PlayerId = player.Id, TeamId = team.Id };
    db.Assignments.Add(a);
    await db.SaveChangesAsync();
    return Results.Ok(new { a.Id, a.PlayerId, a.TeamId });
});

admin.MapDelete("/assignments", async (int playerId, int teamId, AppDbContext db) =>
{
    var a = await db.Assignments.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.TeamId == teamId);
    if (a is null) return Results.NotFound();
    db.Assignments.Remove(a);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

admin.MapPut("/teams/{id:int}/status", async (int id, TeamStatusReq req, AppDbContext db, StatusService status) =>
{
    var team = await db.Teams.FindAsync(id);
    if (team is null) return Results.NotFound();

    if (req.ClearOverride == true)
    {
        team.ManualOverride = false;
    }
    else
    {
        team.ManualOverride = true;
        if (req.Status is not null)
        {
            if (req.Status.Equals("eliminated", StringComparison.OrdinalIgnoreCase))
            {
                team.Status = TeamStatus.Eliminated;
                team.EliminatedStage = req.EliminatedStage ?? "Eliminated";
                team.IsChampion = false;
            }
            else
            {
                team.Status = TeamStatus.Alive;
                team.EliminatedStage = null;
            }
        }
        if (req.IsChampion is { } champ)
        {
            team.IsChampion = champ;
            if (champ) { team.Status = TeamStatus.Alive; team.EliminatedStage = null; }
        }
    }
    await db.SaveChangesAsync();
    await status.RecomputeAsync();   // reapply auto-rules to non-overridden teams
    return Results.Ok(new { team.Id, Status = team.Status.ToString(), team.EliminatedStage, team.IsChampion, team.ManualOverride });
});

// Export the current bet state (participants + the teams they picked). Teams are
// identified by FIFA code so the dump stays portable across DB re-seeds.
admin.MapGet("/export", async (AppDbContext db, CancellationToken ct) =>
{
    var players = await db.Players.AsNoTracking()
        .Include(p => p.Assignments).ThenInclude(a => a.Team)
        .OrderBy(p => p.Name)
        .ToListAsync(ct);
    var dto = new BetStateExport(
        players.Select(p => new BetStatePlayer(
            p.Name,
            p.Assignments.Select(a => a.Team.FifaCode).OrderBy(c => c).ToList()))
        .ToList());
    return Results.Ok(dto);
});

// Completely replace the bet state with the supplied dump. Validated fully before
// anything is mutated, then applied atomically.
admin.MapPost("/import", async (BetStateExport req, AppDbContext db, CancellationToken ct) =>
{
    if (req?.Players is null)
        return Results.BadRequest("Body must be { \"players\": [ { \"name\": ..., \"teams\": [...] } ] }");

    var idByCode = (await db.Teams.AsNoTracking().ToListAsync(ct))
        .ToDictionary(t => t.FifaCode, t => t.Id, StringComparer.OrdinalIgnoreCase);

    var prepared = new List<(string Name, List<int> TeamIds)>();
    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var p in req.Players)
    {
        if (string.IsNullOrWhiteSpace(p.Name))
            return Results.BadRequest("Every player needs a non-empty name");
        var name = p.Name.Trim();
        if (!seenNames.Add(name))
            return Results.BadRequest($"Duplicate player '{name}'");

        var ids = new List<int>();
        foreach (var code in p.Teams ?? new())
        {
            if (!idByCode.TryGetValue(code, out var id))
                return Results.BadRequest($"Unknown team code '{code}' for player '{name}'");
            if (ids.Contains(id))
                return Results.BadRequest($"Duplicate team '{code}' for player '{name}'");
            ids.Add(id);
        }
        prepared.Add((name, ids));
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    db.Assignments.RemoveRange(db.Assignments);
    db.Players.RemoveRange(db.Players);
    await db.SaveChangesAsync(ct);

    foreach (var (name, ids) in prepared)
        db.Players.Add(new Player
        {
            Name = name,
            Assignments = ids.Select(id => new Assignment { TeamId = id }).ToList(),
        });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Ok(new { ok = true, players = prepared.Count });
});

admin.MapPost("/sync", async (IngestService ingest, CancellationToken ct) =>
{
    await ingest.FullSyncAsync(ct);
    return Results.Ok(new { ok = true });
});

admin.MapPut("/settings", async (SettingsReq req, SettingsStore settings) =>
{
    if (req.EntryFee is { } fee) await settings.SetAsync(SettingKeys.EntryFee, fee.ToString());
    if (req.LivePollSeconds is { } lp) await settings.SetAsync(SettingKeys.LivePollSeconds, lp.ToString());
    if (req.IdlePollSeconds is { } ip) await settings.SetAsync(SettingKeys.IdlePollSeconds, ip.ToString());
    if (!string.IsNullOrWhiteSpace(req.AdminToken)) await settings.SetAsync(SettingKeys.AdminToken, req.AdminToken!);
    if (!string.IsNullOrWhiteSpace(req.DataProvider)) await settings.SetAsync(SettingKeys.DataProvider, req.DataProvider!);
    return Results.Ok(new { ok = true });
});

// SPA fallback (after API routes).
app.MapFallbackToFile("index.html");

app.Run();

// ---- Request DTOs ----
record CreatePlayerReq(string Name);
record CreateAssignmentReq(int PlayerId, string? TeamCode, int? TeamId);
record TeamStatusReq(string? Status, string? EliminatedStage, bool? IsChampion, bool? ClearOverride);
record SettingsReq(int? EntryFee, int? LivePollSeconds, int? IdlePollSeconds, string? AdminToken, string? DataProvider);

// ---- Bet-state export/import DTOs ----
record BetStateExport(List<BetStatePlayer> Players);
record BetStatePlayer(string Name, List<string> Teams);
