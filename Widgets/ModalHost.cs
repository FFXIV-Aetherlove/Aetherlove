using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using AetherLove.UI;

namespace AetherLove.Widgets;

/// <summary>A single top-level, full-viewport window that hosts the app's modals. It draws its own dim
/// and centred panel, so the backdrop is always behind the modal — sidestepping ImGui's auto-dim, whose
/// layering breaks when popups are opened inside the phone's nested windows. Only one modal shows at a
/// time, so one shared host suffices. Callers use <see cref="Open"/> / <see cref="Close"/>.</summary>
public sealed class ModalHost : Window
{
    public static ModalHost? Instance { get; private set; }

    private float _panelWidthDesign;
    private Action<float>? _body;
    private bool _closeOnClickOutside;
    private float _lastPanelHeight;
    private float _savedFontGlobalScale;

    public ModalHost() : base("##AetherLoveModalHost",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
      | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
      | ImGuiWindowFlags.NoDocking)
    {
        Instance = this;
        IsOpen = false;
        ForceMainWindow = true;
        RespectCloseHotkey = false;
    }

    /// <summary><paramref name="body"/> draws the modal's content given the panel's inner width.</summary>
    public void Open(float panelWidthDesign, Action<float> body, bool closeOnClickOutside = true)
    {
        _panelWidthDesign = panelWidthDesign;
        _body = body;
        _closeOnClickOutside = closeOnClickOutside;
        _lastPanelHeight = 0f;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _body = null;
    }

    public override void PreDraw()
    {
        var vp = ImGui.GetMainViewport();
        Position = vp.Pos;
        PositionCondition = ImGuiCond.Always;
        Size = vp.Size;
        SizeCondition = ImGuiCond.Always;

        var io = ImGui.GetIO();
        _savedFontGlobalScale = io.FontGlobalScale;
        io.FontGlobalScale = 1f;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        ImGui.GetIO().FontGlobalScale = _savedFontGlobalScale;
    }

    public override void Draw()
    {
        using var bodyFont = UiFonts.Body?.Push();

        var vp = ImGui.GetMainViewport();
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(vp.Pos, vp.Pos + vp.Size, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        // Scrim drawn before the panel so the panel and its widgets sit on top and receive input.
        ImGui.SetCursorScreenPos(vp.Pos);
        if (ImGui.InvisibleButton("##modalScrim", vp.Size) && _closeOnClickOutside)
        {
            Close();
            return;
        }

        if (_body is null)
        {
            return;
        }

        var w = Px(_panelWidthDesign);
        var pad = Px(18f, 14f);

        // Panel auto-sizes to content: centre using last frame's measured height, then re-measure.
        var h = _lastPanelHeight > 0f ? _lastPanelHeight : Px(160f);
        var panelPos = vp.Pos + (vp.Size - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##modalPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                _body.Invoke(innerW);
                ImGui.PopTextWrapPos();
                _lastPanelHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        if (_closeOnClickOutside && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Close();
        }
    }
}
