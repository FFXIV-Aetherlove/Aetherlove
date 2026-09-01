using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The moment it grows, given the crystal's own treatment and then some. The room does not dim,
/// it goes OUT: the page falls to true black, the creature is left alone in it drawing its light in and
/// shaking with the effort, collapses to a point, and then there is nothing at all for a beat. The flash
/// that ends the dark is where the body is swapped, which is the one frame nobody can see it happen.
///
/// <para>The held black is the whole trick. A dip to 88% grey reads as a transition; a page with nothing
/// on it reads as something about to happen.</para></summary>
internal sealed class EvolutionScene
{
    private const float DimEnd = 0.7f;
    private const float GatherEnd = 2.0f;
    private const float CollapseEnd = 2.4f;
    private const float FlashAt = 2.8f;
    private const float FlashPeak = 0.1f;
    private const float FlashFade = 0.7f;
    private const float PopStart = 2.86f;
    private const float PopSeconds = 0.62f;
    private const float TitleAt = 3.35f;
    private const float LiftStart = 5.5f;
    private const float TotalSeconds = 6.2f;

    private readonly PetRuntime _pet;

    private float _t = -1f;
    private bool _swapped;
    private bool _adulting;
    private Vector4 _accent = new(0.62f, 0.88f, 0.85f, 1f);

    public EvolutionScene(PetRuntime pet) => _pet = pet;

    /// <summary>Fires once, on the frame the flash lands: the caller swaps the body and cracks.</summary>
    public event Action? Flashed;

    public bool Playing => _t >= 0f;

    /// <summary>Starts the ceremony. <paramref name="adulting"/> makes it the bigger one, for the
    /// growing up that only happens once.</summary>
    public void Begin(bool adulting, Vector4 accent)
    {
        _t = 0f;
        _swapped = false;
        _adulting = adulting;
        _accent = accent;
    }

    public void Stop() => _t = -1f;

    /// <summary>Draws the whole scene over the page and advances it. Returns false when it is done.
    /// Under reduce motion it holds only long enough to swap the body and get out of the way.</summary>
    public bool Draw(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float dt, string name)
    {
        if (_t < 0f)
        {
            return false;
        }

        if (ctx.ReduceMotion)
        {
            RaiseFlash(ctx);
            _t = -1f;
            return false;
        }

        _t += dt;
        var centre = new Vector2(origin.X + (size.X * 0.5f), origin.Y + (size.Y * 0.54f));
        var petSize = MathF.Min(size.X * 0.62f, size.Y * 0.46f) * (_adulting ? 1.15f : 1f);

        // Out, not down: at full alpha the page underneath is simply gone.
        var dim = _t < DimEnd
            ? Ease(_t / DimEnd)
            : _t > LiftStart
                ? Math.Clamp((TotalSeconds - _t) / (TotalSeconds - LiftStart), 0f, 1f)
                : 1f;
        dl.AddRectFilled(origin, origin + size, Look.U32(new Vector4(0f, 0f, 0f, 1f), dim));

        DrawGathering(dl, centre, petSize);

        if (_t >= FlashAt)
        {
            RaiseFlash(ctx);
        }

        var scale = ShapeScale();
        if (scale > 0.01f)
        {
            var pose = _pet.Pose;
            pose.Scale *= scale;

            // The tremble: nothing at the start, hardest just before it goes.
            var shake = Vector2.Zero;
            if (_t < CollapseEnd)
            {
                var build = Math.Clamp(_t / GatherEnd, 0f, 1f);
                var strength = build * build * petSize * 0.035f;
                shake = new Vector2(
                    MathF.Sin(_t * 47f) * strength,
                    MathF.Sin(_t * 61f) * strength * 0.6f);
            }

            var feet = centre + new Vector2(0f, petSize * 0.5f) + shake;
            _pet.Draw(dl, ctx.Capabilities.Textures, feet, petSize, pose);
        }

        // The point it collapses into, and the dark it leaves behind.
        if (_t >= GatherEnd && _t < FlashAt)
        {
            var p = Math.Clamp((_t - GatherEnd) / (CollapseEnd - GatherEnd), 0f, 1f);
            var fade = _t < CollapseEnd ? 1f : Math.Clamp(1f - ((_t - CollapseEnd) / (FlashAt - CollapseEnd)), 0f, 1f);
            var radius = petSize * (0.34f - (0.30f * p));
            Look.Halo(dl, centre, MathF.Max(2f, radius * 2.4f), _accent, 0.85f * fade);
            dl.AddCircleFilled(centre, MathF.Max(1.5f, radius),
                Look.U32(new Vector4(1f, 1f, 1f, 1f), 0.9f * fade), 20);
        }

        // The flash: white over everything, including the black.
        var since = _t - FlashAt;
        if (since >= 0f && since < FlashPeak + FlashFade)
        {
            var alpha = since < FlashPeak
                ? since / FlashPeak
                : 1f - ((since - FlashPeak) / FlashFade);
            dl.AddRectFilled(origin, origin + size,
                Look.U32(new Vector4(1f, 1f, 1f, 1f), (_adulting ? 1f : 0.94f) * Math.Clamp(alpha, 0f, 1f)));
        }

        if (_t >= TitleAt)
        {
            var fade = Math.Clamp((_t - TitleAt) / 0.5f, 0f, 1f)
                * Math.Clamp((TotalSeconds - _t) / 0.8f, 0f, 1f);
            var title = ctx.Localize(_adulting ? "os.aetherling_grew_adult" : "os.aetherling_grew");
            Look.GlowText(dl, string.Format(title, name), centre.X, origin.Y + (size.Y * 0.20f),
                Look.U32(Look.CrystalPale, fade), _adulting ? 1.6f : 1.35f, _accent, 0.95f * fade);
        }

        if (_t >= TotalSeconds)
        {
            _t = -1f;
            return false;
        }
        return true;
    }

    /// <summary>The light drawing inward before the collapse, then one shell pushing back out.</summary>
    private void DrawGathering(ImDrawListPtr dl, Vector2 centre, float petSize)
    {
        if (_t < GatherEnd)
        {
            // Rings closing in, and closing faster as the moment approaches.
            var p = Math.Clamp((_t - (DimEnd * 0.5f)) / (GatherEnd - (DimEnd * 0.5f)), 0f, 1f);
            var rings = _adulting ? 4 : 3;
            for (var i = 0; i < rings; i++)
            {
                var phase = ((p * (1f + p)) + (i / (float)rings)) % 1f;
                var radius = petSize * (1.35f - (1.05f * phase));
                var alpha = MathF.Sin(phase * MathF.PI) * 0.55f * p;
                Look.Halo(dl, centre, radius, _accent, alpha);
            }
            return;
        }

        var out0 = Math.Clamp((_t - FlashAt) / 1.1f, 0f, 1f);
        if (out0 > 0f && out0 < 1f)
        {
            Look.Halo(dl, centre, petSize * (0.4f + (2.0f * out0)), _accent, 0.6f * (1f - out0));
        }
    }

    /// <summary>Zero through the collapse and the dark so the sprite is never seen changing, then a
    /// back-out overshoot as the new shape arrives, the curve the birth pops on.</summary>
    private float ShapeScale()
    {
        if (_t < GatherEnd)
        {
            // A long inhale: it draws itself in before it goes.
            return 1f - (0.16f * Ease(_t / GatherEnd));
        }
        if (_t < CollapseEnd)
        {
            var p = (_t - GatherEnd) / (CollapseEnd - GatherEnd);
            return 0.84f * (1f - Ease(p));
        }
        if (_t < PopStart)
        {
            return 0f;
        }

        var pop = Math.Clamp((_t - PopStart) / PopSeconds, 0f, 1f);
        const float Overshoot = 1.70158f;
        var x = pop - 1f;
        return 1f + (((Overshoot + 1f) * x * x * x) + (Overshoot * x * x));
    }

    private static float Ease(float t)
    {
        var x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - (2f * x));
    }

    private void RaiseFlash(OsAppContext ctx)
    {
        if (_swapped)
        {
            return;
        }
        _swapped = true;

        // The burst belongs to the flash, not to the start: it is the new shape arriving.
        _pet.PlayEvolutionMoment(_accent, ctx.ReduceMotion);
        Flashed?.Invoke();
    }
}
