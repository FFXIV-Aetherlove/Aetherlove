using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>The table view: TBLSET picks where the column of x starts and how far it steps, and every
/// enabled Y= slot gets a column beside it.</summary>
internal sealed class TableScreen
{
    private const int Rows = 200;

    private readonly CalcSession _session;
    private readonly Action<CalcNav> _nav;
    private string _startBuffer = "0";
    private string _stepBuffer = "1";
    private bool _setupOpen;
    private string? _setupError;

    public TableScreen(CalcSession session, Action<CalcNav> nav)
    {
        _session = session;
        _nav = nav;
    }

    public void OnShow()
    {
        foreach (var fn in _session.Functions)
        {
            fn.Recompile();
        }
    }

    public void OpenSetup()
    {
        _setupOpen = true;
        _startBuffer = CalcFormat.Number(_session.TableStart);
        _stepBuffer = CalcFormat.Number(_session.TableStep);
        _setupError = null;
    }

    public void Draw(OsAppContext ctx)
    {
        var top = ImGui.GetCursorScreenPos();
        var region = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(top, top + region, ImGui.ColorConvertFloat4ToU32(DeviceUi.Chassis));

        var padX = ctx.Px(8f);
        var stripH = ctx.Px(26f);
        DrawToolStrip(ctx, new Vector2(top.X + padX, top.Y + ctx.Px(6f)),
            new Vector2(region.X - padX * 2f, stripH));

        var lcdTL = new Vector2(top.X + padX + ctx.Px(3f), top.Y + ctx.Px(6f) + stripH + ctx.Px(10f));
        var lcdSize = new Vector2(region.X - (padX + ctx.Px(3f)) * 2f,
            MathF.Max(ctx.Px(80f), top.Y + region.Y - lcdTL.Y - ctx.Px(8f)));
        DeviceUi.Lcd(ctx, dl, lcdTL, lcdSize);
        DrawGrid(ctx, lcdTL, lcdSize);

        if (_setupOpen)
        {
            DeviceUi.Overlay(ctx, "calcTblSet", ctx.Px(240f), ctx.Px(196f), DrawSetup(ctx),
                () => _setupOpen = false);
        }
    }

    private void DrawToolStrip(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var gap = ctx.Px(6f);
        var w = (size.X - gap * 2f) / 3f;
        var cell = new Vector2(w, size.Y);
        if (DeviceUi.Pill(ctx, "##tblSetup", tl, cell, ctx.Localize("os.calc_view_tblset"), _setupOpen, DeviceUi.Teal))
        {
            if (_setupOpen)
            {
                _setupOpen = false;
            }
            else
            {
                OpenSetup();
            }
        }
        if (DeviceUi.Pill(ctx, "##tblGraph", tl + new Vector2(w + gap, 0f), cell, ctx.Localize("os.calc_view_graph"), false, DeviceUi.Teal))
        {
            _nav(CalcNav.Graph);
        }
        if (DeviceUi.Pill(ctx, "##tblHome", tl + new Vector2((w + gap) * 2f, 0f), cell,
            ctx.Localize("os.calc_view_home"), false, DeviceUi.Teal))
        {
            _nav(CalcNav.Home);
        }
    }

    private void DrawGrid(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var pad = ctx.Px(6f);
        var innerTL = tl + new Vector2(pad, pad);
        var innerSize = size - new Vector2(pad * 2f, pad * 2f);

        var columns = 1;
        foreach (var fn in _session.Functions)
        {
            if (fn.Plotted)
            {
                columns++;
            }
        }
        if (columns == 1)
        {
            ImGui.SetCursorScreenPos(innerTL);
            ImGui.PushTextWrapPos(innerTL.X + innerSize.X - ImGui.GetWindowPos().X);
            ImGui.TextColored(RetroLcd.Pixel with { W = 0.7f }, ctx.Localize("os.calc_table_empty"));
            ImGui.PopTextWrapPos();
            return;
        }

        var colW = innerSize.X / columns;
        var gutter = ctx.Px(4f);
        var headerH = ImGui.GetTextLineHeight() + ctx.Px(4f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(innerTL + new Vector2(0f, headerH), innerTL + new Vector2(innerSize.X, headerH),
            DeviceUi.Ink(0.6f), ctx.Px(1f));

        var index = 0;
        DrawHeaderCell(dl, innerTL, colW, index, "x", RetroLcd.Pixel, gutter);
        index++;
        foreach (var fn in _session.Functions)
        {
            if (!fn.Plotted)
            {
                continue;
            }
            DrawHeaderCell(dl, innerTL, colW, index, fn.Label, fn.Color, gutter);
            index++;
        }

        ImGui.SetCursorScreenPos(innerTL + new Vector2(0f, headerH + ctx.Px(2f)));
        using var body = ImRaii.Child("##calcTableBody",
            new Vector2(innerSize.X, innerSize.Y - headerH - ctx.Px(2f)), false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        if (!body)
        {
            return;
        }
        var childTL = ImGui.GetCursorScreenPos();
        var lineH = ImGui.GetTextLineHeight() + ctx.Px(3f);
        var bodyDl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(innerSize.X, Rows * lineH));

        var scroll = ImGui.GetScrollY();
        var first = Math.Max(0, (int)(scroll / lineH) - 1);
        var last = Math.Min(Rows - 1, (int)((scroll + ImGui.GetWindowSize().Y) / lineH) + 1);
        for (var row = first; row <= last; row++)
        {
            var x = _session.TableStart + (_session.TableStep * row);
            var rowTL = childTL + new Vector2(0f, row * lineH);
            DrawCell(bodyDl, rowTL, colW, 0, CalcFormat.Axis(x), RetroLcd.Pixel with { W = 0.8f }, gutter);
            var column = 1;
            foreach (var fn in _session.Functions)
            {
                if (!fn.Plotted)
                {
                    continue;
                }
                var text = _session.TrySample(fn, x, out var y)
                    ? CalcFormat.Axis(y)
                    : "-";
                DrawCell(bodyDl, rowTL, colW, column, text, fn.Color, gutter);
                column++;
            }
        }
    }

    private static void DrawHeaderCell(ImDrawListPtr dl, Vector2 tl, float colW, int column, string text,
        Vector4 color, float gutter)
    {
        var sz = ImGui.CalcTextSize(text);
        var pos = new Vector2(tl.X + colW * (column + 1) - sz.X - gutter, tl.Y);
        dl.AddText(pos, ImGui.ColorConvertFloat4ToU32(color), text);
    }

    private static void DrawCell(ImDrawListPtr dl, Vector2 rowTL, float colW, int column, string text,
        Vector4 color, float gutter)
    {
        var sz = ImGui.CalcTextSize(text);
        var pos = new Vector2(rowTL.X + colW * (column + 1) - sz.X - gutter, rowTL.Y);
        dl.AddText(pos, ImGui.ColorConvertFloat4ToU32(color), text);
    }

    private Action<Vector2, Vector2> DrawSetup(OsAppContext ctx) => (tl, size) =>
    {
        if (DeviceUi.PanelHeader(ctx, "calcTblSet", tl, size, ctx.Localize("os.calc_tblset_title")))
        {
            _setupOpen = false;
        }
        var pad = ctx.Px(14f);
        var fieldW = size.X - pad * 2f;
        ImGui.SetCursorScreenPos(new Vector2(tl.X + pad, tl.Y + pad + ImGui.GetTextLineHeight() + ctx.Px(8f)));
        using (ImRaii.Group())
        {
            DeviceUi.NumberField("##calcTblStart", ctx.Localize("os.calc_tblset_start"), ref _startBuffer,
                fieldW);
            DeviceUi.NumberField("##calcTblStep", ctx.Localize("os.calc_tblset_step"), ref _stepBuffer,
                fieldW);
        }
        if (_setupError is { } error)
        {
            ImGui.TextColored(UiColors.Danger, error);
        }

        var btnH = ctx.Px(28f);
        if (DeviceUi.Pill(ctx, "##calcTblApply", new Vector2(tl.X + pad, tl.Y + size.Y - pad - btnH),
            new Vector2(fieldW, btnH), ctx.Localize("os.calc_window_apply"), true, DeviceUi.Teal))
        {
            ApplySetup(ctx);
        }
    };

    private void ApplySetup(OsAppContext ctx)
    {
        if (!CalcFormat.TryParse(_startBuffer, out var start) || !CalcFormat.TryParse(_stepBuffer, out var step))
        {
            _setupError = ctx.Localize("os.calc_err_syntax");
            return;
        }
        if (Math.Abs(step) < 1e-9d)
        {
            _setupError = ctx.Localize("os.calc_tblset_bad");
            return;
        }
        _session.TableStart = start;
        _session.TableStep = step;
        _setupError = null;
        _setupOpen = false;
    }
}
