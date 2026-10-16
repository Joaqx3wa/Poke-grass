using System.Windows.Threading;

namespace PokeGrass;

internal sealed class TimedItemEffects : IDisposable
{
    private readonly GameSaveStore _save;
    private readonly DispatcherTimer _timer;
    private int _repelSeconds;
    private string? _incenseId;
    private int _incenseSeconds;
    private int _persistTicks;

    internal event Action? Changed;
    internal event Action? RulesChanged;
    internal event Action<string>? Expired;
    internal int RepelSeconds => _repelSeconds;
    internal string? IncenseId => _incenseSeconds > 0 ? _incenseId : null;
    internal int IncenseSeconds => _incenseSeconds;
    internal bool HasRepel => _repelSeconds > 0;
    internal bool HasIncense => _incenseSeconds > 0 && _incenseId is not null;

    internal TimedItemEffects(GameSaveStore save)
    {
        _save = save;
        var state = save.GetTimedItems();
        _repelSeconds = Math.Max(0, state.RepelSeconds);
        _incenseId = state.IncenseId;
        _incenseSeconds = Math.Max(0, state.IncenseSeconds);
        if (_incenseSeconds == 0) _incenseId = null;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Tick;
        if (_repelSeconds > 0 || _incenseSeconds > 0) _timer.Start();
    }

    internal static bool IsRepel(string id) => id is "repel" or "super-repel" or "max-repel";
    internal static bool IsIncense(string id) => id.StartsWith("incense-", StringComparison.OrdinalIgnoreCase);
    internal static int RepelDuration(string id) => id switch { "repel" => 15 * 60, "super-repel" => 30 * 60, "max-repel" => 60 * 60, _ => 0 };
    internal static int IncenseDuration(string id) => id switch
    {
        "incense-full" => 45 * 60, "incense-lax" => 30 * 60, "incense-luck" => 10 * 60,
        "incense-odd" => 25 * 60, "incense-pure" => 10 * 60, "incense-rock" => 25 * 60,
        "incense-rose" => 25 * 60, "incense-sea" => 30 * 60, "incense-wave" => 10 * 60, _ => 0
    };

    internal bool ActivateRepel(string id)
    {
        var duration = RepelDuration(id);
        if (duration == 0 || !_save.TryConsume(id)) return false;
        _incenseId = null; _incenseSeconds = 0;
        _repelSeconds = checked(_repelSeconds + duration);
        Persist(); _timer.Start(); Changed?.Invoke(); RulesChanged?.Invoke(); return true;
    }

    internal bool ActivateIncense(string id)
    {
        var duration = IncenseDuration(id);
        if (duration == 0 || !_save.TryConsume(id)) return false;
        _repelSeconds = 0;
        if (string.Equals(_incenseId, id, StringComparison.OrdinalIgnoreCase) && _incenseSeconds > 0)
            _incenseSeconds = checked(_incenseSeconds + duration);
        else { _incenseId = id; _incenseSeconds = duration; }
        Persist(); _timer.Start(); Changed?.Invoke(); RulesChanged?.Invoke(); return true;
    }

    internal void CancelRepel() { _repelSeconds = 0; Persist(); Changed?.Invoke(); RulesChanged?.Invoke(); StopIfEmpty(); }
    internal void CancelIncense() { _incenseId = null; _incenseSeconds = 0; Persist(); Changed?.Invoke(); RulesChanged?.Invoke(); StopIfEmpty(); }

    internal int EncounterIntervalSeconds(Random random)
    {
        if (_incenseId is "incense-luck" or "incense-wave") return random.Next(60, 121);
        if (HasIncense) return random.Next(120, 241);
        return EncounterRules.NextIntervalSeconds(random);
    }

    internal int AlertSeconds => _incenseId == "incense-lax" && HasIncense ? 30 : 10;
    internal int ShinyDenominator => _incenseId == "incense-pure" && HasIncense ? 256 : 512;
    internal string? FavoredType => _incenseId switch { "incense-odd" => "Psychic", "incense-rock" => "Rock", "incense-rose" => "Grass", "incense-sea" or "incense-wave" => "Water", _ => null };

    internal void SaveForShutdown() => Persist();
    internal void StopForDataReload() => _timer.Stop();
    private void Tick(object? sender, EventArgs e)
    {
        var expired = new List<string>();
        if (_repelSeconds > 0 && --_repelSeconds == 0) expired.Add("repel");
        if (_incenseSeconds > 0 && --_incenseSeconds == 0) { expired.Add(_incenseId ?? "incense"); _incenseId = null; }
        if (expired.Count > 0 || ++_persistTicks >= 10) { _persistTicks = 0; Persist(); }
        Changed?.Invoke();
        if (expired.Count > 0) RulesChanged?.Invoke();
        foreach (var id in expired) Expired?.Invoke(id);
        StopIfEmpty();
    }
    private void Persist() => _save.SaveTimedItems(new TimedItemSave { RepelSeconds = _repelSeconds, IncenseId = _incenseId, IncenseSeconds = _incenseSeconds });
    private void StopIfEmpty() { if (_repelSeconds <= 0 && _incenseSeconds <= 0) _timer.Stop(); }
    public void Dispose() { _timer.Stop(); Persist(); }
}
