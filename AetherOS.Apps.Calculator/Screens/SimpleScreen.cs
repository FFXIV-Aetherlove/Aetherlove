using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>The everyday calculator: the four operations, a percent key and the three roots and powers most
/// people actually reach for, on the familiar six by four grid.
///
/// It runs the same engine and writes to the same tape as the graphing keypad, so a switch between the two
/// keeps the history and the last answer. There is no 2nd or ALPHA layer here on purpose: every key does the
/// one thing printed on it.</summary>
internal sealed class SimpleScreen
{
    /// <summary>Face, what it types, and how it is tinted. A null insert means the key is an action, handled
    /// by <see cref="Press"/> under its id.</summary>
    private readonly record struct SimpleKey(string Id, string Face, string? Insert, KeyTone Tone);

    private static readonly SimpleKey[][] Rows =
    [
        [
            new SimpleKey("pct", "%", "%", KeyTone.Nav),
            new SimpleKey("ce", "CE", null, KeyTone.Nav),
            new SimpleKey("clear", "C", null, KeyTone.Nav),
            new SimpleKey("del", "DEL", null, KeyTone.Nav),
        ],
        [
            new SimpleKey("inv", "1/x", "^(-1)", KeyTone.Nav),
            new SimpleKey("sqr", "x²", "^2", KeyTone.Nav),
            new SimpleKey("sqrt", "sqrt", "sqrt(", KeyTone.Nav),
            new SimpleKey("div", "÷", "/", KeyTone.Operator),
        ],
        [
            new SimpleKey("d7", "7", "7", KeyTone.Digit),
            new SimpleKey("d8", "8", "8", KeyTone.Digit),
            new SimpleKey("d9", "9", "9", KeyTone.Digit),
            new SimpleKey("mul", "×", "*", KeyTone.Operator),
        ],
        [
            new SimpleKey("d4", "4", "4", KeyTone.Digit),
            new SimpleKey("d5", "5", "5", KeyTone.Digit),
            new SimpleKey("d6", "6", "6", KeyTone.Digit),
            new SimpleKey("sub", "-", "-", KeyTone.Operator),
        ],
        [
            new SimpleKey("d1", "1", "1", KeyTone.Digit),
            new SimpleKey("d2", "2", "2", KeyTone.Digit),
            new SimpleKey("d3", "3", "3", KeyTone.Digit),
            new SimpleKey("add", "+", "+", KeyTone.Operator),
        ],
        [
            new SimpleKey("lpar", "(", "(", KeyTone.Nav),
            new SimpleKey("d0", "0", "0", KeyTone.Digit),
            new SimpleKey("dot", ".", ".", KeyTone.Digit),
            new SimpleKey("enter", "=", null, KeyTone.Accent),
        ],
    ];

    private readonly CalcSession _session;
    private int _tapeCount = -1;

    public SimpleScreen(CalcSession session) => _session = session;

    public void OnShow()
    {
    }

    public void Draw(OsAppContext ctx)
    {
        ReadKeyboard(ctx);
        var top = ImGui.GetCursorScreenPos();
        var region = ImGui.GetContentRegionAvail();
        var padX = ctx.Px(8f);
        var lcdH = Math.Clamp(region.Y * 0.30f, ctx.Px(104f), ctx.Px(180f));
        var lcdTL = new Vector2(top.X + padX + ctx.Px(3f), top.Y + ctx.Px(3f));
        var lcdSize = new Vector2(region.X - (padX + ctx.Px(3f)) * 2f, lcdH);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(top, top + region, ImGui.ColorConvertFloat4ToU32(DeviceUi.Chassis));
        dl.AddLine(top, new Vector2(top.X + region.X, top.Y),
            ImGui.ColorConvertFloat4ToU32(DeviceUi.ChassisEdge), ctx.Px(1f));

        DeviceUi.Lcd(ctx, dl, lcdTL, lcdSize);
        DrawLcdContents(ctx, lcdTL, lcdSize);

        var keypadTop = lcdTL.Y + lcdSize.Y + ctx.Px(12f);
        var keypadH = top.Y + region.Y - keypadTop - ctx.Px(6f);
        DrawKeypad(ctx, new Vector2(top.X + padX, keypadTop),
            new Vector2(region.X - padX * 2f, MathF.Max(ctx.Px(140f), keypadH)));
    }

    private void DrawLcdContents(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var pad = ctx.Px(7f);
        var statusH = _session.Status is null ? 0f : ImGui.GetTextLineHeight() + ctx.Px(2f);
        var entryH = ImGui.GetTextLineHeight() + ctx.Px(6f);
        var tapeTL = new Vector2(tl.X + pad, tl.Y + pad);
        var tapeSize = new Vector2(size.X - pad * 2f,
            MathF.Max(ctx.Px(20f), size.Y - entryH - statusH - pad * 2f - ctx.Px(4f)));

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

    private void DrawTape(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        using var tape = ImRaii.Child("##calcSimpleTape", size, false,
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
        var submitted = ImGui.InputText("##calcSimpleEntry", ref _session.Entry, CalcSession.EntryMaxLength,
            ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }
        if (submitted)
        {
            _session.Submit(ctx.Localize);
        }
    }

    private void DrawKeypad(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var gap = ctx.Px(5f);
        var columns = 4;
        var rows = Rows.Length;
        var keyW = (size.X - gap * (columns - 1)) / columns;
        var rowH = (size.Y - gap * (rows - 1)) / rows;

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var key = Rows[r][c];
                var pos = new Vector2(tl.X + c * (keyW + gap), tl.Y + r * (rowH + gap));
                if (DeviceUi.Key(ctx, $"##calcSimpleKey{key.Id}", pos, new Vector2(keyW, rowH), key.Face,
                    null, null, key.Tone, false))
                {
                    Press(ctx, key);
                }
            }
        }
    }

    /// <summary>Number-row and numpad typing while this screen is up. Polling captures the keyboard from
    /// the game for as long as the calculator shows, which is exactly what typing into it means; the
    /// service already stands down while a game text field is open.</summary>
    private void ReadKeyboard(OsAppContext ctx)
    {
        var keys = ctx.Capabilities.Keyboard;
        ReadOnlySpan<(AppKey Key, string Insert)> inserts =
        [
            (AppKey.D1, "1"), (AppKey.D2, "2"), (AppKey.D3, "3"), (AppKey.D4, "4"), (AppKey.D5, "5"),
            (AppKey.D6, "6"), (AppKey.D7, "7"), (AppKey.D8, "8"), (AppKey.D9, "9"), (AppKey.D0, "0"),
            (AppKey.Num1, "1"), (AppKey.Num2, "2"), (AppKey.Num3, "3"), (AppKey.Num4, "4"), (AppKey.Num5, "5"),
            (AppKey.Num6, "6"), (AppKey.Num7, "7"), (AppKey.Num8, "8"), (AppKey.Num9, "9"), (AppKey.Num0, "0"),
            (AppKey.NumAdd, "+"), (AppKey.NumSub, "-"), (AppKey.NumMul, "*"), (AppKey.NumDiv, "/"),
            (AppKey.NumDecimal, "."),
        ];
        foreach (var (key, insert) in inserts)
        {
            if (keys.WasPressed(key))
            {
                _session.Insert(insert);
            }
        }
        if (keys.WasPressed(AppKey.Enter) || keys.WasPressed(AppKey.NumEnter))
        {
            _session.Submit(ctx.Localize);
        }
        if (keys.WasPressed(AppKey.Backspace))
        {
            _session.Backspace();
        }
    }

    private void Press(OsAppContext ctx, SimpleKey key)
    {
        switch (key.Id)
        {
            case "ce":
                _session.Entry = string.Empty;
                _session.SetStatus(null, false);
                break;
            case "clear":
                _session.Entry = string.Empty;
                _session.SetStatus(null, false);
                _session.ClearHistory();
                break;
            case "del":
                _session.Backspace();
                break;
            case "enter":
                _session.Submit(ctx.Localize);
                break;
            default:
                if (key.Insert is { } insert)
                {
                    _session.Insert(insert);
                }
                break;
        }
    }
}
