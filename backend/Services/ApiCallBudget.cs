namespace Worldcup.Api.Services;

/// <summary>
/// Singleton hard cap on api-football requests per UTC day, protecting the free tier's
/// 100/day quota regardless of how often the poller ticks. Resets at UTC midnight.
/// </summary>
public class ApiCallBudget
{
    private readonly int _budget;
    private readonly object _lock = new();
    private int _dayNumber;
    private int _used;

    public ApiCallBudget(IConfiguration config)
    {
        _budget = int.TryParse(config["ApiFootball:DailyBudget"], out var b) ? b : 90;
    }

    public bool TryConsume()
    {
        lock (_lock)
        {
            Rollover();
            if (_used >= _budget) return false;
            _used++;
            return true;
        }
    }

    public bool Exhausted
    {
        get { lock (_lock) { Rollover(); return _used >= _budget; } }
    }

    public int Remaining
    {
        get { lock (_lock) { Rollover(); return Math.Max(0, _budget - _used); } }
    }

    private void Rollover()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        if (today != _dayNumber) { _dayNumber = today; _used = 0; }
    }
}
