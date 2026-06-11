using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{

    private sealed class ConfettiParticle
    {
        public float X, Y;
        public float StartX;
        public float Vy;         // px/s
        public float Amp;        // px
        public float Freq;       // rad/s
        public float Phase;
        public float Rot;        // radians
        public float RotSpeed;   // rad/s
        public float Life;       // seconds since spawn
        public uint  BaseColor;  // 0x00BBGGRR; alpha applied at draw time
        public float Size;       // half-width for rect, or radius for circle
        public bool  IsCircle;
    }

    private const int ConfettiCount = 48;
    private readonly List<ConfettiParticle> _confetti = [];
    private bool  _confettiInitialized;
    private float _confettiElapsed;
    private static readonly Random _confettiRng = new();

    private const float ConfettiDuration  = 5f;
    private const float ConfettiFadeStart = 4f;

    private void ResetConfetti()
    {
        _confetti.Clear();
        _confettiInitialized = false;
        _confettiElapsed     = 0f;
    }

    private static void SpawnConfettiParticle(
        ConfettiParticle p, Vector2 clipMin, Vector2 clipMax, bool atTop)
    {
        var rng   = _confettiRng;
        var areaW = clipMax.X - clipMin.X;
        var areaH = clipMax.Y - clipMin.Y;

        p.StartX   = clipMin.X + rng.NextSingle() * areaW;
        p.X        = p.StartX;
        p.Y        = atTop
            ? clipMin.Y - rng.NextSingle() * Px(60f)
            : clipMin.Y + rng.NextSingle() * areaH;
        p.Vy       = 55f + rng.NextSingle() * 85f;
        p.Amp      = Px(8f)  + rng.NextSingle() * Px(28f);
        p.Freq     = 1.5f + rng.NextSingle() * 2.5f;
        p.Phase    = rng.NextSingle() * MathF.Tau;
        p.Rot      = rng.NextSingle() * MathF.Tau;
        p.RotSpeed = (rng.NextSingle() - 0.5f) * 7f;
        p.Life     = 0f;
        p.BaseColor = UiColors.ConfettiPalette[rng.Next(UiColors.ConfettiPalette.Length)];
        p.IsCircle = rng.NextSingle() < 0.28f;
        p.Size     = p.IsCircle
            ? Px(2.5f) + rng.NextSingle() * Px(2.0f)
            : Px(4.5f) + rng.NextSingle() * Px(4.5f);
    }


    private void DrawStepFinished()
    {
        var t     = ThemeService.Current;
        var muted = new Vector4(0.55f, 0.55f, 0.55f, 0.75f);
        var green = new Vector4(0.38f, 0.82f, 0.48f, 1.00f);

        var wPos    = ImGui.GetWindowPos();
        var wSize   = ImGui.GetWindowSize();
        var clipMin = wPos + Px(0f,      58f + 6f);    // below header
        var clipMax = wPos + new Vector2(wSize.X, wSize.Y - Px(48f)); // above nav bar

        var availH = ImGui.GetContentRegionAvail().Y;
        using (var scroll = ImRaii.Child("##finishedScroll", new Vector2(0f, availH), false))
        {
            if (scroll.Success)
            {
                ImGui.Spacing();

                using (UiFonts.H2?.Push())
                {
                    var Heading = Loc.T("onboarding.finished_heading");
                    var headSz = ImGui.CalcTextSize(Heading);
                    ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - headSz.X) * 0.5f);
                    ImGui.TextColored(t.AccentLight, Heading);
                }

                ImGui.Spacing();
                ImGui.TextWrapped(Loc.T("onboarding.finished_intro"));
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("onboarding.finished_verification_heading"), t);
                ImGui.TextWrapped(Loc.T("onboarding.finished_verification_body"));
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(muted, Loc.T("onboarding.finished_verification_note"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("onboarding.finished_swiping_heading"), t);
                ImGui.TextWrapped(Loc.T("onboarding.finished_swiping_body"));
                ImGui.Spacing();
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("onboarding.finished_rejected_heading"), t);
                ImGui.TextWrapped(Loc.T("onboarding.finished_rejected_body"));
                ImGui.Spacing();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(green, Loc.T("onboarding.finished_good_luck"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }
        }


        var dt = ImGui.GetIO().DeltaTime;
        _confettiElapsed += dt;

        if (!_confettiInitialized)
        {
            for (var i = 0; i < ConfettiCount; i++)
            {
                var p = new ConfettiParticle();
                SpawnConfettiParticle(p, clipMin, clipMax, atTop: false);
                _confetti.Add(p);
            }
            _confettiInitialized = true;
        }

        if (_confettiElapsed < ConfettiDuration)
        {
            var globalAlpha = _confettiElapsed >= ConfettiFadeStart
                ? 1f - (_confettiElapsed - ConfettiFadeStart) / (ConfettiDuration - ConfettiFadeStart)
                : 1f;

            var dl = ImGui.GetWindowDrawList();
            dl.PushClipRect(clipMin, clipMax, true);

            foreach (var p in _confetti)
            {
                p.Life += dt;
                p.Y    += p.Vy * dt;
                p.X     = p.StartX + MathF.Sin(p.Life * p.Freq + p.Phase) * p.Amp;
                p.Rot  += p.RotSpeed * dt;

                if (p.Y > clipMax.Y + p.Size * 2f)
                {
                    SpawnConfettiParticle(p, clipMin, clipMax, atTop: true);
                    continue;
                }

                var alpha  = MathF.Min(1f, MathF.Max(0f, p.Life / 0.25f)) * globalAlpha;
                var colorA = ((uint)(alpha * 210f) << 24) | (p.BaseColor & 0x00FFFFFFu);
                var center = new Vector2(p.X, p.Y);

                if (p.IsCircle)
                {
                    dl.AddCircleFilled(center, p.Size, colorA);
                }
                else
                {
                    var hw  = p.Size;
                    var hh  = p.Size * 0.45f;
                    var cos = MathF.Cos(p.Rot);
                    var sin = MathF.Sin(p.Rot);
                    var tl  = center + new Vector2(-hw * cos + hh * sin, -hw * sin - hh * cos);
                    var tr  = center + new Vector2( hw * cos + hh * sin,  hw * sin - hh * cos);
                    var br  = center + new Vector2( hw * cos - hh * sin,  hw * sin + hh * cos);
                    var bl  = center + new Vector2(-hw * cos - hh * sin, -hw * sin + hh * cos);
                    dl.AddQuadFilled(tl, tr, br, bl, colorA);
                }
            }

            dl.PopClipRect();
        }
    }
}
