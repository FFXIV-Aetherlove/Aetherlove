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
    private float _savedFontGlobalScale = 1f;
    private Vector2 _anchorPos;
    private Vector2 _anchorSize;
    private bool _hasAnchor;
    private bool _justOpened;

    public ModalHost() : base("##AetherLoveModalHost",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
      | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
      | ImGuiWindowFlags.NoDocking)
    {
        Instance = this;
        IsOpen = false;
    }

    /// <summary><paramref name="body"/> draws the modal's content given the panel's inner width.</summary>
    public void Open(float panelWidthDesign, Action<float> body, bool closeOnClickOutside = true)
    {
        _panelWidthDesign = panelWidthDesign;
        _body = body;
        _closeOnClickOutside = closeOnClickOutside;
        _lastPanelHeight = 0f;
        _justOpened = true;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _body = null;
    }

    /// <summary>Pins the modal over the host phone window's rect so the dim + panel follow the phone to
    /// whatever viewport it's on — fixes "Enable multiple screens", where the phone can live on a viewport
    /// other than the game's main one.</summary>
    public void SetAnchor(Vector2 pos, Vector2 size)
    {
        _anchorPos = pos;
        _anchorSize = size;
        _hasAnchor = true;
    }

    public override void PreDraw()
    {
        var io = ImGui.GetIO();

        // Multi-viewport ("Enable multiple screens") can put the phone on its own OS viewport that a
        // main-viewport window can't cover — anchor the dim to the phone's rect then. Otherwise pin to the
        // main viewport for a real full-screen backdrop behind a screen-centred modal.
        var multiViewport = io.ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable);

        if (multiViewport && _hasAnchor)
        {
            ForceMainWindow = false;
            Position = _anchorPos;
            Size = _anchorSize;
        }
        else
        {
            ForceMainWindow = true;
            var vp = ImGui.GetMainViewport();
            Position = vp.Pos;
            Size = vp.Size;
        }
        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;

        // Raise the modal above the phone once, when it opens — not every frame, which would steal focus
        // from text fields inside the modal.
        if (_justOpened)
        {
            ImGui.SetNextWindowFocus();
            _justOpened = false;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        _savedFontGlobalScale = io.FontGlobalScale;
        io.FontGlobalScale = 1f;
    }

    public override void PostDraw()
    {
        ImGui.GetIO().FontGlobalScale = _savedFontGlobalScale;
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        using var bodyFont = UiFonts.Body?.Push();

        // PreDraw sizes this window to whatever it dims — the full main viewport normally, or the phone's
        // rect under multi-viewport — so its own pos/size is the backdrop area in both cases.
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(winPos, winPos + winSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        // Scrim drawn before the panel so the panel and its widgets sit on top and receive input.
        ImGui.SetCursorScreenPos(winPos);
        if (ImGui.InvisibleButton("##modalScrim", winSize) && _closeOnClickOutside)
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
        var panelPos = winPos + (winSize - new Vector2(w, h)) * 0.5f;

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
