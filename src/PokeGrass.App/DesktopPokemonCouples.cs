using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace PokeGrass;

/// <summary>Runs a single lightweight social-pair event over the existing companion update loop.</summary>
internal sealed class DesktopPokemonCouples : IDisposable
{
    private enum Phase { Transforming, Approach, FirstHeart, SecondHeart, BothHearts, Walk, Dance, Finish }
    private sealed class Event(DesktopCompanionWindow first, DesktopCompanionWindow second, DateTime now)
    {
        internal DesktopCompanionWindow First { get; } = first;
        internal DesktopCompanionWindow Second { get; } = second;
        internal DateTime StartedAt { get; } = now;
        internal DateTime Until;
        internal DateTime NextAction;
        internal DateTime SegmentUntil;
        internal Point FirstTarget;
        internal Point SecondTarget;
        internal Point WalkDirection;
        internal Phase Phase;
        internal bool FirstJumps;
        internal bool AffinitySeek;
        internal DesktopCompanionWindow? TransformingDitto;
    }

    private readonly Func<IReadOnlyList<DesktopCompanionWindow>> _getCompanions;
    private readonly Func<bool> _hasIncompatibleEvent;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DateTime> _pairCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _individualCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _affinityUntil = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _nextAffinityCheck = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nearPairs = new(StringComparer.Ordinal);
    private Event? _active;
    private bool _disposed;

    internal bool IsCouplesActive => _active is not null;

    internal bool HasRecentCouplesAffinity(DesktopCompanionWindow first, DesktopCompanionWindow second)
    {
        var key = PairKey(first.RecordId, second.RecordId);
        return _affinityUntil.TryGetValue(key, out var until) && DateTime.UtcNow < until;
    }

    internal DesktopPokemonCouples(Func<IReadOnlyList<DesktopCompanionWindow>> getCompanions,
        Func<bool> hasIncompatibleEvent)
    {
        _getCompanions = getCompanions;
        _hasIncompatibleEvent = hasIncompatibleEvent;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if (_disposed) return;
        if (_active is { } current)
        {
            if ((current.Phase != Phase.Transforming && (!IsAvailable(current.First) || !IsAvailable(current.Second)))
                || (current.Phase == Phase.Transforming && (!current.First.IsVisible || !current.Second.IsVisible))
                || current.First.IsBeingDragged || current.Second.IsBeingDragged
                || _hasIncompatibleEvent())
            {
                Finish(cancelled: true);
                return;
            }
            TickEvent(current, DateTime.UtcNow);
            return;
        }

        if (_hasIncompatibleEvent()) return;
        var companions = _getCompanions().Where(item => item.CanBeginCouples).OrderBy(item => item.RecordId, StringComparer.Ordinal).ToArray();
        var now = DateTime.UtcNow;
        var currentPairs = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < companions.Length; i++)
        for (var j = i + 1; j < companions.Length; j++)
        {
            var first = companions[i];
            var second = companions[j];
            if (!Compatible(first, second)) continue;
            var key = PairKey(first.RecordId, second.RecordId);
            if (!IsNear(first, second))
            {
                if (TryStartAffinitySeek(first, second, key, now)) break;
                continue;
            }
            currentPairs.Add(key);
            if (_nearPairs.Contains(key)) continue;
            TryStartEncounter(first, second, key, now);
        }
        _nearPairs.Clear();
        foreach (var key in currentPairs) _nearPairs.Add(key);
        foreach (var key in _pairCooldowns.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToArray()) _pairCooldowns.Remove(key);
        foreach (var key in _individualCooldowns.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToArray()) _individualCooldowns.Remove(key);
    }

    internal bool TryStartFromCollision(DesktopCompanionWindow first, DesktopCompanionWindow second)
    {
        if (_disposed || _active is not null || _hasIncompatibleEvent()
            || !first.CanBeginCouples || !second.CanBeginCouples || !Compatible(first, second)
            || !IsNear(first, second)) return false;
        return TryStartEncounter(first, second, PairKey(first.RecordId, second.RecordId), DateTime.UtcNow);
    }

    private bool TryStartEncounter(DesktopCompanionWindow first, DesktopCompanionWindow second, string key, DateTime now)
    {
        if (!_nearPairs.Add(key)) return false;
        if (_active is not null || HasCooldown(key, first, second, now)
            || Random.Shared.Next(100) >= (_affinityUntil.TryGetValue(key, out var affinity) && now < affinity ? 30 : 15)) return false;
        Start(first, second, now);
        return _active is not null;
    }

    private bool TryStartAffinitySeek(DesktopCompanionWindow first, DesktopCompanionWindow second, string key, DateTime now)
    {
        if (_active is not null || !_affinityUntil.TryGetValue(key, out var affinityUntil) || now >= affinityUntil
            || HasCooldown(key, first, second, now)
            || (_nextAffinityCheck.TryGetValue(key, out var nextCheck) && now < nextCheck)) return false;
        _nextAffinityCheck[key] = now.AddSeconds(35);
        var a = first.VisibleBodyScreenBounds;
        var b = second.VisibleBodyScreenBounds;
        if (a.IsEmpty || b.IsEmpty) return false;
        var distance = (Center(a) - Center(b)).Length;
        if (distance < 190d || distance > 460d || Random.Shared.Next(100) >= 16) return false;
        if (first.IsPinned && second.IsPinned) return false;

        var chaser = first.IsPinned ? second : second.IsPinned ? first : Random.Shared.Next(2) == 0 ? first : second;
        var waiting = ReferenceEquals(chaser, first) ? second : first;
        var chaserBody = chaser.VisibleBodyScreenBounds;
        var waitingBody = waiting.VisibleBodyScreenBounds;
        var chaserCenter = Center(chaserBody);
        var waitingCenter = Center(waitingBody);
        var side = chaserCenter.X <= waitingCenter.X ? -1d : 1d;
        var gap = (chaserBody.Width + waitingBody.Width) / 2d + 12d;
        var target = new Point(waitingCenter.X + side * gap, waitingCenter.Y);
        var ev = new Event(chaser, waiting, now)
        {
            Phase = Phase.Approach,
            Until = now.AddSeconds(4),
            FirstTarget = target,
            SecondTarget = waitingCenter,
            AffinitySeek = true
        };
        _active = ev;
        var cooldownUntil = now.AddMinutes(3);
        _pairCooldowns[key] = cooldownUntil;
        _individualCooldowns[first.RecordId] = cooldownUntil;
        _individualCooldowns[second.RecordId] = cooldownUntil;
        if (!StartDittoTransformIfNeeded(ev)) BeginParticipants(ev);
        chaser.FaceCouplesPartner(waiting.GetBattleBodyCenter());
        waiting.FaceCouplesPartner(chaser.GetBattleBodyCenter());
        return true;
    }

    private void Start(DesktopCompanionWindow first, DesktopCompanionWindow second, DateTime now)
    {
        var a = first.VisibleBodyScreenBounds;
        var b = second.VisibleBodyScreenBounds;
        if (a.IsEmpty || b.IsEmpty || !Compatible(first, second)) return;
        var centerA = Center(a);
        var centerB = Center(b);
        var mid = new Point((centerA.X + centerB.X) / 2d, (centerA.Y + centerB.Y) / 2d);
        var visualScale = DesktopVisualScaleService.Current;
        var gap = (a.Width + b.Width) / 2d + Math.Max(12d * visualScale, Math.Min(a.Height, b.Height) * .12d);
        var firstLeft = centerA.X <= centerB.X;
        var firstOffset = firstLeft ? -gap / 2d : gap / 2d;
        var secondOffset = -firstOffset;
        var ev = new Event(first, second, now)
        {
            Phase = Phase.Approach,
            Until = now.AddSeconds(4),
            FirstTarget = new Point(mid.X + firstOffset, mid.Y),
            SecondTarget = new Point(mid.X + secondOffset, mid.Y)
        };
        _active = ev;
        var until = now.AddMinutes(3);
        _pairCooldowns[PairKey(first.RecordId, second.RecordId)] = until;
        _individualCooldowns[first.RecordId] = until;
        _individualCooldowns[second.RecordId] = until;
        if (!StartDittoTransformIfNeeded(ev)) BeginParticipants(ev);
    }

    private static bool StartDittoTransformIfNeeded(Event ev)
    {
        if (ev.First.IsDitto && !ev.Second.IsDitto && ev.First.BeginDittoCouplesTransform(ev.Second))
        {
            ev.TransformingDitto = ev.First;
            ev.Phase = Phase.Transforming;
            ev.Until = DateTime.UtcNow.AddSeconds(8);
            return true;
        }
        if (ev.Second.IsDitto && !ev.First.IsDitto && ev.Second.BeginDittoCouplesTransform(ev.First))
        {
            ev.TransformingDitto = ev.Second;
            ev.Phase = Phase.Transforming;
            ev.Until = DateTime.UtcNow.AddSeconds(8);
            return true;
        }
        return false;
    }

    private static void BeginParticipants(Event ev)
    {
        ev.First.BeginCouples(ev.Second.RecordId, CompanionEmotion.Happy);
        ev.Second.BeginCouples(ev.First.RecordId, CompanionEmotion.Happy);
        FaceEachOther(ev);
    }

    private void TickEvent(Event ev, DateTime now)
    {
        var elapsed = Math.Clamp((now - ev.StartedAt).TotalSeconds, .01d, .2d);
        switch (ev.Phase)
        {
            case Phase.Transforming:
                if (ev.TransformingDitto is { IsDittoTransforming: false } ditto)
                {
                    BeginParticipants(ev);
                    ev.Phase = Phase.Approach;
                    ev.Until = now.AddSeconds(4);
                }
                else if (now >= ev.Until) Finish(cancelled: true);
                break;
            case Phase.Approach:
                var firstArrived = ev.First.MoveForCouples(ev.FirstTarget, elapsed);
                var secondArrived = ev.Second.MoveForCouples(ev.SecondTarget, elapsed);
                if ((firstArrived && secondArrived) || now >= ev.Until) StartInteraction(ev, now);
                break;
            case Phase.FirstHeart:
            case Phase.SecondHeart:
                if (now >= ev.Until) StartNextHeart(ev, now);
                break;
            case Phase.BothHearts:
                if (now >= ev.Until) Finish(cancelled: false);
                break;
            case Phase.Walk:
                if (now >= ev.Until)
                {
                    FaceEachOther(ev);
                    EndWithHearts(ev, now);
                    break;
                }
                if (now >= ev.SegmentUntil && !TryChooseWalkLeg(ev, now))
                {
                    FaceEachOther(ev);
                    EndWithHearts(ev, now);
                    break;
                }
                var sharedSpeed = Math.Min(ev.First.CouplesWalkSpeed, ev.Second.CouplesWalkSpeed);
                ev.First.WalkForCouples(ev.WalkDirection, elapsed, sharedSpeed);
                ev.Second.WalkForCouples(ev.WalkDirection, elapsed, sharedSpeed);
                break;
            case Phase.Dance:
                if (now >= ev.NextAction)
                {
                    var jumper = ev.FirstJumps ? ev.First : ev.Second;
                    jumper.SetCouplesJump(.13d);
                    jumper.PlayCouplesCry();
                    ev.FirstJumps = !ev.FirstJumps;
                    ev.NextAction = now.AddMilliseconds(650);
                    ev.Until = ev.Until.AddMilliseconds(-650);
                    if (ev.Until <= now) EndWithHearts(ev, now);
                }
                break;
            case Phase.Finish:
                if (now >= ev.Until) Finish(cancelled: false);
                break;
        }
    }

    private static void StartInteraction(Event ev, DateTime now)
    {
        FaceEachOther(ev);
        if (ev.AffinitySeek)
        {
            EndWithHearts(ev, now);
            ev.Until = now.AddSeconds(2.5);
            return;
        }
        var interaction = ev.First.IsPinned || ev.Second.IsPinned ? Random.Shared.Next(2) * 2 : Random.Shared.Next(3);
        if (interaction == 1 && !TryBeginWalk(ev, now)) interaction = 2;
        switch (interaction)
        {
            case 0:
                ev.Phase = Phase.FirstHeart;
                ev.Until = now.AddMilliseconds(1000);
                ev.First.SetCouplesReaction(CompanionEmotion.Heart, TimeSpan.FromMilliseconds(1100));
                break;
            case 1:
                break;
            default:
                ev.Phase = Phase.Dance;
                ev.FirstJumps = true;
                ev.Until = now.AddSeconds(4);
                ev.NextAction = now.AddMilliseconds(350);
                break;
        }
    }

    private static bool TryBeginWalk(Event ev, DateTime now)
    {
        if (!TryChooseWalkLeg(ev, now)) return false;
        ev.Phase = Phase.Walk;
        ev.Until = now.AddSeconds(40);
        return true;
    }

    private static bool TryChooseWalkLeg(Event ev, DateTime now)
    {
        var firstBody = ev.First.VisibleBodyScreenBounds;
        var secondBody = ev.Second.VisibleBodyScreenBounds;
        if (firstBody.IsEmpty || secondBody.IsEmpty
            || CompanionCollisionGeometry.Overlaps(firstBody, secondBody)) return false;
        var group = Rect.Union(firstBody, secondBody);
        var center = Center(group);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(center.X), (int)Math.Round(center.Y)));
        var firstCenter = Center(firstBody);
        var secondCenter = Center(secondBody);
        var firstScreen = Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(firstCenter.X), (int)Math.Round(firstCenter.Y)));
        var secondScreen = Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(secondCenter.X), (int)Math.Round(secondCenter.Y)));
        if (!string.Equals(firstScreen.DeviceName, secondScreen.DeviceName, StringComparison.OrdinalIgnoreCase)) return false;

        var area = screen.WorkingArea;
        var margin = Math.Max(24d * DesktopVisualScaleService.Current, Math.Max(firstBody.Height, secondBody.Height) * .3d);
        var speed = Math.Max(1d, Math.Min(ev.First.CouplesWalkSpeed, ev.Second.CouplesWalkSpeed));
        var options = new[] { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) }
            .OrderBy(direction =>
            {
                var dot = direction.X * ev.WalkDirection.X + direction.Y * ev.WalkDirection.Y;
                return dot == 0 ? 0 : dot < 0 ? 1 : 2;
            })
            .ThenBy(_ => Random.Shared.Next()).ToArray();
        foreach (var direction in options)
        {
            var available = direction.X > 0 ? area.Right - margin - group.Right
                : direction.X < 0 ? group.Left - (area.Left + margin)
                : direction.Y > 0 ? area.Bottom - margin - group.Bottom
                : group.Top - (area.Top + margin);
            var maxSeconds = available / speed;
            if (maxSeconds < .75d) continue;
            var legSeconds = Math.Min(1.5d + Random.Shared.NextDouble() * 2d, maxSeconds);
            ev.WalkDirection = direction;
            ev.SegmentUntil = now.AddSeconds(legSeconds);
            return true;
        }
        return false;
    }

    private static void StartNextHeart(Event ev, DateTime now)
    {
        if (ev.Phase == Phase.FirstHeart)
        {
            ev.Phase = Phase.SecondHeart;
            ev.Second.SetCouplesReaction(CompanionEmotion.Heart, TimeSpan.FromMilliseconds(1100));
            ev.Until = now.AddMilliseconds(1000);
            return;
        }
        EndWithHearts(ev, now);
    }

    private static void EndWithHearts(Event ev, DateTime now)
    {
        FaceEachOther(ev);
        ev.First.SetCouplesJump(.12d);
        ev.Second.SetCouplesJump(.12d);
        ev.First.SetCouplesReaction(CompanionEmotion.Heart, TimeSpan.FromSeconds(1.5));
        ev.Second.SetCouplesReaction(CompanionEmotion.Heart, TimeSpan.FromSeconds(1.5));
        ev.Phase = Phase.BothHearts;
        ev.Until = now.AddSeconds(1.5);
    }

    private static void FaceEachOther(Event ev)
    {
        ev.First.FaceCouplesPartner(ev.Second.GetBattleBodyCenter());
        ev.Second.FaceCouplesPartner(ev.First.GetBattleBodyCenter());
    }

    internal void CancelFor(DesktopCompanionWindow companion)
    {
        if (_active is { } ev && (ev.First.RecordId == companion.RecordId || ev.Second.RecordId == companion.RecordId))
            Finish(cancelled: true);
    }

    internal void CancelForRecord(string recordId)
    {
        if (_active is { } ev && (ev.First.RecordId == recordId || ev.Second.RecordId == recordId))
            Finish(cancelled: true);
    }

    internal void CancelActive() { if (_active is not null) Finish(cancelled: true); }

    private void Finish(bool cancelled)
    {
        var ev = _active;
        if (ev is null) return;
        _active = null;
        if (cancelled && ev.TransformingDitto is { IsDittoTransforming: true } transformingDitto)
            transformingDitto.CancelDittoTransitionAndRestore();
        if (!cancelled)
        {
            var key = PairKey(ev.First.RecordId, ev.Second.RecordId);
            _affinityUntil[key] = DateTime.UtcNow.AddMinutes(30);
            var positiveProtectionUntil = DateTime.UtcNow.AddSeconds(60);
            ev.First.ProtectFromNegativeInteractionWith(ev.Second, positiveProtectionUntil);
            ev.First.SetCouplesReaction(CompanionEmotion.Happy, TimeSpan.FromMilliseconds(800));
            ev.Second.SetCouplesReaction(CompanionEmotion.Happy, TimeSpan.FromMilliseconds(800));
        }
        var returnDelay = cancelled ? TimeSpan.Zero : TimeSpan.FromMilliseconds(900);
        ev.First.ScheduleDittoReturn("couples", returnDelay);
        ev.Second.ScheduleDittoReturn("couples", returnDelay);
        ev.First.EndCouples(cancelled);
        ev.Second.EndCouples(cancelled);
    }

    private bool HasCooldown(string key, DesktopCompanionWindow first, DesktopCompanionWindow second, DateTime now) =>
        (_pairCooldowns.TryGetValue(key, out var pairUntil) && now < pairUntil)
        || (_individualCooldowns.TryGetValue(first.RecordId, out var firstUntil) && now < firstUntil)
        || (_individualCooldowns.TryGetValue(second.RecordId, out var secondUntil) && now < secondUntil);

    private static bool Compatible(DesktopCompanionWindow first, DesktopCompanionWindow second) =>
        first.IsDitto || second.IsDitto || CouplesCompatibilityPolicy.AreCompatible(first.CouplesSpeciesId, first.CouplesSex,
            second.CouplesSpeciesId, second.CouplesSex);

    private static bool IsAvailable(DesktopCompanionWindow companion) => companion.CanContinueCouples;

    private static bool IsNear(DesktopCompanionWindow first, DesktopCompanionWindow second)
    {
        var a = first.VisibleBodyScreenBounds;
        var b = second.VisibleBodyScreenBounds;
        if (a.IsEmpty || b.IsEmpty) return false;
        var range = Math.Max(100d * DesktopVisualScaleService.Current, Math.Max(a.Width + b.Width, a.Height + b.Height) * .8d);
        return CompanionCollisionGeometry.IsNear(a, b, range);
    }

    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2d, bounds.Y + bounds.Height / 2d);
    private static string PairKey(string first, string second) => string.CompareOrdinal(first, second) < 0
        ? first + "\u001f" + second : second + "\u001f" + first;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Finish(cancelled: true);
        _nearPairs.Clear();
        _pairCooldowns.Clear();
        _individualCooldowns.Clear();
        _affinityUntil.Clear();
        _nextAffinityCheck.Clear();
    }
}

internal static class CouplesCompatibilityPolicy
{
    private static readonly HashSet<string> SpecialSpeciesPairs =
    [
        PairKey("nidoran-m", "nidoran-f"),
        PairKey("nidorino", "nidorina"),
        PairKey("nidoking", "nidoqueen"),
        PairKey("tauros", "miltank"),
        PairKey("volbeat", "illumise"),
        PairKey("latios", "latias"),
        PairKey("mothim", "wormadam"),
        PairKey("rufflet", "vullaby"),
        PairKey("braviary", "mandibuzz"),
        PairKey("impidimp", "hatenna"),
        PairKey("morgrem", "hattrem"),
        PairKey("grimmsnarl", "hatterene")
    ];

    internal static bool AreCompatible(string firstSpecies, string firstSex, string secondSpecies, string secondSex)
    {
        if (string.IsNullOrWhiteSpace(firstSpecies) || string.IsNullOrWhiteSpace(secondSpecies)
            || !AreOppositeSexes(firstSex, secondSex)) return false;

        return firstSpecies.Equals(secondSpecies, StringComparison.OrdinalIgnoreCase)
            || SpecialSpeciesPairs.Contains(PairKey(firstSpecies, secondSpecies));
    }

    private static bool AreOppositeSexes(string firstSex, string secondSex) =>
        firstSex.Equals("male", StringComparison.OrdinalIgnoreCase)
            && secondSex.Equals("female", StringComparison.OrdinalIgnoreCase)
        || firstSex.Equals("female", StringComparison.OrdinalIgnoreCase)
            && secondSex.Equals("male", StringComparison.OrdinalIgnoreCase);

    private static string PairKey(string first, string second)
    {
        var normalizedFirst = first.Trim().ToLowerInvariant();
        var normalizedSecond = second.Trim().ToLowerInvariant();
        return string.CompareOrdinal(normalizedFirst, normalizedSecond) < 0
            ? $"{normalizedFirst}|{normalizedSecond}"
            : $"{normalizedSecond}|{normalizedFirst}";
    }
}
