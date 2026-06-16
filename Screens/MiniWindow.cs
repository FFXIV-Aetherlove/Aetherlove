using System;
using System.IO;
using System.Numerics;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherLove.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace AetherLove.Screens;

/// <summary>Floating minimised-state bubble. Drag to move, tap to restore.</summary>
public sealed class MiniWindow : Window, IDisposable
{
    private readonly MainPluginWindow _main;
    private readonly NotificationCenter _notifications;
    private readonly PhoneShellWidget _shell = new();

    private ISharedImmediateTexture? _logoTex;
    private bool _logoLoaded;

    private const string LogoFileName = "logo_mini.png";

    private bool _mouseDownOnWindow;
    private Vector2 _mouseDownPos;
    private Vector2 _windowPosAtDown;
    private bool _dragThresholdMet;
    private const float DragThreshold = 5f;

    /// <summary>Seconds of vibrate remaining; 0 is idle.</summary>
    private float _shakeRemaining;

    /// <summary>Window position sampled at shake start; offsets applied relative to it.</summary>
    private Vector2 _shakeAnchorPos;

    private const float ShakeTotalSeconds = 3f;
    private const float ShakeAmplitude = 9f;
    private const float ShakeFrequencyHz = 4f;

    public MiniWindow(
        MainPluginWindow main,
        NotificationCenter notifications) : base(
        "AetherLove##Mini",
        ImGuiWindowFlags.NoResize
      | ImGuiWindowFlags.NoScrollbar
      | ImGuiWindowFlags.NoScrollWithMouse
      | ImGuiWindowFlags.NoTitleBar
      | ImGuiWindowFlags.NoMove)
    {
        _main = main;
        _notifications = notifications;
        Size = new Vector2(85, 153);
        SizeCondition = ImGuiCond.Always;
        Position = new Vector2(80, 300);
        PositionCondition = ImGuiCond.FirstUseEver;
        IsOpen = false;

        _notifications.UnreadChatMessageArrived += OnUnreadChatMessageArrived;
        _notifications.PendingWarningRaised += OnPendingWarningRaised;
    }

    private void OnUnreadChatMessageArrived()
    {
        // Only vibrate when the mini phone is what the user sees; full window has its own badge.
        if (!IsOpen)
        {
            return;
        }
        _shakeRemaining = ShakeTotalSeconds;
        // Anchor is sampled in Draw() on the first shake frame — no valid window pos yet here.
        _shakeAnchorPos = Vector2.Zero;
    }

    private void OnPendingWarningRaised()
    {
        if (!IsOpen)
        {
            return;
        }
        _shakeRemaining = ShakeTotalSeconds;
        _shakeAnchorPos = Vector2.Zero;
    }

    /// <summary>Visibility rule: always hidden while logged out. Mirrors MainPluginWindow —
    /// Hide = auto-hide during combat; Minimize / LeaveOpen = always visible.</summary>
    public override bool DrawConditions()
        => Plugin.ClientState.IsLoggedIn
           && (Plugin.Configuration.CombatBehavior != CombatBehavior.Hide
               || !Plugin.Condition[ConditionFlag.InCombat]);

    private float _savedFontGlobalScale;

    public override void PreDraw()
    {
        // Pin out Dalamud's global font scale so the mini phone stays its fixed size (see MainPluginWindow).
        Size = Px(85f, 153f);
        var io = ImGui.GetIO();
        _savedFontGlobalScale = io.FontGlobalScale;
        io.FontGlobalScale = 1f;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void Draw()
    {
        using var bodyFont = UiFonts.Body?.Push();

        ApplyShake();

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var mousePos = ImGui.GetMousePos();

        _shell.DrawBackground(pos, size);

        var LogoSize = Px(64f);
        var LogoTopMargin = Px(18f);
        var IconAreaH = Px(28f);
        var IconBottomMargin = Px(28f);

        EnsureLogo();
        var logoTL = pos + new Vector2((size.X - LogoSize) * 0.5f, LogoTopMargin);
        var logoBR = logoTL + new Vector2(LogoSize, LogoSize);
        var logoWrap = _logoTex?.GetWrapOrDefault();
        if (logoWrap != null)
        {
            dl.AddImage(logoWrap.Handle, logoTL, logoBR);
        }

        // Manual rect-test; InvisibleButton would steal the tap-to-restore click.
        var logoHovered = ImGui.IsWindowHovered()
                          && mousePos.X >= logoTL.X && mousePos.X <= logoBR.X
                          && mousePos.Y >= logoTL.Y && mousePos.Y <= logoBR.Y;
        if (logoHovered)
        {
            ImGui.SetTooltip("Open AetherLove");
        }

        var DotR = Px(9f);
        var dotCenter = pos + new Vector2(size.X - DotR - Px(3f), DotR + Px(3f));
        if (_notifications.HasPendingWarning)
        {
            dl.AddCircleFilled(dotCenter, DotR, ImGui.ColorConvertFloat4ToU32(UiColors.Amber));
            var warnSz = ImGui.CalcTextSize("!");
            dl.AddText(dotCenter - warnSz * 0.5f, 0xFF111111u, "!");
        }
        else if (_notifications.TotalBadge > 0)
        {
            var badgeCount = _notifications.TotalBadge;
            dl.AddCircleFilled(dotCenter, DotR, UiColors.UnreadBadge);
            var badgeLabel = badgeCount > 9 ? "9+" : badgeCount.ToString();
            var badgeSz = ImGui.CalcTextSize(badgeLabel);
            dl.AddText(dotCenter - badgeSz * 0.5f, 0xFFFFFFFF, badgeLabel);
        }

        var iconBoxTL = pos + new Vector2(0f, size.Y - IconAreaH - IconBottomMargin);
        var iconBoxSize = new Vector2(size.X, IconAreaH);
        ImGui.SetCursorScreenPos(iconBoxTL);
        ImGui.InvisibleButton("##miniShutdown", iconBoxSize);
        var iconHovered = ImGui.IsItemHovered();
        var iconClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (iconHovered)
        {
            ImGui.SetTooltip("Close AetherLove");
        }

        var iconCol = iconHovered ? 0xFFFF6060u : 0xFFAAAAAAu;
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = FontAwesomeIcon.PowerOff.ToIconString();
        var iconBaseSize = ImGui.GetFontSize();
        var iconRenderSz = iconBaseSize * 1.4f;
        var iconGlyphSz = ImGui.CalcTextSize(iconStr) * (iconRenderSz / iconBaseSize);
        var iconGlyphPos = iconBoxTL + (iconBoxSize - iconGlyphSz) * 0.5f;
        dl.AddText(ImGui.GetFont(), iconRenderSz, iconGlyphPos, iconCol, iconStr);
        ImGui.PopFont();

        if (iconClicked)
        {
            // Hide both windows; the plugin stays connected so notifications keep arriving.
            _main.IsOpen = false;
            IsOpen = false;
            _mouseDownOnWindow = false;
            return;
        }

        // <=5px movement counts as a tap (restore), anything more is a drag.
        if (!iconHovered && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _mouseDownOnWindow = true;
            _mouseDownPos = mousePos;
            _windowPosAtDown = pos;
            _dragThresholdMet = false;
        }

        if (_mouseDownOnWindow && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = mousePos - _mouseDownPos;
            if (!_dragThresholdMet &&
                (MathF.Abs(delta.X) > DragThreshold || MathF.Abs(delta.Y) > DragThreshold))
            {
                _dragThresholdMet = true;
            }

            if (_dragThresholdMet)
            {
                ImGui.SetWindowPos(_windowPosAtDown + delta);
            }
        }

        if (_mouseDownOnWindow && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (!_dragThresholdMet)
            {
                _main.OpenToChat();
                IsOpen = false;
            }
            _mouseDownOnWindow = false;
        }
    }

    public override void PostDraw()
    {
        ImGui.GetIO().FontGlobalScale = _savedFontGlobalScale;
        ImGui.PopStyleVar();
    }

    /// <summary>Drives the 3×1s damped-sine vibrate on the X axis.</summary>
    private void ApplyShake()
    {
        if (_shakeRemaining <= 0f)
        {
            return;
        }

        var io = ImGui.GetIO();
        if (_shakeAnchorPos == Vector2.Zero)
        {
            _shakeAnchorPos = ImGui.GetWindowPos();
        }

        var elapsedTotal = ShakeTotalSeconds - _shakeRemaining;
        var shakeIndex = (int)MathF.Floor(elapsedTotal);
        var shakeLocalT = elapsedTotal - shakeIndex;
        var damp = 1f - shakeLocalT;
        var offsetX = MathF.Sin(shakeLocalT * MathF.Tau * ShakeFrequencyHz)
                          * ShakeAmplitude
                          * damp;

        ImGui.SetWindowPos(_shakeAnchorPos + new Vector2(offsetX, 0f));

        _shakeRemaining -= io.DeltaTime;
        if (_shakeRemaining <= 0f)
        {
            ImGui.SetWindowPos(_shakeAnchorPos);
            _shakeAnchorPos = Vector2.Zero;
            _mouseDownOnWindow = false;
        }
    }


    private void EnsureLogo()
    {
        if (_logoLoaded)
        {
            return;
        }
        _logoLoaded = true;

        try
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", LogoFileName);
            if (File.Exists(path))
            {
                _logoTex = Plugin.TextureProvider.GetFromFile(path);
            }
            else
            {
                Plugin.Log.Warning($"[MiniWindow] Mini logo not found at {path}.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MiniWindow] Failed to load mini logo.");
        }
    }

    public void Dispose()
    {
        _notifications.UnreadChatMessageArrived -= OnUnreadChatMessageArrived;
        _notifications.PendingWarningRaised -= OnPendingWarningRaised;
        _shell.Dispose();
    }
}
