using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Apps.Calculator.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>What a keypad press asks the app to do when it is not simply typing.</summary>
internal enum CalcNav
{
    Home,
    Graph,
    Table,
    YEditor,
    Window,
    Zoom,
    Trace,
    CalcMenu,
    TblSet,
}

/// <summary>One key face: what is printed on it, its 2nd legend, its ALPHA letter and how it is tinted.</summary>
internal readonly record struct KeyDef(string Id, string Face, string? Second, string? Alpha, KeyTone Tone);

/// <summary>The home view: the LCD tape of finished lines, the live entry, and the full keypad.</summary>
internal sealed class HomeScreen
{
    private static readonly KeyDef[][] Rows =
    [
        [
            new KeyDef("2nd", "2nd", null, null, KeyTone.Second),
            new KeyDef("alpha", "ALPHA", null, null, KeyTone.Alpha),
            new KeyDef("mode", "MODE", null, null, KeyTone.Nav),
            new KeyDef("del", "DEL", null, null, KeyTone.Nav),
            new KeyDef("clear", "CLEAR", null, null, KeyTone.Nav),
        ],
        [
            new KeyDef("ye", "Y=", null, null, KeyTone.Operator),
            new KeyDef("window", "WINDOW", "TBLSET", null, KeyTone.Operator),
            new KeyDef("zoom", "ZOOM", null, null, KeyTone.Operator),
            new KeyDef("trace", "TRACE", "CALC", null, KeyTone.Operator),
            new KeyDef("graph", "GRAPH", "TABLE", null, KeyTone.Operator),
        ],
        [
            new KeyDef("sqr", "x²", "sqrt", "A", KeyTone.Nav),
            new KeyDef("inv", "1/x", "abs", "B", KeyTone.Nav),
            new KeyDef("sin", "sin", "asin", "C", KeyTone.Nav),
            new KeyDef("cos", "cos", "acos", "D", KeyTone.Nav),
            new KeyDef("tan", "tan", "atan", "E", KeyTone.Nav),
        ],
        [
            new KeyDef("pow", "^", null, "F", KeyTone.Nav),
            new KeyDef("log", "log", "10^", "G", KeyTone.Nav),
            new KeyDef("ln", "ln", "e^", "H", KeyTone.Nav),
            new KeyDef("lpar", "(", null, "I", KeyTone.Nav),
            new KeyDef("rpar", ")", null, "J", KeyTone.Nav),
        ],
        [
            new KeyDef("sto", "STO", null, "K", KeyTone.Nav),
            new KeyDef("d7", "7", null, "L", KeyTone.Digit),
            new KeyDef("d8", "8", null, "M", KeyTone.Digit),
            new KeyDef("d9", "9", null, "N", KeyTone.Digit),
            new KeyDef("div", "÷", null, "O", KeyTone.Operator),
        ],
        [
            new KeyDef("pi", "pi", "e", "P", KeyTone.Nav),
            new KeyDef("d4", "4", null, "Q", KeyTone.Digit),
            new KeyDef("d5", "5", null, "R", KeyTone.Digit),
            new KeyDef("d6", "6", null, "S", KeyTone.Digit),
            new KeyDef("mul", "×", null, "T", KeyTone.Operator),
        ],
        [
            new KeyDef("comma", ",", null, "U", KeyTone.Nav),
            new KeyDef("d1", "1", null, "V", KeyTone.Digit),
            new KeyDef("d2", "2", null, "W", KeyTone.Digit),
            new KeyDef("d3", "3", null, "X", KeyTone.Digit),
            new KeyDef("sub", "-", null, "Y", KeyTone.Operator),
        ],
        [
            new KeyDef("neg", "(-)", "Ans", "Z", KeyTone.Digit),
            new KeyDef("d0", "0", null, null, KeyTone.Digit),
            new KeyDef("dot", ".", null, null, KeyTone.Digit),
            new KeyDef("add", "+", null, null, KeyTone.Operator),
            new KeyDef("enter", "ENTER", null, null, KeyTone.Accent),
        ],
    ];

    private readonly CalcSession _session;
    private readonly Action<CalcNav> _nav;
    private bool _second;
    private bool _alpha;
    private int _tapeCount = -1;

    public HomeScreen(CalcSession session, Action<CalcNav> nav)
    {
        _session = session;
        _nav = nav;
    }

    public void OnShow()
    {
        _second = false;
        _alpha = false;
    }

    public void Draw(OsAppContext ctx)
    {
        var top = ImGui.GetCursorScreenPos();
        var region = ImGui.GetContentRegionAvail();
        var padX = ctx.Px(8f);
        var lcdH = Math.Clamp(region.Y * 0.34f, ctx.Px(112f), ctx.Px(200f));
        var lcdTL = new Vector2(top.X + padX + ctx.Px(3f), top.Y + ctx.Px(3f));
        var lcdSize = new Vector2(region.X - (padX + ctx.Px(3f)) * 2f, lcdH);

        var dl = ImGui.GetWindowDrawList();
        DrawChassis(ctx, dl, top, region);
        DeviceUi.Lcd(ctx, dl, lcdTL, lcdSize);
        DrawLcdContents(ctx, lcdTL, lcdSize);

        var keypadTop = lcdTL.Y + lcdSize.Y + ctx.Px(12f);
        var keypadH = top.Y + region.Y - keypadTop - ctx.Px(6f);
        DrawKeypad(ctx, new Vector2(top.X + padX, keypadTop),
            new Vector2(region.X - padX * 2f, MathF.Max(ctx.Px(140f), keypadH)));
    }

    private static void DrawChassis(OsAppContext ctx, ImDrawListPtr dl, Vector2 top, Vector2 region)
    {
        var br = top + region;
        dl.AddRectFilled(top, br, ImGui.ColorConvertFloat4ToU32(DeviceUi.Chassis));
        dl.AddLine(top, new Vector2(br.X, top.Y), ImGui.ColorConvertFloat4ToU32(DeviceUi.ChassisEdge),
            ctx.Px(1f));
    }

    private void DrawLcdContents(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var pad = ctx.Px(7f);
        var statusH = _session.Status is null ? 0f : ImGui.GetTextLineHeight() + ctx.Px(2f);
        var entryH = ImGui.GetTextLineHeight() + ctx.Px(6f);
        var indicatorH = ImGui.GetTextLineHeight() * 0.8f + ctx.Px(2f);
        var tapeTL = new Vector2(tl.X + pad, tl.Y + indicatorH + ctx.Px(2f));
        var tapeSize = new Vector2(size.X - pad * 2f,
            MathF.Max(ctx.Px(20f), size.Y - indicatorH - entryH - statusH - pad - ctx.Px(4f)));

        DrawIndicators(ctx, tl, size, pad);
        DrawTape(ctx, tapeTL, tapeSize);
        DrawEntry(ctx, new Vector2(tapeTL.X, tapeTL.Y + tapeSize.Y + ctx.Px(2f)),
            new Vector2(tapeSize.X, entryH));
        if (_session.Status is { } status)
        {
            var pos = new Vector2(tapeTL.X, tapeTL.Y + tapeSize.Y + entryH + ctx.Px(2f));
            var color = _session.StatusIsError ? new Vector4(0.35f, 0.06f, 0.06f, 1f) : RetroLcd.Pixel;
            ImGui.GetWindowDrawList().AddText(pos, ImGui.ColorConvertFloat4ToU32(color), status);
        }
    }

    private void DrawIndicators(OsAppContext ctx, Vector2 tl, Vector2 size, float pad)
    {
        var dl = ImGui.GetWindowDrawList();
        var scale = 0.8f;
        var mode = ctx.Localize(_session.Env.Angle == AngleMode.Degrees ? "os.calc_deg" : "os.calc_rad");
        DeviceUi.SmallText(dl, mode, new Vector2(tl.X + pad, tl.Y + ctx.Px(3f)), scale, DeviceUi.Ink(0.75f));

        var x = tl.X + size.X - pad;
        if (_alpha)
        {
            var label = "ALPHA";
            var w = ImGui.CalcTextSize(label).X * scale;
            x -= w;
            DeviceUi.SmallText(dl, label, new Vector2(x, tl.Y + ctx.Px(3f)), scale, DeviceUi.Ink());
            x -= ctx.Px(6f);
        }
        if (_second)
        {
            var label = "2nd";
            var w = ImGui.CalcTextSize(label).X * scale;
            x -= w;
            DeviceUi.SmallText(dl, label, new Vector2(x, tl.Y + ctx.Px(3f)), scale, DeviceUi.Ink());
        }
    }

    private void DrawTape(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        using var tape = ImRaii.Child("##calcTape", size, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        if (!tape)
        {
            return;
        }
        if (_session.History.Count == 0)
        {
            ImGui.PushTextWrapPos(size.X);
            ImGui.TextColored(RetroLcd.Pixel with { W = 0.6f }, ctx.Localize("os.calc_history_empty"));
            ImGui.PopTextWrapPos();
            return;
        }

        foreach (var line in _session.History)
        {
            ImGui.TextColored(RetroLcd.Pixel with { W = 0.72f }, line.Source);
            var resultW = ImGui.CalcTextSize(line.Result).X;
            ImGui.SetCursorPosX(MathF.Max(0f, size.X - resultW - ctx.Px(2f)));
            var color = line.IsError ? new Vector4(0.35f, 0.06f, 0.06f, 1f) : RetroLcd.Pixel;
            ImGui.TextColored(color, line.Result);
        }
        if (_tapeCount != _session.History.Count)
        {
            _tapeCount = _session.History.Count;
            ImGui.SetScrollHereY(1f);
        }
    }

    private void DrawEntry(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        using var color = ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0f, 0f))
            .Push(ImGuiCol.FrameBgHovered, RetroLcd.Pixel with { W = 0.08f })
            .Push(ImGuiCol.FrameBgActive, RetroLcd.Pixel with { W = 0.12f })
            .Push(ImGuiCol.Text, RetroLcd.Pixel)
            .Push(ImGuiCol.TextSelectedBg, RetroLcd.Pixel with { W = 0.3f });
        ImGui.SetNextItemWidth(size.X);
        var submitted = ImGui.InputText("##calcEntry", ref _session.Entry, CalcSession.EntryMaxLength,
            ImGuiInputTextFlags.EnterReturnsTrue);
        var active = ImGui.IsItemActive();
        if (ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }
        if (submitted)
        {
            _session.Submit(ctx.Localize);
        }

        if (active || ctx.ReduceMotion)
        {
            return;
        }
        if ((int)(ImGui.GetTime() * 2d) % 2 != 0)
        {
            return;
        }
        var caretX = tl.X + ImGui.GetStyle().FramePadding.X + ImGui.CalcTextSize(_session.Entry).X;
        var caretTL = new Vector2(caretX, tl.Y + ctx.Px(3f));
        ImGui.GetWindowDrawList().AddRectFilled(caretTL,
            caretTL + new Vector2(ctx.Px(6f), ImGui.GetTextLineHeight()), DeviceUi.Ink(0.85f));
    }

    private void DrawKeypad(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var gap = ctx.Px(4f);
        var columns = 5;
        var rows = Rows.Length;
        var keyW = (size.X - gap * (columns - 1)) / columns;
        var rowH = (size.Y - gap * (rows - 1)) / rows;

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var key = Rows[r][c];
                var pos = new Vector2(tl.X + c * (keyW + gap), tl.Y + r * (rowH + gap));
                var latched = (key.Id == "2nd" && _second) || (key.Id == "alpha" && _alpha);
                if (DeviceUi.Key(ctx, $"##calcKey{key.Id}", pos, new Vector2(keyW, rowH), key.Face,
                    key.Second, key.Alpha, key.Tone, latched))
                {
                    Press(ctx, key);
                }
            }
        }
    }

    private void Press(OsAppContext ctx, KeyDef key)
    {
        if (key.Id == "2nd")
        {
            _second = !_second;
            _alpha = false;
            return;
        }
        if (key.Id == "alpha")
        {
            _alpha = !_alpha;
            _second = false;
            return;
        }

        if (_second && key.Second is not null)
        {
            _second = false;
            PressSecond(ctx, key.Id);
            return;
        }
        if (_alpha && key.Alpha is { } letter)
        {
            _alpha = false;
            PressLetter(ctx, letter);
            return;
        }
        _second = false;
        _alpha = false;
        PressPrimary(ctx, key.Id);
    }

    private void PressLetter(OsAppContext ctx, string letter)
    {
        if (_session.StorePending)
        {
            _session.StoreInto(letter, ctx.Localize);
            return;
        }
        _session.Insert(letter);
    }

    private void PressPrimary(OsAppContext ctx, string id)
    {
        switch (id)
        {
            case "mode":
                _session.ToggleAngle();
                break;
            case "del":
                _session.Backspace();
                break;
            case "clear":
                if (_session.Entry.Length > 0)
                {
                    _session.Entry = string.Empty;
                    _session.SetStatus(null, false);
                }
                else
                {
                    _session.ClearHistory();
                }
                _session.StorePending = false;
                break;
            case "ye":
                _nav(CalcNav.YEditor);
                break;
            case "window":
                _nav(CalcNav.Window);
                break;
            case "zoom":
                _nav(CalcNav.Zoom);
                break;
            case "trace":
                _nav(CalcNav.Trace);
                break;
            case "graph":
                _nav(CalcNav.Graph);
                break;
            case "sqr":
                _session.Insert("^2");
                break;
            case "inv":
                _session.Insert("^(-1)");
                break;
            case "sin":
                _session.Insert("sin(");
                break;
            case "cos":
                _session.Insert("cos(");
                break;
            case "tan":
                _session.Insert("tan(");
                break;
            case "pow":
                _session.Insert("^");
                break;
            case "log":
                _session.Insert("log(");
                break;
            case "ln":
                _session.Insert("ln(");
                break;
            case "lpar":
                _session.Insert("(");
                break;
            case "rpar":
                _session.Insert(")");
                break;
            case "sto":
                _session.StorePending = true;
                _session.SetStatus(ctx.Localize("os.calc_sto_prompt"), false);
                break;
            case "div":
                _session.Insert("/");
                break;
            case "mul":
                _session.Insert("*");
                break;
            case "sub":
                _session.Insert("-");
                break;
            case "add":
                _session.Insert("+");
                break;
            case "comma":
                _session.Insert(",");
                break;
            case "pi":
                _session.Insert("pi");
                break;
            case "neg":
                _session.Insert("-");
                break;
            case "dot":
                _session.Insert(".");
                break;
            case "enter":
                _session.Submit(ctx.Localize);
                break;
            default:
                if (id.Length == 2 && id[0] == 'd')
                {
                    _session.Insert(id[1].ToString());
                }
                break;
        }
    }

    private void PressSecond(OsAppContext ctx, string id)
    {
        switch (id)
        {
            case "window":
                _nav(CalcNav.TblSet);
                break;
            case "trace":
                _nav(CalcNav.CalcMenu);
                break;
            case "graph":
                _nav(CalcNav.Table);
                break;
            case "sqr":
                _session.Insert("sqrt(");
                break;
            case "inv":
                _session.Insert("abs(");
                break;
            case "sin":
                _session.Insert("asin(");
                break;
            case "cos":
                _session.Insert("acos(");
                break;
            case "tan":
                _session.Insert("atan(");
                break;
            case "log":
                _session.Insert("10^(");
                break;
            case "ln":
                _session.Insert("e^(");
                break;
            case "pi":
                _session.Insert("e");
                break;
            case "neg":
                _session.Insert("Ans");
                break;
        }
    }
}
