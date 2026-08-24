using System;
using System.IO;
using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Startup splash: the AetherOS logo on a dark fill with a light shine sweeping across it to signal
/// loading, held for a few seconds while the session bootstraps. Tap to skip once the bootstrap resolves.</summary>
public sealed class SplashScreen : IDisposable
{
    private const string LogoFileName = "splash-screen.png";
    private const float TexW = 913f;
    private const float TexH = 1723f;

    /// <summary>#010a1c, behind the image in the unlikely event of a gap.</summary>
    private static readonly Vector4 BackgroundColor = new(1f / 255f, 10f / 255f, 28f / 255f, 1f);

    private const float FadeInSeconds = 0.4f;

    /// <summary>Minimum hold before the fade-out: long enough for the charge animation's breath and beam
    /// sweep to play, but the real gate is the bootstrap; a finished boot leaves at this mark rather than
    /// sitting out a longer show (it held 8s flat before 2026-08-18).</summary>
    private const float ShineDuration = 5f;

    /// <summary>The splash content fade-out at the end, before it hands off to the home screen.</summary>
    private const float FadeOutSeconds = 0.45f;

    /// <summary>The ambient loop (glow breath, beam sweep, star twinkle), in seconds.</summary>
    private const float LoopPeriod = 2.25f;

    /// <summary>Cap so a hung server can't lock the splash forever.</summary>
    private const float BootstrapWaitCap = 10f;

    private const float Tau = 6.2831853f;

    /// <summary>The frozen animation pose used under reduce-motion.</summary>
    private const float UStatic = 0.25f;

    /// <summary>Focal anchors in the SOURCE image's UV space: the crystal centre and its painted glint.</summary>
    private static readonly Vector2 CrystalUv = new(0.500f, 0.465f);
    private static readonly Vector2 GlintUv = new(0.635f, 0.480f);

    private static readonly Vector4 IceBlue = new(0.42f, 0.66f, 1f, 1f);
    private static readonly Vector4 CoreWhite = new(0.80f, 0.92f, 1f, 1f);

    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly WebpCapabilityProbe _webpProbe;
    private ISharedImmediateTexture? _logoTexture;
    private float _elapsed;
    private bool _fadingOut;
    private float _fadeOutT;

    public SplashScreen(ScreenRouter router, SessionBootstrapper bootstrap, WebpCapabilityProbe webpProbe)
    {
        _router = router;
        _bootstrap = bootstrap;
        _webpProbe = webpProbe;
    }

    public void OnShow()
    {
        _elapsed = 0f;
        _fadingOut = false;
        _fadeOutT = 0f;

        _ = _bootstrap.RunAsync();

        if (_logoTexture == null)
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
            var logoPath = Path.Combine(dir, "Media", LogoFileName);
            _logoTexture = File.Exists(logoPath)
                ? Plugin.TextureProvider.GetFromFile(logoPath)
                : null;

            if (_logoTexture == null)
            {
                Plugin.Log.Warning($"[SplashScreen] Logo not found: {logoPath}");
            }
        }
    }

    private void Advance()
    {
        _router.Navigate(_bootstrap.ResolveNextStartupScreen());
    }

    public void Dispose()
    {
    }

    public void Draw()
    {
        var dt = ImGui.GetIO().DeltaTime;
        _elapsed += dt;
        _webpProbe.Tick(dt);

        var reduce = AccessibilityService.ReduceMotion;
        var bootstrapDone = _bootstrap.LastResult != SessionBootstrapResult.Pending;

        if (_fadingOut)
        {
            _fadeOutT += dt / FadeOutSeconds;
            if (_fadeOutT >= 1f)
            {
                Advance();
                return;
            }
        }
        else if (_elapsed >= ShineDuration && (bootstrapDone || _elapsed >= BootstrapWaitCap))
        {
            if (reduce)
            {
                Advance();
                return;
            }
            _fadingOut = true;
        }

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        dl.AddRectFilled(origin, origin + avail, ImGui.ColorConvertFloat4ToU32(BackgroundColor));

        var fade = reduce ? 1f : Math.Clamp(_elapsed / FadeInSeconds, 0f, 1f);
        if (_fadingOut)
        {
            fade *= Math.Clamp(1f - _fadeOutT, 0f, 1f);
        }

        var tex = _logoTexture?.GetWrapOrDefault();
        if (tex != null)
        {
            var (uv0, uv1) = CoverUv(avail.X, avail.Y);
            dl.AddImage(tex.Handle, origin, origin + avail, uv0, uv1,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, fade)));
            DrawAetherCharge(dl, origin, origin + avail, fade);
        }
        else
        {
            const string Title = "AetherOS";
            var tsz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorScreenPos(origin + (avail - tsz) * 0.5f);
            ImGui.TextColored(new Vector4(0.75f, 0.88f, 1f, fade), Title);
        }

        // Tap to skip, but only before the fade-out has started.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##splashTap", avail);
        if (!_fadingOut && ImGui.IsItemClicked() && bootstrapDone)
        {
            if (reduce)
            {
                Advance();
                return;
            }
            _fadingOut = true;
        }
    }

    /// <summary>Cover-fit UVs cropping the (TexW x TexH) image onto a destination of the given aspect.</summary>
    private static (Vector2 uv0, Vector2 uv1) CoverUv(float destW, float destH)
    {
        var texAspect = TexW / TexH;
        var destAspect = destW / destH;
        if (texAspect > destAspect)
        {
            var crop = destAspect / texAspect;
            var x0 = (1f - crop) * 0.5f;
            return (new Vector2(x0, 0f), new Vector2(x0 + crop, 1f));
        }
        var cropY = texAspect / destAspect;
        var y0 = (1f - cropY) * 0.5f;
        return (new Vector2(0f, y0), new Vector2(1f, y0 + cropY));
    }

    private static float Frac(float x) => x - MathF.Floor(x);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Sin01(float u, float cyc, float ph) => 0.5f + 0.5f * MathF.Sin(Tau * (cyc * u + ph));
    private static float Hann(float x) => MathF.Abs(x) < 1f ? 0.5f * (1f + MathF.Cos(MathF.PI * x)) : 0f;
    private static float Smoother(float t) => t * t * t * (t * (6f * t - 15f) + 10f);
    private static float Hash(int i, float s)
    {
        var v = MathF.Sin(i * 12.9898f + s * 78.233f) * 43758.5453f;
        return v - MathF.Floor(v);
    }
    private static uint U32(Vector4 c, float a) => ImGui.ColorConvertFloat4ToU32(c with { W = a });

    /// <summary>"Aether Charge": the crystal breathes a soft icy halo, sonar rings ripple out through the rune
    /// circle (the loading tell), a diagonal sheen rakes the whole panel and ignites the crystal's painted
    /// glint as it passes, and a hashed twinkle field flickers. All plain-alpha, seamless per <see cref="LoopPeriod"/>;
    /// frozen to one composed still under reduce-motion.</summary>
    private void DrawAetherCharge(ImDrawListPtr dl, Vector2 tl, Vector2 br, float fade)
    {
        var animated = !AccessibilityService.ReduceMotion;
        var avail = br - tl;
        float w = avail.X, h = avail.Y;
        var u = animated ? Frac(_elapsed / LoopPeriod) : UStatic;

        var (uv0, uv1) = CoverUv(w, h);
        Vector2 Map(Vector2 uv) => tl + new Vector2((uv.X - uv0.X) / (uv1.X - uv0.X),
                                                    (uv.Y - uv0.Y) / (uv1.Y - uv0.Y)) * avail;
        var c = Map(CrystalUv);
        var gp = Map(GlintUv);

        var breath = animated ? Sin01(u, 1f, -0.25f) : 0.5f;
        var coreBeat = MathF.Pow(breath, 1.4f);

        dl.PushClipRect(tl, br, true);

        // L1a - crystal halo: a stacked low-alpha bell that lifts as a soft aura.
        var ra = w * (0.30f + 0.06f * breath);
        var aA = 1f - MathF.Pow(1f - (0.08f + 0.06f * breath) * fade, 1f / 14f);
        var auraCol = Vector4.Lerp(IceBlue, CoreWhite, 0.3f * breath);
        for (var i = 0; i < 14; i++)
        {
            dl.AddCircleFilled(c, ra * (1f - i / 14f), U32(auraCol, aA), 36);
        }

        // L1b - core bloom, elongated along the crystal's vertical axis.
        var hy = h * 0.13f;
        var rb = w * (0.10f + 0.03f * coreBeat);
        void DrawCore(Vector2 off, float wgt)
        {
            var aB = 1f - MathF.Pow(1f - (0.10f + 0.14f * coreBeat) * wgt * fade, 1f / 6f);
            for (var j = 0; j < 6; j++)
            {
                dl.AddCircleFilled(c + off, rb * (1f - j / 6f), U32(CoreWhite, aB), 28);
            }
        }
        DrawCore(new Vector2(0f, -0.45f * hy), 0.65f);
        DrawCore(Vector2.Zero, 1f);
        DrawCore(new Vector2(0f, 0.45f * hy), 0.6f);

        // L1c - glint throb, breath-synced.
        dl.AddCircleFilled(gp, w * 0.040f, U32(Vector4.One, 0.10f * coreBeat * fade), 24);
        dl.AddCircleFilled(gp, w * 0.014f, U32(Vector4.One, 0.40f * coreBeat * fade), 16);

        // L3 - a single diagonal beam sweeping once, top-left to bottom-right (scheduled on absolute time, so
        // it fires exactly once). Drawn as thin quads stacked perpendicular to the beam: a full-height column
        // recoloured by its 4 corners can only gradient top-to-bottom, never show a band floating mid-screen,
        // which is what made earlier attempts render as a vertical pillar.
        var beamP = animated ? (_elapsed - 0.8f) / 2.5f : 0.5f;
        if (beamP >= 0f && beamP <= 1f)
        {
            var ud = new Vector2(0.7071f, 0.7071f);        // 45-degree travel, top-left to bottom-right
            var perp = new Vector2(-ud.Y, ud.X);
            var sSpan = w * ud.X + h * ud.Y;               // = S(br)
            var hwW = 0.11f * sSpan;                       // soft outer halo half-width
            var hwC = 0.035f * sSpan;                      // narrow bright core = the visible beam
            var sCenter = -hwW + Smoother(beamP) * (sSpan + 2f * hwW);
            var reach = MathF.Sqrt(w * w + h * h);         // long enough to cross the screen at any angle
            const int strips = 40;
            var ds = 2f * hwW / strips;
            for (var i = 0; i < strips; i++)
            {
                var si = sCenter - hwW + (i + 0.5f) * ds;
                var d = si - sCenter;
                var iC = Hann(d / hwC) * 0.16f;
                var iW = Hann(d / hwW) * 0.05f;
                var a = MathF.Min(iC + iW, 0.18f) * fade;
                if (a <= 0.002f)
                {
                    continue;
                }
                var col = U32(IceBlue, a);   // soft blue beam (no white core)
                var mid = tl + ud * si;
                var t = ud * (ds * 0.75f);
                dl.AddQuadFilled(mid - perp * reach - t, mid + perp * reach - t,
                                 mid + perp * reach + t, mid - perp * reach + t, col);
            }
        }

        // L5 - tiny background stars that twinkle in brightness only (no size change, no flare). Deterministic
        // hashed positions/phases (no per-frame RNG).
        for (var i = 0; i < 26; i++)
        {
            var pos = tl + new Vector2(Lerp(0.05f, 0.95f, Hash(i, 1f)) * w, Lerp(0.04f, 0.62f, Hash(i, 2f)) * h);
            var clear = w * 0.11f;
            if ((pos - c).LengthSquared() < clear * clear)
            {
                continue; // keep clear of the bright crystal core
            }
            var twK = 2 + (int)(Hash(i, 4f) * 3f);
            var tw = animated ? Sin01(u, twK, Hash(i, 5f)) : 0.5f;
            var b = 0.3f + 0.7f * tw;
            var sz = MathF.Max(1f, (0.8f + 0.6f * Hash(i, 3f)) * (h / 1700f) * 1.6f);
            dl.AddCircleFilled(pos, sz * 1.8f, U32(IceBlue, 0.12f * b * fade), 6);
            dl.AddCircleFilled(pos, sz, U32(Vector4.One, 0.60f * b * fade), 6);
        }

        dl.PopClipRect();
    }
}
