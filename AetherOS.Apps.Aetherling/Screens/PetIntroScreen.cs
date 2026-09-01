using System;
using System.Numerics;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The real introduction, and the only one there is: the app's first three screens said nothing on
/// purpose. Three beats, the creature itself standing under them, and a question at the end.</summary>
internal sealed class PetIntroScreen(PetRuntime pet, Action<bool, int> done)
{
    private const int TotalBeats = 3;

    private int _beat;
    private double _shown;
    private int _size = FloatingPet.DefaultSizeIndex;

    /// <summary>The size the last answer was given with, so a re-run of the tour opens on the current pick
    /// rather than back at the middle.</summary>
    public int SizeIndex
    {
        get => _size;
        set => _size = value;
    }

    public void OnShow()
    {
        _beat = 0;
        _shown = ImGui.GetTime();
    }

    public void Draw(OsAppContext ctx, string name)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var fade = ctx.ReduceMotion ? 1f : Look.EaseOut((float)(ImGui.GetTime() - _shown) / 0.45f);
        var centreX = origin.X + (size.X * 0.5f);

        Look.Backdrop(dl, ctx.Theme, origin, size);
        Look.Halo(dl, new Vector2(centreX, origin.Y + (size.Y * 0.52f)), size.X * 0.7f, Look.Crystal,
            0.12f * fade);

        var pipY = origin.Y + Px(26f);
        for (var i = 0; i < TotalBeats; i++)
        {
            var centre = new Vector2(centreX + ((i - 1) * Px(16f)), pipY);
            dl.AddCircleFilled(centre, Px(3.5f), Look.U32(Look.Crystal, (i <= _beat ? 0.85f : 0.22f) * fade), 16);
        }

        var titleY = origin.Y + (size.Y * 0.12f);
        using (ctx.TitleFont?.Push())
        {
            Look.CentredWrapped(dl, string.Format(ctx.Localize($"os.aetherling_intro_{_beat}_title"), name),
                centreX, titleY, size.X - Px(44f), Look.U32(Look.CrystalPale, fade), 1f);
        }

        Look.CentredWrapped(dl, string.Format(ctx.Localize($"os.aetherling_intro_{_beat}_body"), name),
            centreX, origin.Y + (size.Y * 0.26f), size.X - Px(52f), Look.U32(Look.Whisper, 0.95f * fade), 0.95f);

        DrawPet(ctx, dl, origin, size, fade);

        if (_beat < TotalBeats - 1)
        {
            if (DrawButton(ctx, dl, origin, size, 0, ctx.Localize("os.aetherling_intro_next"), primary: true, fade))
            {
                Advance();
            }
            return;
        }

        // Asking how big before asking whether, so the answer to the question is complete.
        var pad = Px(30f);
        var pickerY = origin.Y + size.Y - Px(28f) - (2f * (Px(38f) + Px(10f))) - Px(50f);
        Look.Centred(dl, ctx.Localize("os.aetherling_size_label"), origin.X + (size.X * 0.5f), pickerY,
            Look.U32(Look.Whisper, 0.9f * fade), 0.9f);
        PetSizePicker.Draw(ctx, dl, new Vector2(origin.X + pad, pickerY + Px(22f)), size.X - (pad * 2f),
            ref _size);

        // The last beat is a question, so it gets an answer on each side rather than a Next.
        if (DrawButton(ctx, dl, origin, size, 1, ctx.Localize("os.aetherling_intro_yes"), primary: true, fade))
        {
            done(true, _size);
        }
        if (DrawButton(ctx, dl, origin, size, 0, ctx.Localize("os.aetherling_intro_no"), primary: false, fade))
        {
            done(false, _size);
        }
    }

    private void Advance()
    {
        _beat++;
        _shown = ImGui.GetTime();
    }

    private void DrawPet(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float fade)
    {
        if (!pet.Ready)
        {
            return;
        }
        pet.Tick(ctx.ReduceMotion);
        var last = _beat == TotalBeats - 1;

        // On the last beat it stands for the size being picked, so it wears the pick: relative to the middle
        // one, and eased down, because a screen inside a phone cannot show a screen-sized creature honestly.
        var pick = last
            ? 1f + (((FloatingPet.SizeScales[_size] / FloatingPet.SizeScales[FloatingPet.DefaultSizeIndex]) - 1f)
                * 0.55f)
            : 1f;
        var petSize = MathF.Min(size.X * 0.52f, size.Y * 0.30f) * (0.85f + (0.15f * fade))
            * (last ? 0.82f : 1f) * pick;
        var bottom = new Vector2(origin.X + (size.X * 0.5f), origin.Y + (size.Y * (last ? 0.62f : 0.74f)));
        pet.Draw(dl, ctx.Capabilities.Textures, bottom, petSize, pet.Pose);
    }

    /// <summary>Buttons stack from the bottom, so a one-answer beat and a two-answer beat share a layout.</summary>
    private static bool DrawButton(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, int row, string label, bool primary,
        float fade)
    {
        var height = Px(38f);
        var width = size.X - (Px(40f) * 2f);
        var tl = new Vector2(
            origin.X + ((size.X - width) * 0.5f),
            origin.Y + size.Y - Px(28f) - height - (row * (height + Px(10f))));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##aetherlingIntro{row}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var radius = height * 0.5f;
        var fill = primary
            ? Look.Crystal with { W = hovered ? 0.22f : 0.12f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.12f : 0.05f);
        dl.AddRectFilled(tl, tl + new Vector2(width, height), Look.U32(fill, fade), radius);
        if (primary)
        {
            dl.AddRect(tl, tl + new Vector2(width, height), Look.U32(Look.Crystal, (hovered ? 0.8f : 0.45f) * fade),
                radius, ImDrawFlags.RoundCornersAll, Px(1.2f));
        }
        Look.Centred(dl, label, tl.X + (width * 0.5f), tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(primary ? Look.CrystalPale : Look.Whisper, fade));
        _ = ctx;
        return pressed;
    }
}
