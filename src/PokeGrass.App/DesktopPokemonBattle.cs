using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace PokeGrass;

/// <summary>Owns per-pair cooldowns and the one-at-a-time, temporary desktop battle.</summary>
internal sealed class DesktopPokemonBattle : IDisposable
{
    private sealed class ActiveBattle(DesktopCompanionWindow first, DesktopCompanionWindow second,
        DateTime startedAt, int maxTurns, DesktopCompanionWindow winner, BattleArenaWindow? arena)
    {
        internal DesktopCompanionWindow First { get; } = first;
        internal DesktopCompanionWindow Second { get; } = second;
        internal DateTime StartedAt { get; } = startedAt;
        internal int MaxTurns { get; } = maxTurns;
        internal DesktopCompanionWindow Winner { get; } = winner;
        internal int Turns;
        internal bool LastTurnWasDefense;
        internal bool FirstStartsFirst { get; } = Random.Shared.Next(2) == 0;
        internal DesktopCompanionWindow? CurrentAttacker;
        internal bool ShieldQueued;
        internal BattleEffectWindow? Effect;
        internal BattleArenaWindow? Arena { get; } = arena;
        internal string PairKey => MakePairKey(First.RecordId, Second.RecordId);
    }

    private enum Phase { Preparing, Transforming, Facing, Jumping, Shielding, Effect, Recovery }
    private readonly Func<IReadOnlyList<DesktopCompanionWindow>> _getCompanions;
    private readonly Func<bool> _hasIncompatibleEvent;
    private readonly Func<DesktopCompanionWindow, DesktopCompanionWindow, bool> _areCouplePartners;
    private readonly Action? _restoreOverlayZOrder;
    private readonly Func<System.Drawing.Rectangle> _getGardenBounds;
    private readonly Action<DesktopCompanionWindow> _winnerCelebrationCompleted;
    private readonly Dictionary<string, DateTime> _cooldowns = new(StringComparer.Ordinal);
    private readonly Random _random = new();
    private readonly Random _backgroundRandom = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _victoryTimer;
    private DesktopCompanionWindow? _pendingVictoryWinner;
    private ActiveBattle? _battle;
    private BattleArenaWindow? _endingArena;
    private Phase _phase;
    private DateTime _phaseUntil;
    private bool _disposed;

    internal bool IsBattleActive => _battle is not null;
    internal bool IsBattleEffectVisible => _battle?.Effect?.IsVisible == true;
    internal bool IsArenaVisible => (_battle?.Arena ?? _endingArena)?.IsVisible == true;

    internal System.Drawing.Rectangle GetArenaBounds() => (_battle?.Arena ?? _endingArena)?.GetPhysicalBounds()
        ?? System.Drawing.Rectangle.Empty;

    internal IntPtr GetArenaHandle() => (_battle?.Arena ?? _endingArena)?.GetWindowHandle() ?? IntPtr.Zero;

    internal IntPtr GetEffectHandle() => _battle?.Effect?.GetWindowHandle() ?? IntPtr.Zero;

#if DEBUG
    internal bool StartForDebug(DesktopCompanionWindow first, DesktopCompanionWindow second)
    {
        if (_disposed || _battle is not null || !first.CanBeginFriendlyBattle || !second.CanBeginFriendlyBattle) return false;
        Start(first, second, DateTime.UtcNow);
        return _battle is not null;
    }
#endif

    internal DesktopPokemonBattle(Func<IReadOnlyList<DesktopCompanionWindow>> getCompanions,
        Func<bool> hasIncompatibleEvent,
        Func<DesktopCompanionWindow, DesktopCompanionWindow, bool>? areCouplePartners = null,
        Action? restoreOverlayZOrder = null,
        Func<System.Drawing.Rectangle>? getGardenBounds = null,
        Action<DesktopCompanionWindow>? winnerCelebrationCompleted = null)
    {
        _getCompanions = getCompanions;
        _hasIncompatibleEvent = hasIncompatibleEvent;
        _areCouplePartners = areCouplePartners ?? ((_, _) => false);
        _restoreOverlayZOrder = restoreOverlayZOrder;
        _getGardenBounds = getGardenBounds ?? (() => System.Drawing.Rectangle.Empty);
        _winnerCelebrationCompleted = winnerCelebrationCompleted ?? (_ => { });
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Tick();
        // The winner celebrates for 2.8 seconds; the arena then fades for
        // 750 ms. Leave a small buffer so evolution prompts never overlap it.
        _victoryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(3700) };
        _victoryTimer.Tick += (_, _) =>
        {
            _victoryTimer.Stop();
            var winner = _pendingVictoryWinner;
            _pendingVictoryWinner = null;
            if (winner is { IsVisible: true, IsBattleParticipant: false, IsBattleCelebrating: false })
                _winnerCelebrationCompleted(winner);
        };
    }

    internal void ReportAnger(DesktopCompanionWindow angryPokemon, DesktopCompanionWindow otherPokemon)
    {
        if (_disposed || _battle is not null || angryPokemon.RecordId == otherPokemon.RecordId
            || !angryPokemon.CanBeginFriendlyBattle || !otherPokemon.CanBeginFriendlyBattle || _hasIncompatibleEvent()) return;

        var key = MakePairKey(angryPokemon.RecordId, otherPokemon.RecordId);
        var now = DateTime.UtcNow;
        if (_cooldowns.TryGetValue(key, out var cooldown) && now < cooldown) return;
        var battleChance = _areCouplePartners(angryPokemon, otherPokemon) ? 10 : 38;
        if (_random.Next(100) >= battleChance) return;
        if (!angryPokemon.CanBeginFriendlyBattle || !otherPokemon.CanBeginFriendlyBattle || _hasIncompatibleEvent()) return;
        Start(angryPokemon, otherPokemon, now);
    }

    private void Start(DesktopCompanionWindow first, DesktopCompanionWindow second, DateTime now)
    {
        var firstBody = first.VisibleBodyScreenBounds;
        var secondBody = second.VisibleBodyScreenBounds;
        if (firstBody.IsEmpty || secondBody.IsEmpty) return;

        var firstCenter = new Point(firstBody.X + firstBody.Width / 2d, firstBody.Y + firstBody.Height / 2d);
        var secondCenter = new Point(secondBody.X + secondBody.Width / 2d, secondBody.Y + secondBody.Height / 2d);
        var firstIsLeft = firstCenter.X < secondCenter.X
            || (Math.Abs(firstCenter.X - secondCenter.X) < .5d && string.CompareOrdinal(first.RecordId, second.RecordId) < 0);
        var left = firstIsLeft ? first : second;
        var right = firstIsLeft ? second : first;
        var leftBody = firstIsLeft ? firstBody : secondBody;
        var rightBody = firstIsLeft ? secondBody : firstBody;
        var leftCenter = firstIsLeft ? firstCenter : secondCenter;
        var rightCenter = firstIsLeft ? secondCenter : firstCenter;

        var monitorPoint = new System.Drawing.Point((int)((leftCenter.X + rightCenter.X) / 2d),
            (int)((leftCenter.Y + rightCenter.Y) / 2d));
        var work = Forms.Screen.FromPoint(monitorPoint).WorkingArea;
        var visualScale = DesktopVisualScaleService.Current;
        var stage = DesktopEventStage.FindClearCenter(new Point((leftCenter.X + rightCenter.X) / 2d,
                (leftCenter.Y + rightCenter.Y) / 2d),
            Math.Max(440d * visualScale, leftBody.Width + rightBody.Width + 80d * visualScale),
            Math.Max(300d * visualScale, Math.Max(leftBody.Height, rightBody.Height) + 100d * visualScale),
            _getGardenBounds(), work);
        if (stage is { } stageCenter)
        {
            var dx = stageCenter.X - (leftCenter.X + rightCenter.X) / 2d;
            var dy = stageCenter.Y - (leftCenter.Y + rightCenter.Y) / 2d;
            leftCenter = new Point(leftCenter.X + dx, leftCenter.Y + dy);
            rightCenter = new Point(rightCenter.X + dx, rightCenter.Y + dy);
        }
        var separation = Math.Max(64d * visualScale, (leftBody.Width + rightBody.Width) / 2d + 42d * visualScale);
        var middleX = Math.Clamp((leftCenter.X + rightCenter.X) / 2d,
            work.Left + separation / 2d, work.Right - separation / 2d);
        var avgY = (leftCenter.Y + rightCenter.Y) / 2d;
        var leftTargetX = middleX - separation / 2d;
        var rightTargetX = middleX + separation / 2d;
        var leftTargetY = Math.Clamp(avgY, work.Top + leftBody.Height / 2d, work.Bottom - leftBody.Height / 2d);
        var rightTargetY = Math.Clamp(avgY, work.Top + rightBody.Height / 2d, work.Bottom - rightBody.Height / 2d);

        _endingArena?.CloseImmediately();
        _endingArena = null;
        BattleArenaWindow? arena = null;
        var firstBackgroundProfile = new BattleBackgroundProfile(first.BattleFormId, first.BattleSpeciesId,
            first.BattleTypes, first.IsMegaEvolved);
        var secondBackgroundProfile = new BattleBackgroundProfile(second.BattleFormId, second.BattleSpeciesId,
            second.BattleTypes, second.IsMegaEvolved);
        var background = BattleBackgroundSelector.Select(firstBackgroundProfile, secondBackgroundProfile,
            _backgroundRandom.NextDouble, BattleArenaWindow.IsBackgroundAvailable);
        EncounterDiagnostics.Write("battle-background",
            $"pokemonA={firstBackgroundProfile.PokemonId} speciesA={firstBackgroundProfile.SpeciesId} typesA={string.Join('/', firstBackgroundProfile.Types)} megaA={firstBackgroundProfile.IsMega}; "
            + $"pokemonB={secondBackgroundProfile.PokemonId} speciesB={secondBackgroundProfile.SpeciesId} typesB={string.Join('/', secondBackgroundProfile.Types)} megaB={secondBackgroundProfile.IsMega}; "
            + $"category={background.Category} background={background.BackgroundId ?? "none"}; reason={background.Reason}");
        if (background.BackgroundId is { } backgroundId)
        {
            try
            {
                arena = new BattleArenaWindow(backgroundId,
                    new Point(middleX, avgY), OnArenaClosed);
                var arenaBounds = arena.GetPhysicalBounds();
                if (!arenaBounds.IsEmpty)
                    (leftTargetX, leftTargetY, rightTargetX, rightTargetY) = GetArenaBattlePositions(
                        arenaBounds, work, leftBody, rightBody);
            }
            catch (Exception exception)
            {
                EncounterDiagnostics.WriteException($"desktop-battle-arena-{backgroundId}", exception);
            }
        }

        var winner = _random.Next(2) == 0 ? first : second;
        _battle = new ActiveBattle(first, second, now, _random.Next(5, 8), winner, arena);
        _cooldowns[_battle.PairKey] = now.AddMinutes(2);
        first.PlayBattleStartCry();
        second.PlayBattleStartCry();
        left.BeginFriendlyBattle(right.RecordId, leftTargetX, leftTargetY, faceRight: true);
        right.BeginFriendlyBattle(left.RecordId, rightTargetX, rightTargetY, faceRight: false);
        _restoreOverlayZOrder?.Invoke();
        _phase = Phase.Preparing;
        _phaseUntil = now.AddMilliseconds(40);
        _timer.Start();
    }

    private static (double LeftX, double LeftY, double RightX, double RightY) GetArenaBattlePositions(
        System.Drawing.Rectangle arena, System.Drawing.Rectangle work,
        Rect leftBody, Rect rightBody)
    {
        // Keep each complete visible body inside its half of the court. Large
        // forms used to push the nominal side-circle center past the field edge.
        // This only changes positions; sprite and arena scales remain untouched.
        const double sideCircleCenter = .285d;
        const double courtBaseline = .525d;
        var centerX = arena.Left + arena.Width / 2d;
        var sideInset = arena.Width * .10d;
        var minimumCenterGap = Math.Max(12d * DesktopVisualScaleService.Current,
            Math.Min(28d * DesktopVisualScaleService.Current, arena.Width * .06d));
        var leftMin = arena.Left + sideInset + leftBody.Width / 2d;
        var leftMax = centerX - minimumCenterGap / 2d - leftBody.Width / 2d;
        var rightMin = centerX + minimumCenterGap / 2d + rightBody.Width / 2d;
        var rightMax = arena.Right - sideInset - rightBody.Width / 2d;
        var leftIdeal = arena.Left + arena.Width * sideCircleCenter;
        var rightIdeal = arena.Right - arena.Width * sideCircleCenter;
        var leftX = leftMin <= leftMax ? Math.Clamp(leftIdeal, leftMin, leftMax) : centerX - leftBody.Width / 2d;
        var rightX = rightMin <= rightMax ? Math.Clamp(rightIdeal, rightMin, rightMax) : centerX + rightBody.Width / 2d;
        var feetY = arena.Top + arena.Height * courtBaseline;
        var leftY = feetY - leftBody.Height / 2d;
        var rightY = feetY - rightBody.Height / 2d;

        leftX = ClampBodyCenter(leftX, leftBody.Width, arena.Left + sideInset, arena.Right - sideInset);
        rightX = ClampBodyCenter(rightX, rightBody.Width, arena.Left + sideInset, arena.Right - sideInset);
        var verticalInset = arena.Height * .10d;
        leftY = ClampBodyCenter(leftY, leftBody.Height, arena.Top + verticalInset, arena.Bottom - verticalInset);
        rightY = ClampBodyCenter(rightY, rightBody.Height, arena.Top + verticalInset, arena.Bottom - verticalInset);
        leftY = ClampBodyCenter(leftY, leftBody.Height, work.Top, work.Bottom);
        rightY = ClampBodyCenter(rightY, rightBody.Height, work.Top, work.Bottom);
        return (leftX, leftY, rightX, rightY);
    }

    private static double ClampBodyCenter(double center, double size, double min, double max)
    {
        var halfSize = Math.Max(0d, size / 2d);
        return max - min <= size ? (min + max) / 2d : Math.Clamp(center, min + halfSize, max - halfSize);
    }

    private void Tick()
    {
        if (_battle is null) { _timer.Stop(); return; }
        if (!IsAvailable(_battle.First) || !IsAvailable(_battle.Second))
        {
            Finish(cancelled: true);
            return;
        }

        var now = DateTime.UtcNow;
        KeepBystandersClear(_battle, now);
        if (now < _phaseUntil) return;
        switch (_phase)
        {
            case Phase.Preparing:
                if (_battle.First.IsAtFriendlyBattlePosition && _battle.Second.IsAtFriendlyBattlePosition)
                {
                    var didTransform = false;
                    if (_battle.First.IsDitto && !_battle.Second.IsDitto)
                        didTransform = _battle.First.BeginDittoBattleTransform(_battle.Second);
                    else if (_battle.Second.IsDitto && !_battle.First.IsDitto)
                        didTransform = _battle.Second.BeginDittoBattleTransform(_battle.First);
                    if (didTransform)
                    {
                        _phase = Phase.Transforming;
                        _phaseUntil = now.AddMilliseconds(40);
                    }
                    else BeginFacingStandoff(now);
                }
                else if (now - _battle.StartedAt >= TimeSpan.FromSeconds(30))
                {
                    EncounterDiagnostics.Write("desktop-battle", "Cancelled because both Pokémon could not reach their assigned positions.");
                    Finish(cancelled: true);
                }
                else
                {
                    _phaseUntil = now.AddMilliseconds(40);
                }
                break;
            case Phase.Transforming:
                if (!_battle.First.IsDittoTransforming && !_battle.Second.IsDittoTransforming)
                    BeginFacingStandoff(now);
                else _phaseUntil = now.AddMilliseconds(40);
                break;
            case Phase.Facing:
                StartNextTurn(now);
                break;
            case Phase.Jumping:
                BeginAttackEffect(now);
                break;
            case Phase.Effect:
                // The effect window closes itself; this is only a safety net for lost WPF close callbacks.
                if (_battle.Effect is null) BeginRecovery(now);
                else if (now - _battle.StartedAt > TimeSpan.FromSeconds(40)) Finish(cancelled: false);
                break;
            case Phase.Recovery:
                StartNextTurn(now);
                break;
        }
    }

    private void KeepBystandersClear(ActiveBattle battle, DateTime now)
    {
        var arenaBounds = battle.Arena?.GetPhysicalBounds() ?? System.Drawing.Rectangle.Empty;
        Point center;
        double safeRadius;
        if (!arenaBounds.IsEmpty)
        {
            center = new Point(arenaBounds.Left + arenaBounds.Width / 2d, arenaBounds.Top + arenaBounds.Height / 2d);
            safeRadius = Math.Max(300d * DesktopVisualScaleService.Current, Math.Max(arenaBounds.Width, arenaBounds.Height) * .78d);
        }
        else
        {
            var first = battle.First.VisibleBodyScreenBounds;
            var second = battle.Second.VisibleBodyScreenBounds;
            center = new Point((first.X + first.Width / 2d + second.X + second.Width / 2d) / 2d,
                (first.Y + first.Height / 2d + second.Y + second.Height / 2d) / 2d);
            safeRadius = Math.Max(300d * DesktopVisualScaleService.Current, (first.Width + second.Width) * .85d);
        }

        foreach (var companion in _getCompanions())
        {
            if (companion.RecordId == battle.First.RecordId || companion.RecordId == battle.Second.RecordId)
                continue;
            companion.KeepAwayFromBattle(center, safeRadius, now);
        }
    }

    private void BeginFacingStandoff(DateTime now)
    {
        var battle = _battle;
        if (battle is null) return;
        battle.First.StopFriendlyBattlePreparation();
        battle.Second.StopFriendlyBattlePreparation();
        battle.First.FaceBattleOpponent(battle.Second.GetBattleBodyCenter());
        battle.Second.FaceBattleOpponent(battle.First.GetBattleBodyCenter());
        _phase = Phase.Facing;
        _phaseUntil = now.AddMilliseconds(700);
    }

    private void StartNextTurn(DateTime now)
    {
        var battle = _battle;
        if (battle is null) return;
        if (battle.Turns >= battle.MaxTurns) { Finish(cancelled: false); return; }

        battle.First.StopFriendlyBattlePreparation();
        battle.Second.StopFriendlyBattlePreparation();
        var isFinalAttack = battle.Turns == battle.MaxTurns - 1;
        var firstAttacks = battle.Turns % 2 == 0 ? battle.FirstStartsFirst : !battle.FirstStartsFirst;
        var attacker = isFinalAttack ? battle.Winner : firstAttacks ? battle.First : battle.Second;
        battle.Turns++;
        var defense = !isFinalAttack && !battle.LastTurnWasDefense && _random.Next(100) < 18;
        battle.LastTurnWasDefense = defense;
        battle.CurrentAttacker = attacker;
        battle.ShieldQueued = defense;
        var target = ReferenceEquals(attacker, battle.First) ? battle.Second : battle.First;
        attacker.FaceBattleOpponent(target.GetBattleBodyCenter());
        attacker.SetBattleJump(.14d);
        _phase = Phase.Jumping;
        _phaseUntil = now.AddMilliseconds(_random.Next(340, 461));
    }

    private void BeginAttackEffect(DateTime now)
    {
        var battle = _battle;
        if (battle is null || battle.CurrentAttacker is not { } attacker) return;
        var target = ReferenceEquals(attacker, battle.First) ? battle.Second : battle.First;
        attacker.SetBattleJump(0d);
        if (battle.ShieldQueued)
        {
            battle.ShieldQueued = false;
            try
            {
                ShowEffect(BattleEffectCatalog.All.First(item => item.Id == "shield"), null, target);
                _phase = Phase.Shielding;
                _phaseUntil = now.AddSeconds(2);
            }
            catch (Exception exception)
            {
                EncounterDiagnostics.WriteException("desktop-battle-shield", exception);
                BeginRecovery(now);
            }
            return;
        }
        BeginAttackAnimation(attacker, target, now);
    }

    private void BeginAttackAnimation(DesktopCompanionWindow attacker, DesktopCompanionWindow target, DateTime now)
    {
        target.SetBattleHitReaction(TimeSpan.FromMilliseconds(850));
        try
        {
            ShowEffect(BattleEffectCatalog.PickAttack(attacker, _random), attacker, target);
            _phase = Phase.Effect;
            _phaseUntil = now.AddSeconds(2);
        }
        catch (Exception exception)
        {
            EncounterDiagnostics.WriteException("desktop-battle-attack", exception);
            BeginRecovery(now);
        }
    }

    private void ShowEffect(BattleEffectDefinition definition, DesktopCompanionWindow? attacker,
        DesktopCompanionWindow target)
    {
        var body = target.VisibleBodyScreenBounds;
        if (body.IsEmpty) { BeginRecovery(DateTime.UtcNow); return; }
        var effect = new BattleEffectWindow(definition, attacker, target, OnEffectEnded);
        _battle!.Effect = effect;
        effect.Show();
    }

    private void OnEffectEnded(BattleEffectWindow effect)
    {
        if (_battle is null || !ReferenceEquals(_battle.Effect, effect)) return;
        _battle.Effect = null;
        if (_phase == Phase.Shielding && _battle.CurrentAttacker is { } attacker)
        {
            var target = ReferenceEquals(attacker, _battle.First) ? _battle.Second : _battle.First;
            BeginAttackAnimation(attacker, target, DateTime.UtcNow);
            return;
        }
        BeginRecovery(DateTime.UtcNow);
    }

    private void BeginRecovery(DateTime now)
    {
        if (_battle is null) return;
        _battle.Effect = null;
        _phase = Phase.Recovery;
        _phaseUntil = now.AddMilliseconds(_random.Next(520, 801));
    }

    internal void CancelFor(DesktopCompanionWindow companion)
    {
        if (_battle is null || (_battle.First.RecordId != companion.RecordId && _battle.Second.RecordId != companion.RecordId)) return;
        Finish(cancelled: true);
    }

    internal void CancelForRecord(string recordId)
    {
        if (_battle is null || (_battle.First.RecordId != recordId && _battle.Second.RecordId != recordId)) return;
        Finish(cancelled: true);
    }

    internal void CancelActiveBattle()
    {
        if (_battle is not null) Finish(cancelled: true);
    }

    private void Finish(bool cancelled)
    {
        var battle = _battle;
        if (battle is null) return;
        _battle = null;
        _timer.Stop();
        battle.Effect?.Close();
        var firstWon = !cancelled && ReferenceEquals(battle.Winner, battle.First);
        var firstCenter = battle.First.GetBattleBodyCenter();
        var secondCenter = battle.Second.GetBattleBodyCenter();
        battle.First.FinishFriendlyBattle(firstWon,
            secondCenter, cancelled);
        battle.Second.FinishFriendlyBattle(!cancelled && !firstWon,
            firstCenter, cancelled);
        foreach (var companion in _getCompanions()) companion.EndBattleEvacuation();
        if (battle.Arena is { } arena)
        {
            _endingArena = arena;
            if (cancelled) arena.FadeOutNow();
            else arena.FadeOutWhen(() => (firstWon ? battle.First : battle.Second).IsBattleCelebrating);
        }
        if (!cancelled)
        {
            _pendingVictoryWinner = firstWon ? battle.First : battle.Second;
            _victoryTimer.Stop();
            _victoryTimer.Start();
        }
    }

    private void OnArenaClosed(BattleArenaWindow arena)
    {
        if (ReferenceEquals(_endingArena, arena)) _endingArena = null;
    }

    private static bool IsAvailable(DesktopCompanionWindow companion) => companion.IsVisible && companion.IsBattleParticipant;

    private static string MakePairKey(string first, string second) => string.CompareOrdinal(first, second) < 0
        ? first + "\u001f" + second : second + "\u001f" + first;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Finish(cancelled: true);
        _endingArena?.CloseImmediately();
        _endingArena = null;
        _timer.Stop();
        _victoryTimer.Stop();
        _pendingVictoryWinner = null;
        _cooldowns.Clear();
    }
}
