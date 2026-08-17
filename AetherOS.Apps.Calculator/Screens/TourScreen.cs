using System;
using System.Numerics;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>The five-step introduction, auto-run once. The graphing steps draw a real miniature plot through
/// the same code the graph view uses, rather than a picture of one.</summary>
internal sealed class TourScreen
{
    private const string DemoSource = "sin(x)*x";

    /// <summary>Where the graphing half begins: the keypad step, then plotting, tracing and the table.</summary>
    private const int GraphingFirstStep = 2;

    private readonly Action<CalcMode> _done;
    private readonly Func<CalcMode> _currentMode;
    private readonly ConfettiBurst _confetti = new();
    private readonly CalcSession _demo = new();
    private CalcMode _mode = CalcMode.Simple;
    private int _step;

    /// <summary>The step Back leaves on. Zero for the full introduction; the graphing explainer starts partway
    /// in, and stepping back out of it into the mode question would be answering something nobody asked.</summary>
    private int _floor;

    /// <summary>Simple gets a short introduction because there is little to introduce; the graphing keypad
    /// earns the longer one.</summary>
    private int TotalSteps => _mode == CalcMode.Simple ? 3 : 6;

    public TourScreen(Action<CalcMode> done, Func<CalcMode> currentMode)
    {
        _done = done;
        _currentMode = currentMode;
        _demo.Functions[0].Source = DemoSource;
        _demo.Functions[0].Recompile();
        _demo.Window = new GraphWindow
        {
            XMin = -10d,
            XMax = 10d,
            YMin = -9d,
            YMax = 9d,
        };
    }

    public void OnShow()
    {
        _step = 0;
        _floor = 0;
        _mode = _currentMode();
    }

    /// <summary>Just the graphing half, for somebody meeting the graphing keypad for the first time: either
    /// they chose simple at the start and switched later, or they arrived from an older install.</summary>
    public void OnShowGraphing()
    {
        _mode = CalcMode.Graphing;
        _step = GraphingFirstStep;
        _floor = GraphingFirstStep;
    }

    public void Draw(OsAppContext ctx)
    {
        if (OnboardingUi.DrawProgress(_step, TotalSteps, true))
        {
            if (_step <= _floor)
            {
                _done(_mode);
            }
            else
            {
                _step--;
            }
        }

        var topH = ctx.Px(34f);
        var navH = ctx.Px(62f);
        var contentH = ImGui.GetWindowSize().Y - topH - navH;

        ImGui.SetCursorPos(new Vector2(0f, topH));
        SharedUiHelpers.PushScrollbarStyle();
        using (var content = ImRaii.Child("##calcTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome(ctx);
                        break;
                    case 1:
                        DrawModeChoice(ctx);
                        break;
                    case 2 when _mode == CalcMode.Simple:
                        DrawSimple(ctx);
                        break;
                    case 2:
                        DrawKeypad(ctx);
                        break;
                    case 3:
                        DrawGraphing(ctx);
                        break;
                    case 4:
                        DrawTracing(ctx);
                        break;
                    default:
                        DrawTable(ctx);
                        break;
                }
            }
        }
        SharedUiHelpers.PopScrollbarStyle();

        var last = _step >= TotalSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - ctx.Px(54f)));
        if (OnboardingUi.DrawPrimaryButton(
            last ? ctx.Localize("os.calc_tour_start_btn") : ctx.Localize("onboarding.next"), true))
        {
            if (last)
            {
                _done(_mode);
                return;
            }
            _step++;
            if (_step == TotalSteps - 1)
            {
                _confetti.Reset();
            }
        }

        if (last && !ctx.ReduceMotion)
        {
            _confetti.Draw(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        }
    }

    private static void DrawWelcome(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        OnboardingUi.DrawHero("calculator_tour_welcome", FontAwesomeIcon.Calculator,
            ctx.Localize("os.calc_tour_s0_title"), ctx.Localize("os.calc_tour_s0_body"), 40f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Superscript, ctx.Localize("os.calc_tour_s0_f1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.ChartLine, ctx.Localize("os.calc_tour_s0_f2"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Table, ctx.Localize("os.calc_tour_s0_f3"));
    }

    /// <summary>The one question the introduction asks. Simple is preselected, so Next always does something
    /// and the answer is a change of mind rather than a decision nobody asked to make.</summary>
    private void DrawModeChoice(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        OnboardingUi.DrawHero("calculator_tour_mode", FontAwesomeIcon.SlidersH,
            ctx.Localize("os.calc_tour_mode_title"), ctx.Localize("os.calc_tour_mode_body"), 34f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));

        DrawModeCard(ctx, CalcMode.Simple, FontAwesomeIcon.Calculator,
            ctx.Localize("os.calc_mode_simple"), ctx.Localize("os.calc_mode_simple_hint"));
        DrawModeCard(ctx, CalcMode.Graphing, FontAwesomeIcon.ChartLine,
            ctx.Localize("os.calc_mode_graphing"), ctx.Localize("os.calc_mode_graphing_hint"));

        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
        OnboardingUi.DrawCenteredParagraph(ctx.Localize("os.calc_tour_mode_switch"),
            ImGui.GetWindowSize().X - ctx.Px(48f), UiColors.Hint);
    }

    private void DrawModeCard(OsAppContext ctx, CalcMode mode, FontAwesomeIcon icon, string title, string hint)
    {
        var selected = _mode == mode;
        var winW = ImGui.GetWindowSize().X;
        var pad = ctx.Px(22f);
        var cardW = winW - pad * 2f;
        var wrapW = cardW - ctx.Px(58f);
        var titleH = ImGui.GetTextLineHeight();
        var hintH = ImGui.CalcTextSize(hint, false, wrapW).Y;
        var cardH = titleH + hintH + ctx.Px(26f);

        var tl = new Vector2(ImGui.GetWindowPos().X + pad, ImGui.GetCursorScreenPos().Y);
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##calcModeCard{mode}", new Vector2(cardW, cardH)))
        {
            _mode = mode;
        }
        SharedUiHelpers.HandOnHover();
        var hovered = ImGui.IsItemHovered();

        var accent = DeviceUi.Teal;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(selected
                ? accent with { W = 0.16f }
                : new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.05f)), ctx.Px(14f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(selected ? accent with { W = 0.75f } : new Vector4(1f, 1f, 1f, 0.10f)),
            ctx.Px(14f), ImDrawFlags.None, ctx.Px(selected ? 1.6f : 1f));

        var iconPx = ctx.Px(20f);
        var iconSz = IconDraw.Measure(icon, iconPx);
        IconDraw.Add(dl, icon, iconPx,
            new Vector2(tl.X + ctx.Px(16f), tl.Y + ctx.Px(13f) + (titleH - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(selected ? accent : UiColors.Body));

        var textX = tl.X + ctx.Px(46f);
        dl.AddText(new Vector2(textX, tl.Y + ctx.Px(13f)),
            ImGui.GetColorU32(selected ? accent : UiColors.Body), title);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, tl.Y + ctx.Px(13f) + titleH),
            ImGui.GetColorU32(UiColors.Hint), hint, wrapW);

        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
    }

    private static void DrawSimple(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        OnboardingUi.DrawHero("calculator_tour_simple", FontAwesomeIcon.Calculator,
            ctx.Localize("os.calc_tour_simple_title"), ctx.Localize("os.calc_tour_simple_body"), 40f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Keyboard, ctx.Localize("os.calc_tour_simple_f1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.History, ctx.Localize("os.calc_tour_simple_f2"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.ChartLine, ctx.Localize("os.calc_tour_simple_f3"));
    }

    private static void DrawKeypad(OsAppContext ctx)
    {
        OnboardingUi.DrawHero("calculator_tour_keys", FontAwesomeIcon.Th, ctx.Localize("os.calc_tour_s1_title"),
            ctx.Localize("os.calc_tour_s1_body"), 30f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
        DrawKeyDemo(ctx);
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.LevelUpAlt, ctx.Localize("os.calc_tour_s1_f1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Font, ctx.Localize("os.calc_tour_s1_f2"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Undo, ctx.Localize("os.calc_tour_s1_f3"));
    }

    /// <summary>Three real key faces, so the coloured 2nd and ALPHA legends are shown rather than described.</summary>
    private static void DrawKeyDemo(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        var keyW = ctx.Px(56f);
        var keyH = ctx.Px(46f);
        var gap = ctx.Px(10f);
        var tl = new Vector2(ImGui.GetWindowPos().X + (winW - (keyW * 3f + gap * 2f)) * 0.5f,
            ImGui.GetCursorScreenPos().Y);

        DeviceUi.Key(ctx, "##tourKey2nd", tl, new Vector2(keyW, keyH), "2nd", null, null, KeyTone.Second, true);
        DeviceUi.Key(ctx, "##tourKeySin", tl + new Vector2(keyW + gap, 0f), new Vector2(keyW, keyH), "sin",
            "asin", "C", KeyTone.Digit, false);
        DeviceUi.Key(ctx, "##tourKeySqr", tl + new Vector2((keyW + gap) * 2f, 0f), new Vector2(keyW, keyH),
            "x²", "sqrt", "A", KeyTone.Digit, false);
        ImGui.Dummy(new Vector2(winW, keyH + ctx.Px(4f)));
    }

    private void DrawGraphing(OsAppContext ctx)
    {
        OnboardingUi.DrawHero("calculator_tour_graph", FontAwesomeIcon.ChartLine,
            ctx.Localize("os.calc_tour_s2_title"), ctx.Localize("os.calc_tour_s2_body"), 28f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
        DrawMiniGraph(ctx, false);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        OnboardingUi.DrawCenteredParagraph($"Y1 = {DemoSource}", ImGui.GetWindowSize().X - ctx.Px(48f),
            UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        // Numbered, because "you can plot things" was never the missing part: which three keys, in order, was.
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.ListOl, ctx.Localize("os.calc_tour_s2_step1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Pen, ctx.Localize("os.calc_tour_s2_step2"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.ChartLine, ctx.Localize("os.calc_tour_s2_step3"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Palette, ctx.Localize("os.calc_tour_s2_slots"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.EyeSlash, ctx.Localize("os.calc_tour_s2_f2"));
    }

    private void DrawTracing(OsAppContext ctx)
    {
        OnboardingUi.DrawHero("calculator_tour_trace", FontAwesomeIcon.Crosshairs,
            ctx.Localize("os.calc_tour_s3_title"), ctx.Localize("os.calc_tour_s3_body"), 28f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
        DrawMiniGraph(ctx, true);
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.ArrowsAltH, ctx.Localize("os.calc_tour_s3_f1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.SearchPlus, ctx.Localize("os.calc_tour_s3_f2"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Calculator, ctx.Localize("os.calc_tour_s3_f3"));
    }

    private static void DrawTable(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        OnboardingUi.DrawHero("calculator_tour_table", FontAwesomeIcon.Table,
            ctx.Localize("os.calc_tour_s4_title"), ctx.Localize("os.calc_tour_s4_body"), 40f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.StepForward, ctx.Localize("os.calc_tour_s4_f1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Columns, ctx.Localize("os.calc_tour_s4_f2"));
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        OnboardingUi.DrawCenteredParagraph(ctx.Localize("os.calc_tour_s4_hint"),
            ImGui.GetWindowSize().X - ctx.Px(48f), UiColors.Success);
    }

    /// <summary>A real plot at postcard size, drawn by the graph view's own renderer.</summary>
    private void DrawMiniGraph(OsAppContext ctx, bool trace)
    {
        var winW = ImGui.GetWindowSize().X;
        var size = new Vector2(winW - ctx.Px(44f), ctx.Px(132f));
        var tl = new Vector2(ImGui.GetWindowPos().X + (winW - size.X) * 0.5f, ImGui.GetCursorScreenPos().Y);

        var window = _demo.Window;
        if (!ctx.ReduceMotion)
        {
            var breath = 1f + 0.16f * MathF.Sin((float)ImGui.GetTime() * 0.6f);
            window.XMin = -10d * breath;
            window.XMax = 10d * breath;
        }

        var dl = ImGui.GetWindowDrawList();
        GraphPlot.DrawFrame(ctx, dl, tl, size, window);
        var fn = _demo.Functions[0];
        GraphPlot.DrawCurve(ctx, dl, tl, size, _demo, fn, window);

        if (trace)
        {
            var phase = ctx.ReduceMotion ? 0.3f : (float)((ImGui.GetTime() * 0.18d) % 1d);
            var x = window.XMin + window.Width * phase;
            if (_demo.TrySample(fn, x, out var y))
            {
                GraphPlot.DrawTraceCursor(ctx, dl, tl, size, window, GraphPlot.ToScreen(window, tl, size, x, y),
                    fn.Color);
                var readout = $"x={CalcFormat.Axis(x)}  y={CalcFormat.Axis(y)}";
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f, tl + new Vector2(ctx.Px(6f), ctx.Px(4f)),
                    DeviceUi.Ink(0.85f), readout);
            }
        }

        ImGui.Dummy(new Vector2(winW, size.Y + ctx.Px(4f)));
    }
}
