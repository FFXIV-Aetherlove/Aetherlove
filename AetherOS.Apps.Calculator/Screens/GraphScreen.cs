using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Apps.Calculator.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>Which panel is floating over the plot.</summary>
internal enum GraphPanel
{
    None,
    YEditor,
    Window,
    Zoom,
    Calc,
}

/// <summary>The graph view: the plot itself, panning and scroll zoom, the trace cursor, and the Y=, WINDOW,
/// ZOOM and CALC panels that float over it.</summary>
internal sealed class GraphScreen
{
    private const int TraceSamples = 160;

    private readonly CalcSession _session;
    private readonly Action<CalcNav> _nav;
    private readonly string[] _windowBuffer = ["-10", "10", "-10", "10"];
    private GraphPanel _panel;
    private bool _trace;
    private int _traceIndex = TraceSamples / 2;
    private int _traceSlot;
    private Vector2 _plotSize = Vector2.One;
    private CalcOp _op = CalcOp.Root;
    private int _opFirst;
    private int _opSecond = 1;
    private string _opLower = "-10";
    private string _opUpper = "10";
    private string _opAt = "0";
    private string? _windowError;

    public GraphScreen(CalcSession session, Action<CalcNav> nav)
    {
        _session = session;
        _nav = nav;
    }

    private enum CalcOp
    {
        Root,
        Minimum,
        Maximum,
        Intersect,
        Derivative,
        Integral,
    }

    public void OnShow()
    {
        foreach (var fn in _session.Functions)
        {
            fn.Recompile();
        }
    }

    public void OpenPanel(GraphPanel panel)
    {
        _panel = panel;
        if (panel == GraphPanel.Window)
        {
            LoadWindowBuffer();
        }
        if (panel == GraphPanel.Calc)
        {
            _opLower = CalcFormat.Number(_session.Window.XMin);
            _opUpper = CalcFormat.Number(_session.Window.XMax);
        }
    }

    public void ToggleTrace()
    {
        _trace = !_trace;
        if (_trace)
        {
            _panel = GraphPanel.None;
            EnsureTraceSlot();
        }
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

        var readoutH = ctx.Px(24f);
        var plotTL = new Vector2(top.X + padX + ctx.Px(3f), top.Y + ctx.Px(6f) + stripH + ctx.Px(10f));
        var plotSize = new Vector2(region.X - (padX + ctx.Px(3f)) * 2f,
            MathF.Max(ctx.Px(80f), top.Y + region.Y - plotTL.Y - readoutH - ctx.Px(12f)));
        _plotSize = plotSize;

        if (!_session.Window.Valid)
        {
            _session.Window = GraphWindow.Standard;
        }
        GraphPlot.DrawFrame(ctx, dl, plotTL, plotSize, _session.Window);
        foreach (var fn in _session.Functions)
        {
            if (fn.Plotted)
            {
                GraphPlot.DrawCurve(ctx, dl, plotTL, plotSize, _session, fn, _session.Window);
            }
        }

        if (!_session.AnyPlotted())
        {
            DrawEmptyPlot(ctx, dl, plotTL, plotSize);
        }

        HandlePlotInput(ctx, plotTL, plotSize);
        DrawTraceLayer(ctx, dl, plotTL, plotSize);
        DrawReadout(ctx, dl, new Vector2(top.X + padX, plotTL.Y + plotSize.Y + ctx.Px(6f)),
            new Vector2(region.X - padX * 2f, readoutH));

        DrawPanel(ctx);
    }

    private void DrawToolStrip(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        var gap = ctx.Px(4f);
        var count = 6;
        var w = (size.X - gap * (count - 1)) / count;
        var cell = new Vector2(w, size.Y);
        var accent = DeviceUi.Teal;

        if (DeviceUi.Pill(ctx, "##gToolY", tl, cell, "Y=", _panel == GraphPanel.YEditor, accent))
        {
            _panel = _panel == GraphPanel.YEditor ? GraphPanel.None : GraphPanel.YEditor;
        }
        if (DeviceUi.Pill(ctx, "##gToolWin", tl + new Vector2(w + gap, 0f), cell, ctx.Localize("os.calc_window_title"),
            _panel == GraphPanel.Window, accent))
        {
            if (_panel == GraphPanel.Window)
            {
                _panel = GraphPanel.None;
            }
            else
            {
                OpenPanel(GraphPanel.Window);
            }
        }
        if (DeviceUi.Pill(ctx, "##gToolZoom", tl + new Vector2((w + gap) * 2f, 0f), cell, ctx.Localize("os.calc_zoom_title"),
            _panel == GraphPanel.Zoom, accent))
        {
            _panel = _panel == GraphPanel.Zoom ? GraphPanel.None : GraphPanel.Zoom;
        }
        if (DeviceUi.Pill(ctx, "##gToolTrace", tl + new Vector2((w + gap) * 3f, 0f), cell, ctx.Localize("os.calc_view_trace"), _trace, accent))
        {
            ToggleTrace();
        }
        if (DeviceUi.Pill(ctx, "##gToolCalc", tl + new Vector2((w + gap) * 4f, 0f), cell, ctx.Localize("os.calc_calc_title"),
            _panel == GraphPanel.Calc, accent))
        {
            if (_panel == GraphPanel.Calc)
            {
                _panel = GraphPanel.None;
            }
            else
            {
                OpenPanel(GraphPanel.Calc);
            }
        }
        if (DeviceUi.Pill(ctx, "##gToolHome", tl + new Vector2((w + gap) * 5f, 0f), cell,
            ctx.Localize("os.calc_view_home"), false, accent))
        {
            _nav(CalcNav.Home);
        }
    }

    /// <summary>An empty graph used to say only that it was empty, which left "so how do I plot something"
    /// unanswered on the one screen where it is asked. It now spells out the three keys and offers the first
    /// of them as a button, submitted before the pan and zoom target beneath it so the button wins the click.</summary>
    private void DrawEmptyPlot(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size)
    {
        var wrap = size.X - ctx.Px(28f);
        var title = ctx.Localize("os.calc_graph_no_functions");
        var steps = ctx.Localize("os.calc_graph_how_to");
        var titleSz = ImGui.CalcTextSize(title, false, wrap);
        var stepsSz = ImGui.CalcTextSize(steps, false, wrap);

        var btnLabel = ctx.Localize("os.calc_graph_add_function");
        var btnSz = ImGui.CalcTextSize(btnLabel);
        var btnH = btnSz.Y + ctx.Px(12f);
        var btnW = btnSz.X + ctx.Px(28f);

        var blockH = titleSz.Y + ctx.Px(6f) + stepsSz.Y + ctx.Px(12f) + btnH;
        var y = tl.Y + (size.Y - blockH) * 0.5f;
        var centerX = tl.X + size.X * 0.5f;

        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(centerX - titleSz.X * 0.5f, y),
            DeviceUi.Ink(0.85f), title, wrap);
        y += titleSz.Y + ctx.Px(6f);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(centerX - stepsSz.X * 0.5f, y),
            DeviceUi.Ink(0.6f), steps, wrap);
        y += stepsSz.Y + ctx.Px(12f);

        var btnTL = new Vector2(centerX - btnW * 0.5f, y);
        ImGui.SetCursorScreenPos(btnTL);
        var clicked = ImGui.InvisibleButton("##calcGraphAdd", new Vector2(btnW, btnH));
        SharedUiHelpers.HandOnHover();
        var hovered = ImGui.IsItemHovered();
        dl.AddRectFilled(btnTL, btnTL + new Vector2(btnW, btnH),
            ImGui.GetColorU32(DeviceUi.Teal with { W = hovered ? 0.34f : 0.22f }), btnH * 0.5f);
        dl.AddRect(btnTL, btnTL + new Vector2(btnW, btnH), ImGui.GetColorU32(DeviceUi.Teal with { W = 0.7f }),
            btnH * 0.5f, ImDrawFlags.None, ctx.Px(1f));
        dl.AddText(btnTL + new Vector2(ctx.Px(14f), ctx.Px(6f)), ImGui.GetColorU32(DeviceUi.Teal), btnLabel);
        if (clicked)
        {
            OpenPanel(GraphPanel.YEditor);
        }
    }

    private void HandlePlotInput(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        if (_panel != GraphPanel.None)
        {
            return;
        }
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton("##calcPlot", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var w = _session.Window;
        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta.X != 0f || delta.Y != 0f)
            {
                var dx = delta.X / size.X * w.Width;
                var dy = delta.Y / size.Y * w.Height;
                w.XMin -= dx;
                w.XMax -= dx;
                w.YMin += dy;
                w.YMax += dy;
                _session.Window = w;
            }
        }

        if (!hovered)
        {
            return;
        }
        var wheel = ImGui.GetIO().MouseWheel;
        if (wheel == 0f)
        {
            return;
        }
        var factor = wheel > 0f ? 1f / 1.18f : 1.18f;
        var mouse = ImGui.GetIO().MousePos;
        var ax = GraphPlot.ScreenToX(w, tl, size, mouse.X);
        var ay = GraphPlot.ScreenToY(w, tl, size, mouse.Y);
        w.XMin = ax + (w.XMin - ax) * factor;
        w.XMax = ax + (w.XMax - ax) * factor;
        w.YMin = ay + (w.YMin - ay) * factor;
        w.YMax = ay + (w.YMax - ay) * factor;
        if (w.Valid)
        {
            _session.Window = w;
        }
    }

    private void DrawTraceLayer(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size)
    {
        if (!_trace)
        {
            return;
        }
        EnsureTraceSlot();
        var fn = _session.Functions[_traceSlot];
        if (!fn.Plotted)
        {
            return;
        }
        var w = _session.Window;
        var x = w.XMin + (w.Width * _traceIndex / TraceSamples);
        if (!_session.TrySample(fn, x, out var y))
        {
            return;
        }
        GraphPlot.DrawTraceCursor(ctx, dl, tl, size, w, GraphPlot.ToScreen(w, tl, size, x, y), fn.Color);
    }

    private void DrawReadout(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size)
    {
        if (_trace)
        {
            DrawTraceReadout(ctx, dl, tl, size);
            return;
        }
        var text = _session.Status ?? ctx.Localize("os.calc_trace_hint");
        var color = _session.Status is null
            ? UiColors.Hint
            : _session.StatusIsError ? UiColors.Danger : DeviceUi.Teal;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f, tl + new Vector2(0f, ctx.Px(4f)),
            ImGui.ColorConvertFloat4ToU32(color), text, size.X);
    }

    private void DrawTraceReadout(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size)
    {
        var btn = MathF.Min(size.Y, ctx.Px(24f));
        if (DeviceUi.IconButton(ctx, "##calcTracePrev", tl, btn, FontAwesomeIcon.ChevronLeft, UiColors.Body))
        {
            _traceIndex = Math.Max(0, _traceIndex - 1);
        }
        if (DeviceUi.IconButton(ctx, "##calcTraceNext", tl + new Vector2(btn + ctx.Px(4f), 0f), btn,
            FontAwesomeIcon.ChevronRight, UiColors.Body))
        {
            _traceIndex = Math.Min(TraceSamples, _traceIndex + 1);
        }

        var fn = _session.Functions[_traceSlot];
        var slotW = ctx.Px(38f);
        var slotTL = tl + new Vector2((btn + ctx.Px(4f)) * 2f, 0f);
        if (DeviceUi.Pill(ctx, "##calcTraceSlot", slotTL, new Vector2(slotW, btn), fn.Label, true, fn.Chip))
        {
            CycleTraceSlot();
        }

        var w = _session.Window;
        var x = w.XMin + (w.Width * _traceIndex / TraceSamples);
        var text = _session.TrySample(fn, x, out var y)
            ? $"x={CalcFormat.Axis(x)}  y={CalcFormat.Axis(y)}"
            : $"x={CalcFormat.Axis(x)}  y={ctx.Localize("os.calc_err_undefined")}";
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.9f,
            slotTL + new Vector2(slotW + ctx.Px(8f), ctx.Px(3f)),
            ImGui.ColorConvertFloat4ToU32(UiColors.Body), text);
    }

    private void CycleTraceSlot()
    {
        for (var i = 1; i <= CalcSession.FunctionCount; i++)
        {
            var next = (_traceSlot + i) % CalcSession.FunctionCount;
            if (_session.Functions[next].Plotted)
            {
                _traceSlot = next;
                return;
            }
        }
    }

    private void EnsureTraceSlot()
    {
        if (_session.Functions[_traceSlot].Plotted)
        {
            return;
        }
        for (var i = 0; i < CalcSession.FunctionCount; i++)
        {
            if (_session.Functions[i].Plotted)
            {
                _traceSlot = i;
                return;
            }
        }
    }

    private void DrawPanel(OsAppContext ctx)
    {
        switch (_panel)
        {
            case GraphPanel.YEditor:
                DeviceUi.Overlay(ctx, "calcY", ctx.Px(280f), ctx.Px(250f), DrawYEditor(ctx), ClosePanel);
                break;
            case GraphPanel.Window:
                DeviceUi.Overlay(ctx, "calcWin", ctx.Px(250f), ctx.Px(268f), DrawWindowPanel(ctx), ClosePanel);
                break;
            case GraphPanel.Zoom:
                DeviceUi.Overlay(ctx, "calcZoom", ctx.Px(250f), ctx.Px(226f), DrawZoomPanel(ctx), ClosePanel);
                break;
            case GraphPanel.Calc:
                DeviceUi.Overlay(ctx, "calcCalc", ctx.Px(292f), ctx.Px(320f), DrawCalcPanel(ctx), ClosePanel);
                break;
        }
    }

    private void ClosePanel()
    {
        _panel = GraphPanel.None;
        _windowError = null;
    }

    private Action<Vector2, Vector2> DrawYEditor(OsAppContext ctx) => (tl, size) =>
    {
        if (DeviceUi.PanelHeader(ctx, "calcY", tl, size, ctx.Localize("os.calc_y_title")))
        {
            ClosePanel();
        }
        var pad = ctx.Px(14f);
        var y = tl.Y + pad + ImGui.GetTextLineHeight() + ctx.Px(10f);
        var rowH = ctx.Px(30f);
        var toggle = ctx.Px(20f);

        for (var i = 0; i < CalcSession.FunctionCount; i++)
        {
            var fn = _session.Functions[i];
            var rowTL = new Vector2(tl.X + pad, y);
            if (DeviceUi.Pill(ctx, $"##calcYOn{i}", rowTL, new Vector2(toggle, toggle), string.Empty,
                fn.Enabled, fn.Chip))
            {
                fn.Enabled = !fn.Enabled;
            }
            var labelPos = rowTL + new Vector2(toggle + ctx.Px(6f), ctx.Px(2f));
            ImGui.GetWindowDrawList().AddText(labelPos,
                ImGui.ColorConvertFloat4ToU32(fn.Enabled ? UiColors.Body : UiColors.Hint), $"{fn.Label}=");

            var fieldX = labelPos.X + ctx.Px(30f);
            ImGui.SetCursorScreenPos(new Vector2(fieldX, rowTL.Y - ctx.Px(1f)));
            ImGui.SetNextItemWidth(tl.X + size.X - pad - fieldX);
            var buffer = fn.Source;
            if (ImGui.InputText($"##calcYSrc{i}", ref buffer, 200))
            {
                fn.Source = buffer;
                fn.Recompile();
            }
            if (fn.Error is { } error)
            {
                ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
                    new Vector2(fieldX, rowTL.Y + ctx.Px(19f)),
                    ImGui.ColorConvertFloat4ToU32(UiColors.Danger), CalcErrorText.Text(ctx.Localize, error));
            }
            y += rowH + ctx.Px(6f);
        }

        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(tl.X + pad, y + ctx.Px(2f)), ImGui.ColorConvertFloat4ToU32(UiColors.Hint),
            ctx.Localize("os.calc_y_hint"), size.X - pad * 2f);
    };

    private Action<Vector2, Vector2> DrawWindowPanel(OsAppContext ctx) => (tl, size) =>
    {
        if (DeviceUi.PanelHeader(ctx, "calcWin", tl, size, ctx.Localize("os.calc_window_title")))
        {
            ClosePanel();
        }
        var pad = ctx.Px(14f);
        var fieldW = size.X - pad * 2f;
        ImGui.SetCursorScreenPos(new Vector2(tl.X + pad, tl.Y + pad + ImGui.GetTextLineHeight() + ctx.Px(8f)));

        using (ImRaii.Group())
        {
            DeviceUi.NumberField("##calcWinXMin", ctx.Localize("os.calc_window_xmin"),
                ref _windowBuffer[0], fieldW);
            DeviceUi.NumberField("##calcWinXMax", ctx.Localize("os.calc_window_xmax"),
                ref _windowBuffer[1], fieldW);
            DeviceUi.NumberField("##calcWinYMin", ctx.Localize("os.calc_window_ymin"),
                ref _windowBuffer[2], fieldW);
            DeviceUi.NumberField("##calcWinYMax", ctx.Localize("os.calc_window_ymax"),
                ref _windowBuffer[3], fieldW);
        }

        if (_windowError is { } error)
        {
            ImGui.TextColored(UiColors.Danger, error);
        }

        var btnH = ctx.Px(28f);
        var btnTL = new Vector2(tl.X + pad, tl.Y + size.Y - pad - btnH);
        var half = (fieldW - ctx.Px(8f)) * 0.5f;
        if (DeviceUi.Pill(ctx, "##calcWinApply", btnTL, new Vector2(half, btnH),
            ctx.Localize("os.calc_window_apply"), true, DeviceUi.Teal))
        {
            ApplyWindowBuffer(ctx);
        }
        if (DeviceUi.Pill(ctx, "##calcWinReset", btnTL + new Vector2(half + ctx.Px(8f), 0f),
            new Vector2(half, btnH), ctx.Localize("os.calc_zoom_standard"), false, DeviceUi.Teal))
        {
            _session.Window = GraphWindow.Standard;
            LoadWindowBuffer();
            _windowError = null;
        }
    };

    private Action<Vector2, Vector2> DrawZoomPanel(OsAppContext ctx) => (tl, size) =>
    {
        if (DeviceUi.PanelHeader(ctx, "calcZoom", tl, size, ctx.Localize("os.calc_zoom_title")))
        {
            ClosePanel();
        }
        var pad = ctx.Px(14f);
        var btnH = ctx.Px(28f);
        var gap = ctx.Px(8f);
        var colW = (size.X - pad * 2f - gap) * 0.5f;
        var y = tl.Y + pad + ImGui.GetTextLineHeight() + ctx.Px(10f);

        string[] labels =
        [
            ctx.Localize("os.calc_zoom_standard"),
            ctx.Localize("os.calc_zoom_trig"),
            ctx.Localize("os.calc_zoom_square"),
            ctx.Localize("os.calc_zoom_fit"),
            ctx.Localize("os.calc_zoom_in"),
            ctx.Localize("os.calc_zoom_out"),
        ];
        for (var i = 0; i < labels.Length; i++)
        {
            var pos = new Vector2(tl.X + pad + (i % 2) * (colW + gap), y + (i / 2) * (btnH + gap));
            if (DeviceUi.Pill(ctx, $"##calcZoom{i}", pos, new Vector2(colW, btnH), labels[i], false,
                DeviceUi.Teal))
            {
                ApplyZoom(i);
                ClosePanel();
            }
        }
    };

    private void ApplyZoom(int index)
    {
        var w = _session.Window;
        switch (index)
        {
            case 0:
                w = GraphWindow.Standard;
                break;
            case 1:
                var span = _session.Env.Angle == AngleMode.Degrees ? 360d : Math.PI * 2d;
                w.XMin = -span;
                w.XMax = span;
                w.YMin = -4d;
                w.YMax = 4d;
                break;
            case 2:
                var aspect = _plotSize.Y / MathF.Max(1f, _plotSize.X);
                var half = w.Width * aspect * 0.5d;
                var center = (w.YMin + w.YMax) * 0.5d;
                w.YMin = center - half;
                w.YMax = center + half;
                break;
            case 3:
                if (GraphPlot.TryFitY(_session, w, out var min, out var max))
                {
                    w.YMin = min;
                    w.YMax = max;
                }
                break;
            case 4:
                Scale(ref w, 0.5d);
                break;
            default:
                Scale(ref w, 2d);
                break;
        }
        if (w.Valid)
        {
            _session.Window = w;
            LoadWindowBuffer();
        }
    }

    private static void Scale(ref GraphWindow w, double factor)
    {
        var cx = (w.XMin + w.XMax) * 0.5d;
        var cy = (w.YMin + w.YMax) * 0.5d;
        var hx = w.Width * 0.5d * factor;
        var hy = w.Height * 0.5d * factor;
        w.XMin = cx - hx;
        w.XMax = cx + hx;
        w.YMin = cy - hy;
        w.YMax = cy + hy;
    }

    private void LoadWindowBuffer()
    {
        _windowBuffer[0] = CalcFormat.Number(_session.Window.XMin);
        _windowBuffer[1] = CalcFormat.Number(_session.Window.XMax);
        _windowBuffer[2] = CalcFormat.Number(_session.Window.YMin);
        _windowBuffer[3] = CalcFormat.Number(_session.Window.YMax);
    }

    private void ApplyWindowBuffer(OsAppContext ctx)
    {
        if (!CalcFormat.TryParse(_windowBuffer[0], out var xmin)
            || !CalcFormat.TryParse(_windowBuffer[1], out var xmax)
            || !CalcFormat.TryParse(_windowBuffer[2], out var ymin)
            || !CalcFormat.TryParse(_windowBuffer[3], out var ymax))
        {
            _windowError = ctx.Localize("os.calc_err_syntax");
            return;
        }
        var candidate = new GraphWindow
        {
            XMin = xmin,
            XMax = xmax,
            YMin = ymin,
            YMax = ymax,
        };
        if (!candidate.Valid)
        {
            _windowError = ctx.Localize("os.calc_window_bad");
            return;
        }
        _session.Window = candidate;
        _windowError = null;
        ClosePanel();
    }

    private Action<Vector2, Vector2> DrawCalcPanel(OsAppContext ctx) => (tl, size) =>
    {
        if (DeviceUi.PanelHeader(ctx, "calcCalc", tl, size, ctx.Localize("os.calc_calc_title")))
        {
            ClosePanel();
        }
        var pad = ctx.Px(14f);
        var gap = ctx.Px(6f);
        var btnH = ctx.Px(26f);
        var colW = (size.X - pad * 2f - gap * 2f) / 3f;
        var y = tl.Y + pad + ImGui.GetTextLineHeight() + ctx.Px(10f);

        string[] ops =
        [
            ctx.Localize("os.calc_calc_root"),
            ctx.Localize("os.calc_calc_min"),
            ctx.Localize("os.calc_calc_max"),
            ctx.Localize("os.calc_calc_intersect"),
            ctx.Localize("os.calc_calc_derivative"),
            ctx.Localize("os.calc_calc_integral"),
        ];
        for (var i = 0; i < ops.Length; i++)
        {
            var pos = new Vector2(tl.X + pad + (i % 3) * (colW + gap), y + (i / 3) * (btnH + gap));
            if (DeviceUi.Pill(ctx, $"##calcOp{i}", pos, new Vector2(colW, btnH), ops[i], (int)_op == i,
                DeviceUi.Teal))
            {
                _op = (CalcOp)i;
            }
        }
        y += btnH * 2f + gap * 2f + ctx.Px(4f);

        y = DrawFunctionRow(ctx, tl, size, y, ctx.Localize("os.calc_calc_first"), ref _opFirst, "F1");
        if (_op == CalcOp.Intersect)
        {
            y = DrawFunctionRow(ctx, tl, size, y, ctx.Localize("os.calc_calc_second"), ref _opSecond, "F2");
        }

        var fieldW = (size.X - pad * 2f - gap) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(tl.X + pad, y));
        if (_op == CalcOp.Derivative)
        {
            DeviceUi.NumberField("##calcOpAt", ctx.Localize("os.calc_calc_at"), ref _opAt, fieldW);
        }
        else
        {
            using (ImRaii.Group())
            {
                DeviceUi.NumberField("##calcOpLo", ctx.Localize("os.calc_calc_lower"), ref _opLower,
                    fieldW);
            }
            ImGui.SameLine(0f, gap);
            using (ImRaii.Group())
            {
                DeviceUi.NumberField("##calcOpHi", ctx.Localize("os.calc_calc_upper"), ref _opUpper,
                    fieldW);
            }
        }

        var runTL = new Vector2(tl.X + pad, tl.Y + size.Y - pad - btnH);
        if (DeviceUi.Pill(ctx, "##calcOpRun", runTL, new Vector2(size.X - pad * 2f, btnH),
            ctx.Localize("os.calc_calc_run"), true, DeviceUi.Teal))
        {
            RunCalc(ctx);
        }
        if (_session.Status is { } status)
        {
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
                runTL - new Vector2(0f, ctx.Px(20f)),
                ImGui.ColorConvertFloat4ToU32(_session.StatusIsError ? UiColors.Danger : UiColors.Success),
                status, size.X - pad * 2f);
        }
    };

    private float DrawFunctionRow(OsAppContext ctx, Vector2 tl, Vector2 size, float y, string label,
        ref int slot, string id)
    {
        var pad = ctx.Px(14f);
        var gap = ctx.Px(6f);
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(tl.X + pad, y), ImGui.ColorConvertFloat4ToU32(UiColors.Hint), label);
        y += ImGui.GetTextLineHeight() * 0.9f + ctx.Px(2f);

        var btnH = ctx.Px(24f);
        var colW = (size.X - pad * 2f - gap * 3f) / 4f;
        for (var i = 0; i < CalcSession.FunctionCount; i++)
        {
            var fn = _session.Functions[i];
            var pos = new Vector2(tl.X + pad + i * (colW + gap), y);
            if (DeviceUi.Pill(ctx, $"##calcFn{id}{i}", pos, new Vector2(colW, btnH), fn.Label, slot == i,
                fn.Chip) && fn.Compiled is not null)
            {
                slot = i;
            }
        }
        return y + btnH + ctx.Px(8f);
    }

    private void RunCalc(OsAppContext ctx)
    {
        var first = _session.Functions[_opFirst];
        if (first.Compiled is not { } f)
        {
            _session.SetStatus(ctx.Localize("os.calc_no_function"), true);
            return;
        }
        if (_op == CalcOp.Derivative)
        {
            if (!CalcFormat.TryParse(_opAt, out var at))
            {
                _session.SetStatus(ctx.Localize("os.calc_err_syntax"), true);
                return;
            }
            if (!CalcSolve.TryDerivative(f, _session.Env, at, out var slope))
            {
                _session.SetStatus(ctx.Localize("os.calc_calc_none"), true);
                return;
            }
            _session.SetStatus($"dy/dx = {CalcFormat.Number(slope)}", false);
            return;
        }

        if (!CalcFormat.TryParse(_opLower, out var lo) || !CalcFormat.TryParse(_opUpper, out var hi))
        {
            _session.SetStatus(ctx.Localize("os.calc_err_syntax"), true);
            return;
        }
        if (hi <= lo)
        {
            _session.SetStatus(ctx.Localize("os.calc_window_bad"), true);
            return;
        }

        switch (_op)
        {
            case CalcOp.Root:
                if (!CalcSolve.TryRoot(f, _session.Env, lo, hi, out var root))
                {
                    _session.SetStatus(ctx.Localize("os.calc_calc_none"), true);
                    return;
                }
                _session.SetStatus($"x = {CalcFormat.Number(root)}", false);
                break;
            case CalcOp.Minimum:
            case CalcOp.Maximum:
                if (!CalcSolve.TryExtremum(f, _session.Env, lo, hi, _op == CalcOp.Maximum, out var ex,
                    out var ey))
                {
                    _session.SetStatus(ctx.Localize("os.calc_calc_none"), true);
                    return;
                }
                _session.SetStatus($"x = {CalcFormat.Number(ex)}   y = {CalcFormat.Number(ey)}", false);
                break;
            case CalcOp.Intersect:
                if (_session.Functions[_opSecond].Compiled is not { } g)
                {
                    _session.SetStatus(ctx.Localize("os.calc_no_function"), true);
                    return;
                }
                if (!CalcSolve.TryIntersect(f, g, _session.Env, lo, hi, out var ix, out var iy))
                {
                    _session.SetStatus(ctx.Localize("os.calc_calc_none"), true);
                    return;
                }
                _session.SetStatus($"x = {CalcFormat.Number(ix)}   y = {CalcFormat.Number(iy)}", false);
                break;
            default:
                if (!CalcSolve.TryIntegral(f, _session.Env, lo, hi, out var area))
                {
                    _session.SetStatus(ctx.Localize("os.calc_calc_none"), true);
                    return;
                }
                _session.SetStatus($"{ctx.Localize("os.calc_calc_integral")} = {CalcFormat.Number(area)}", false);
                break;
        }
    }
}
