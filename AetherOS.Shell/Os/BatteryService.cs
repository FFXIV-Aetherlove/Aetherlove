using System;
using System.Numerics;
using AetherLove.UI;

namespace AetherLove.Os;

/// <summary>The phone's fake battery. Starts full every game session (the state is in-memory, so a reload or a
/// relog refills it), drains while the player keeps playing, and at zero the home screen dies and asks them to go
/// outside. Muting that prompt in settings parks the battery at full and stops the drain entirely.</summary>
public static class BatteryService
{
    public const int Full = 100;

    /// <summary>At or below this the icon goes amber and the drain halves.</summary>
    public const int LowPercent = 20;

    /// <summary>At or below this the icon goes red and pulses.</summary>
    public const int CriticalPercent = 10;

    private const float SecondsPerPercent = 60f;
    private const float LowSecondsPerPercent = 120f;

    public static readonly Vector4 LowTint = new(0.96f, 0.78f, 0.30f, 1f);
    public static readonly Vector4 CriticalTint = new(0.95f, 0.35f, 0.30f, 1f);

    /// <summary>Only ever written from <see cref="Tick"/>, which the host calls on the framework thread. The
    /// grass tap and the debug command run on the draw and command threads, so they request a level instead of
    /// assigning one, or a drain landing in between would swallow the change.</summary>
    private static float _percent = Full;

    private static volatile int _requested = -1;

    /// <summary>Set when a level under full is requested, so the debug command can still reach the low,
    /// critical and flat states while the opt-out is on. Cleared by a full-battery request and by switching
    /// the opt-out on, which is a fresh instruction to stop draining.</summary>
    private static bool _ignoreFloor;

    private static bool _wasMuted;

    /// <summary>Whole percent shown in the status bar. Rounded up, so a full minute elapses before 100 becomes
    /// 99 and the reading only hits 0 when the battery is genuinely flat.</summary>
    public static int Percent => Math.Clamp((int)MathF.Ceiling(_percent - 0.0001f), 0, Full);

    public static float Fraction => Math.Clamp(_percent / Full, 0f, 1f);

    public static bool IsEmpty => Percent <= 0;

    public static bool IsCritical => Percent <= CriticalPercent;

    public static bool IsLow => Percent <= LowPercent;

    /// <summary>Amber from <see cref="LowPercent"/>, red from <see cref="CriticalPercent"/>.</summary>
    public static Vector4 Tint => IsCritical ? CriticalTint : LowTint;

    private static bool Muted => UiHost.Configuration.Os.HideBatteryGrassPrompt;

    /// <summary>While this answers true the battery refuses to reach empty, parking at one percent instead.
    /// The host wires it to "a minigame round is in progress": the go-outside screen taking over mid-run
    /// would cost the player the run, which turns a nudge into a punishment. The drain resumes, and empty
    /// can arrive, the moment the round ends.</summary>
    public static Func<bool>? HoldEmpty { get; set; }

    public static void Reset() => Request(Full);

    private static void Request(int percent)
    {
        _requested = percent;
        FlatBatteryOverlay.Rearm();
    }

    /// <summary>Advances the drain by real elapsed time and applies any requested level. Call every frame:
    /// <paramref name="draining"/> gates only the drain, so a recharge still lands while logged out. A long
    /// stall (an alt-tab, a zone load) is capped so the battery cannot jump several percent in one tick.</summary>
    public static void Tick(float deltaSeconds, bool draining)
    {
        var requested = _requested;
        if (requested >= 0)
        {
            _requested = -1;
            _percent = requested;
            _ignoreFloor = requested < Full;
        }
        // Switching the opt-out on is a fresh instruction, so it retires any debug override still in force.
        var muted = Muted;
        if (muted && !_wasMuted)
        {
            _ignoreFloor = false;
        }
        _wasMuted = muted;

        if (!draining || deltaSeconds <= 0f)
        {
            return;
        }

        var floor = muted && !_ignoreFloor ? Full : 0f;
        if (_percent <= floor)
        {
            // Opting out mid-drain tops the battery back up rather than freezing it wherever it happened to be.
            _percent = floor;
            return;
        }
        const float MaxStepSeconds = 5f;
        var step = MathF.Min(deltaSeconds, MaxStepSeconds);
        var perSecond = 1f / (_percent <= LowPercent ? LowSecondsPerPercent : SecondsPerPercent);
        _percent = MathF.Max(floor, _percent - perSecond * step);

        if (_percent < 1f && HoldEmpty?.Invoke() == true)
        {
            _percent = 1f;
        }
    }

    /// <summary>Stamps that the user has seen the empty screen, so the opt-out appears in settings from now on.</summary>
    public static void MarkEmptySeen()
    {
        if (UiHost.Configuration.Os.BatteryEmptySeen)
        {
            return;
        }
        UiHost.Configuration.Os.BatteryEmptySeen = true;
        UiHost.Configuration.Save();
    }
}
