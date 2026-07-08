using System;
using System.IO;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using TerraFX.Interop.DirectX;

namespace AetherLove.Widgets;

/// <summary>Live viewfinder for the selfie flow: an aspect-locked, movable/resizable crop frame over the live game
/// with take-photo / cancel buttons. The window is NoInputs so the game keeps mouse camera control; the frame and
/// buttons hit-test manually and grab the mouse only while hovered or dragging. On capture it hands back the
/// full-frame temp PNG plus the crop rect in image pixels, like a file pick through the crop popup.</summary>
public sealed class SelfieCaptureOverlay : Window
{
    private const uint Dim = 0x99000000u;
    private const uint PanelBg = 0xF01A1420u;
    private static float HandleSize => Px(22f);
    private const float MinBoxFloor = 40f;

    /// <summary>True while the viewfinder is up; MainPluginWindow reads this to tuck the phone away.</summary>
    internal static bool Active;

    private readonly ScreenCaptureService _capture;
    private Action<string, Vector4>? _onDone;
    private float _aspect = 1.6f;
    private float _minCropW = 100f;
    private bool _focusPending;

    private Vector2 _boxTL;
    private float _boxW;
    private bool _init;

    private bool _movingBox;
    private Vector2 _moveOffset;
    private bool _resizing;
    private float _resizeStartMouseX;
    private float _resizeStartW;

    private bool _awaitingFrame;
    private Vector2 _shotBoxTL;
    private float _shotBoxW;

    private float BoxH => _boxW * _aspect;

    public SelfieCaptureOverlay(ScreenCaptureService capture) : base("##AetherLoveSelfie",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground
      | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoNavFocus
      | ImGuiWindowFlags.NoInputs)
    {
        _capture = capture;
        IsOpen = false;
    }

    /// <param name="aspect">cropHeight / cropWidth (1.0 square avatar, 1.6 for 10:16 portrait).</param>
    /// <param name="minCropW">Minimum crop width in captured pixels, so the shot isn't upscaled below the target.</param>
    /// <param name="onDone">Receives the full-frame temp PNG path and the crop rect (x, y, w, h) in image pixels.</param>
    public void Start(float aspect, float minCropW, Action<string, Vector4> onDone)
    {
        _aspect = aspect;
        _minCropW = minCropW;
        _onDone = onDone;
        _init = false;
        _awaitingFrame = false;
        _movingBox = false;
        _resizing = false;
        _focusPending = true;
        IsOpen = true;
        Active = true;
    }

    public override void PreDraw()
    {
        var vp = ImGui.GetMainViewport();
        Position = vp.Pos;
        Size = vp.Size;
        ImGui.SetNextWindowViewport(vp.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        if (_focusPending)
        {
            ImGui.SetWindowFocus();
            _focusPending = false;
        }

        var vp = ImGui.GetMainViewport();
        var origin = vp.Pos;
        var size = vp.Size;

        if (!_init)
        {
            InitBox(size);
            _init = true;
        }
        ClampBox(size);

        if (_awaitingFrame)
        {
            DrawFrame(origin, size, _shotBoxTL, _shotBoxW);
            DrawCapturingPill(origin, size);
            ImGui.SetNextFrameWantCaptureMouse(true);
            if (_capture.TryGetResult(out var px, out var w, out var h, out var fmt))
            {
                CompleteCapture(px, w, h, fmt, size);
            }
            return;
        }

        DrawFrame(origin, size, _boxTL, _boxW);
        HandleInput(origin, size);
    }

    private void InitBox(Vector2 size)
    {
        var maxByWidth = size.X * 0.6f;
        var maxByHeight = size.Y / _aspect * 0.6f;
        _boxW = MathF.Max(_minCropW, MathF.Min(maxByWidth, maxByHeight));
        _boxTL = new Vector2((size.X - _boxW) * 0.5f, (size.Y - BoxH) * 0.5f);
    }

    private void ClampBox(Vector2 size)
    {
        _boxW = MathF.Max(MinBoxFloor, MathF.Min(_boxW, size.X));
        if (BoxH > size.Y)
        {
            _boxW = size.Y / _aspect;
        }
        _boxTL.X = Math.Clamp(_boxTL.X, 0f, MathF.Max(0f, size.X - _boxW));
        _boxTL.Y = Math.Clamp(_boxTL.Y, 0f, MathF.Max(0f, size.Y - BoxH));
    }

    private void DrawFrame(Vector2 origin, Vector2 size, Vector2 boxTL, float boxW)
    {
        var boxH = boxW * _aspect;
        var dl = ImGui.GetWindowDrawList();
        var tl = origin + boxTL;
        var br = tl + new Vector2(boxW, boxH);
        var end = origin + size;

        dl.AddRectFilled(origin, new Vector2(end.X, tl.Y), Dim);
        dl.AddRectFilled(new Vector2(origin.X, tl.Y), new Vector2(tl.X, br.Y), Dim);
        dl.AddRectFilled(new Vector2(br.X, tl.Y), new Vector2(end.X, br.Y), Dim);
        dl.AddRectFilled(new Vector2(origin.X, br.Y), end, Dim);

        dl.AddRect(tl, br, 0xFFFFFFFF, 0f, ImDrawFlags.None, Px(2f));
        var tick = Px(14f);
        dl.AddLine(tl, tl + new Vector2(tick, 0f), 0xFFFFFFFF, Px(3f));
        dl.AddLine(tl, tl + new Vector2(0f, tick), 0xFFFFFFFF, Px(3f));
        dl.AddLine(new Vector2(br.X, tl.Y), new Vector2(br.X - tick, tl.Y), 0xFFFFFFFF, Px(3f));
        dl.AddLine(new Vector2(br.X, tl.Y), new Vector2(br.X, tl.Y + tick), 0xFFFFFFFF, Px(3f));
        dl.AddLine(new Vector2(tl.X, br.Y), new Vector2(tl.X + tick, br.Y), 0xFFFFFFFF, Px(3f));
        dl.AddLine(new Vector2(tl.X, br.Y), new Vector2(tl.X, br.Y - tick), 0xFFFFFFFF, Px(3f));

        var handleTL = br - new Vector2(HandleSize, HandleSize);
        dl.AddRectFilled(handleTL, br, ThemeService.Current.AccentU32);
        dl.AddRect(handleTL, br, 0xFFFFFFFF, 0f, ImDrawFlags.None, Px(2f));
    }

    private void HandleInput(Vector2 origin, Vector2 size)
    {
        var mouse = ImGui.GetMousePos();
        var clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var released = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        var tl = origin + _boxTL;
        var br = tl + new Vector2(_boxW, BoxH);
        var handleMin = br - new Vector2(HandleSize, HandleSize);
        var moveMax = handleMin;

        var takeW = Px(160f);
        var cancelW = Px(110f);
        var btnH = Px(34f);
        var gap = Px(12f);
        var bx = origin.X + (size.X - (takeW + gap + cancelW)) * 0.5f;
        var by = origin.Y + size.Y - btnH - Px(30f);
        var takeMin = new Vector2(bx, by);
        var takeMax = new Vector2(bx + takeW, by + btnH);
        var cancelMin = new Vector2(bx + takeW + gap, by);
        var cancelMax = new Vector2(cancelMin.X + cancelW, by + btnH);

        var overTake = InRect(mouse, takeMin, takeMax);
        var overCancel = InRect(mouse, cancelMin, cancelMax);
        var overResize = InRect(mouse, handleMin, br);
        var overMove = !overResize && InRect(mouse, tl, moveMax);

        DrawHint(origin, size);
        DrawButton(takeMin, takeMax, Loc.T("common.selfie_take"), overTake, primary: true);
        DrawButton(cancelMin, cancelMax, Loc.T("common.cancel"), overCancel, primary: false);

        if (_resizing)
        {
            var delta = mouse.X - _resizeStartMouseX;
            var newW = MathF.Max(_minCropW, _resizeStartW + delta);
            newW = MathF.Min(newW, size.X - _boxTL.X);
            newW = MathF.Min(newW, (size.Y - _boxTL.Y) / _aspect);
            _boxW = newW;
            if (released)
            {
                _resizing = false;
            }
        }
        else if (_movingBox)
        {
            var newTL = mouse - origin - _moveOffset;
            newTL.X = Math.Clamp(newTL.X, 0f, size.X - _boxW);
            newTL.Y = Math.Clamp(newTL.Y, 0f, size.Y - BoxH);
            _boxTL = newTL;
            if (released)
            {
                _movingBox = false;
            }
        }
        else if (clicked)
        {
            if (overTake)
            {
                _shotBoxTL = _boxTL;
                _shotBoxW = _boxW;
                _awaitingFrame = true;
                _capture.RequestCapture();
            }
            else if (overCancel)
            {
                Close();
                return;
            }
            else if (overResize)
            {
                _resizing = true;
                _resizeStartMouseX = mouse.X;
                _resizeStartW = _boxW;
            }
            else if (overMove)
            {
                _movingBox = true;
                _moveOffset = mouse - tl;
            }
        }

        // Grab the mouse only over a widget or during a drag; everywhere else the game keeps camera control.
        if (overTake || overCancel || overResize || overMove || _movingBox || _resizing)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
        }
    }

    private static bool InRect(Vector2 p, Vector2 min, Vector2 max)
        => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

    private static void DrawHint(Vector2 origin, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var hint = Loc.T("common.selfie_instructions");
        var hintSz = ImGui.CalcTextSize(hint);
        var hintPos = new Vector2(origin.X + (size.X - hintSz.X) * 0.5f, origin.Y + Px(26f));
        var hp = new Vector2(Px(14f), Px(8f));
        dl.AddRectFilled(hintPos - hp, hintPos + hintSz + hp, PanelBg, Px(8f));
        dl.AddText(hintPos, ImGui.GetColorU32(UiColors.Body), hint);
    }

    private static void DrawButton(Vector2 min, Vector2 max, string label, bool hovered, bool primary)
    {
        var dl = ImGui.GetWindowDrawList();
        var t = ThemeService.Current;
        var baseCol = hovered ? t.ButtonHovered : t.ButtonNormal;
        if (!primary)
        {
            baseCol = baseCol with { W = baseCol.W * 0.5f };
        }
        dl.AddRectFilled(min, max, ImGui.GetColorU32(baseCol), Px(6f));
        dl.AddRect(min, max, ImGui.GetColorU32(UiColors.Body with { W = 0.3f }), Px(6f), ImDrawFlags.None, Px(1f));
        var ts = ImGui.CalcTextSize(label);
        var center = (min + max) * 0.5f;
        dl.AddText(center - ts * 0.5f, ImGui.GetColorU32(UiColors.Body), label);
    }

    private void CompleteCapture(byte[]? px, int w, int h, DXGI_FORMAT fmt, Vector2 viewport)
    {
        if (px is null)
        {
            Plugin.Log.Warning("[Selfie] capture failed.");
            Close();
            return;
        }
        try
        {
            var sx = w / viewport.X;
            var sy = h / viewport.Y;
            var crop = new Vector4(_shotBoxTL.X * sx, _shotBoxTL.Y * sy, _shotBoxW * sx, _shotBoxW * _aspect * sy);

            var png = ScreenCaptureService.EncodePng(px, w, h, fmt);
            Directory.CreateDirectory(TempDir);
            var path = Path.Combine(TempDir, $"selfie_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, png);

            var cb = _onDone;
            Close();
            cb?.Invoke(path, crop);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[Selfie] encode/save failed.");
            Close();
        }
    }

    private void Close()
    {
        IsOpen = false;
        Active = false;
        _onDone = null;
        _awaitingFrame = false;
    }

    private static string TempDir => Path.Combine(Path.GetTempPath(), "AetherLoveSelfie");

    /// <summary>Clears leftover selfie temp PNGs; called on load and unload.</summary>
    internal static void PurgeTempFiles()
    {
        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, true);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Selfie] temp cleanup failed.");
        }
    }

    private static void DrawCapturingPill(Vector2 origin, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var text = Loc.T("common.selfie_capturing");
        var ts = ImGui.CalcTextSize(text);
        var pad = new Vector2(Px(20f), Px(12f));
        var center = new Vector2(origin.X + size.X * 0.5f, origin.Y + size.Y * 0.5f);
        var pMin = center - ts * 0.5f - pad;
        var pMax = center + ts * 0.5f + pad;
        dl.AddRectFilled(pMin, pMax, PanelBg, Px(10f));
        dl.AddRect(pMin, pMax, ThemeService.Current.AccentU32, Px(10f), ImDrawFlags.None, Px(1.5f));
        dl.AddText(center - ts * 0.5f, ImGui.GetColorU32(UiColors.Body), text);
    }
}
