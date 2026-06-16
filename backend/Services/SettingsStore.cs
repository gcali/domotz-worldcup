using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Data;
using Worldcup.Api.Models;

namespace Worldcup.Api.Services;

/// <summary>Typed access to the Settings key/value table with sensible defaults.</summary>
public class SettingsStore
{
    private readonly AppDbContext _db;
    public SettingsStore(AppDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key) =>
        (await _db.Settings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task<int> GetIntAsync(string key, int fallback)
    {
        var v = await GetAsync(key);
        return int.TryParse(v, out var n) ? n : fallback;
    }

    public async Task SetAsync(string key, string value)
    {
        var existing = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null) _db.Settings.Add(new Setting { Key = key, Value = value });
        else existing.Value = value;
        await _db.SaveChangesAsync();
    }

    public Task<int> EntryFeeAsync() => GetIntAsync(SettingKeys.EntryFee, 5);
    public Task<int> LivePollSecondsAsync() => GetIntAsync(SettingKeys.LivePollSeconds, 90);
    public Task<int> IdlePollSecondsAsync() => GetIntAsync(SettingKeys.IdlePollSeconds, 3600);
}
