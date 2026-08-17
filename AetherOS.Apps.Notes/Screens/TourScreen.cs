using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Apps.Notes.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Notes.Screens;

/// <summary>The one-time Notes tour: what the app is, writing, searching, pinning, and the clipboard.</summary>
internal sealed class TourScreen
{
    private const int TotalSteps = 5;

    private readonly Action _done;
    private readonly ConfettiBurst _confetti = new();
    private int _step;

    internal TourScreen(Action done)
    {
        _done = done;
    }

    internal void OnShow()
    {
        _step = 0;
    }

    internal void Draw(OsAppContext ctx)
    {
        if (DrawProgress(_step, TotalSteps, true))
        {
            if (_step == 0)
            {
                _done();
            }
            else
            {
                _step--;
            }
        }

        var contentH = ImGui.GetWindowSize().Y - ctx.Px(34f) - ctx.Px(62f);
        ImGui.SetCursorPos(new Vector2(0f, ctx.Px(34f)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##notesTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome(ctx);
                        break;
                    case 1:
                        DrawWriting(ctx);
                        break;
                    case 2:
                        DrawSearching(ctx);
                        break;
                    case 3:
                        DrawPinning(ctx);
                        break;
                    default:
                        DrawClipboard(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _step >= TotalSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - ctx.Px(54f)));
        if (DrawPrimaryButton(NotesUi.T(ctx, last ? "os.notes_tour_start" : "os.notes_tour_next"), true))
        {
            if (last)
            {
                _done();
            }
            else
            {
                _step++;
                if (_step == TotalSteps - 1)
                {
                    _confetti.Reset();
                }
            }
        }
    }

    private static void DrawWelcome(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        DrawHero("notes_tour_welcome", FontAwesomeIcon.StickyNote, NotesUi.T(ctx, "os.notes_tour_s0_title"),
            NotesUi.T(ctx, "os.notes_tour_s0_body"), 40f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.FeatherAlt, NotesUi.T(ctx, "os.notes_tour_s0_f1"));
        DrawFeatureRow(FontAwesomeIcon.Palette, NotesUi.T(ctx, "os.notes_tour_s0_f2"));
        DrawFeatureRow(FontAwesomeIcon.Hdd, NotesUi.T(ctx, "os.notes_tour_s0_f3"));
    }

    private static void DrawWriting(OsAppContext ctx)
    {
        DrawHero("notes_tour_write", FontAwesomeIcon.PenAlt, NotesUi.T(ctx, "os.notes_tour_s1_title"),
            NotesUi.T(ctx, "os.notes_tour_s1_body"), 32f);
        DrawFeatureRow(FontAwesomeIcon.Plus, NotesUi.T(ctx, "os.notes_tour_s1_f1"));
        DrawFeatureRow(FontAwesomeIcon.AlignLeft, NotesUi.T(ctx, "os.notes_tour_s1_f2"));
        DrawFeatureRow(FontAwesomeIcon.ArrowLeft, NotesUi.T(ctx, "os.notes_tour_s1_f3"));
    }

    private static void DrawSearching(OsAppContext ctx)
    {
        DrawHero("notes_tour_search", FontAwesomeIcon.Search, NotesUi.T(ctx, "os.notes_tour_s2_title"),
            NotesUi.T(ctx, "os.notes_tour_s2_body"), 32f);
        DrawFeatureRow(FontAwesomeIcon.Heading, NotesUi.T(ctx, "os.notes_tour_s2_f1"));
        DrawFeatureRow(FontAwesomeIcon.Paragraph, NotesUi.T(ctx, "os.notes_tour_s2_f2"));
        DrawFeatureRow(FontAwesomeIcon.TimesCircle, NotesUi.T(ctx, "os.notes_tour_s2_f3"));
    }

    private static void DrawPinning(OsAppContext ctx)
    {
        DrawHero("notes_tour_pin", FontAwesomeIcon.Thumbtack, NotesUi.T(ctx, "os.notes_tour_s3_title"),
            NotesUi.T(ctx, "os.notes_tour_s3_body"), 32f);
        DrawFeatureRow(FontAwesomeIcon.Thumbtack, NotesUi.T(ctx, "os.notes_tour_s3_f1"));
        DrawFeatureRow(FontAwesomeIcon.Clone, NotesUi.T(ctx, "os.notes_tour_s3_f2"));
        DrawFeatureRow(FontAwesomeIcon.Palette, NotesUi.T(ctx, "os.notes_tour_s3_f3"));
    }

    private void DrawClipboard(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        if (!ctx.ReduceMotion)
        {
            var glowCenter = wPos + new Vector2(wSize.X * 0.5f, wSize.Y * 0.32f);
            var glowSpan = MathF.Min(wSize.X, wSize.Y);
            for (var i = 0; i < 5; i++)
            {
                var r = glowSpan * (0.14f + i * 0.11f);
                var a = 0.08f * (1f - i * 0.18f);
                dl.AddCircleFilled(glowCenter, r, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = a }), 64);
            }
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        DrawHero("notes_tour_clipboard", FontAwesomeIcon.Copy, NotesUi.T(ctx, "os.notes_tour_s4_title"),
            NotesUi.T(ctx, "os.notes_tour_s4_body"), 38f);
        DrawFeatureRow(FontAwesomeIcon.Copy, NotesUi.T(ctx, "os.notes_tour_s4_f1"));
        DrawFeatureRow(FontAwesomeIcon.Paste, NotesUi.T(ctx, "os.notes_tour_s4_f2"));

        ImGui.Dummy(new Vector2(0f, ctx.Px(12f)));
        DrawCenteredParagraph(NotesUi.T(ctx, "os.notes_tour_s4_hint"), wSize.X - ctx.Px(48f), UiColors.Success);

        if (!ctx.ReduceMotion)
        {
            _confetti.Draw(wPos, wPos + wSize);
        }
    }
}
