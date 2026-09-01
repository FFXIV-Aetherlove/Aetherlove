using System;
using System.IO;
using System.Numerics;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Floating minimised-state bubble. Drag to move, tap to restore.</summary>
public sealed class MiniWindow : Window, IDisposable
{
    private readonly MainPluginWindow _main;
    private readonly NotificationCenter _notifications;
    private readonly Os.IOsMediaRemote _media;
    private readonly AetherOS.Sdk.IOsShell _osShell;

    /// <summary>How many app icons the warm-up loads per frame.</summary>
    private const int IconsPerFrame = 2;

    private bool _preWarmed;
    private int _preWarmIndex;
    private readonly PhoneShellWidget _shell = new();

    private ISharedImmediateTexture? _logoTex;
    private bool _logoLoaded;

    private const string LogoFileName = "logo_mini.png";

    private const string MenuId = "##miniPhoneMenu";

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
        NotificationCenter notifications,
        AetherOS.Sdk.IOsShell osShell,
        Os.IOsMediaRemote media) : base(
        "AetherLove##Mini",
        ImGuiWindowFlags.NoResize
      | ImGuiWindowFlags.NoScrollbar
      | ImGuiWindowFlags.NoScrollWithMouse
      | ImGuiWindowFlags.NoTitleBar
      | ImGuiWindowFlags.NoMove
      | ImGuiWindowFlags.NoDocking
      | ImGuiWindowFlags.NoBackground)
    {
        _main = main;
        _notifications = notifications;
        _osShell = osShell;
        _media = media;
        Size = new Vector2(85, 153);
        SizeCondition = ImGuiCond.Always;
        Position = new Vector2(80, 300);
        PositionCondition = ImGuiCond.FirstUseEver;
        IsOpen = false;

        _notifications.UnreadChatMessageArrived += OnUnreadChatMessageArrived;
        _notifications.PendingWarningRaised += OnPendingWarningRaised;
    }

    private bool _recenterRequested;

    /// <summary>Queues a one-shot recenter on the next frame (ImGui isn't valid from the command thread).</summary>
    public void RequestRecenter() => _recenterRequested = true;

    private void OnUnreadChatMessageArrived()
    {
        // Only vibrate when the mini phone is what the user sees; full window has its own badge.
        if (!IsOpen)
        {
            return;
        }
        _shakeRemaining = ShakeTotalSeconds;
        // Anchor is sampled in Draw() on the first shake frame - no valid window pos yet here.
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

    /// <summary>Always hidden while logged out; Hide = auto-hide during combat, Minimize/LeaveOpen = always visible.</summary>
    public override bool DrawConditions()
        => Plugin.ClientState.IsLoggedIn
           && (Plugin.Configuration.CombatBehavior != CombatBehavior.Hide
               || !Plugin.Condition[ConditionFlag.InCombat]);

    private float _savedFontGlobalScale = 1f;

    public override void PreDraw()
    {
        // Width follows the theme's window aspect so wide bezel art (e.g. NieR) isn't squeezed.
        Size = MiniScale.Px(85f * (ThemeService.Current.WindowWidth / UiScale.Design.X), 153f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        // Pin out Dalamud's global font scale so the mini phone stays its fixed size.
        FontDiagnostics.Sample("MiniWindow.PreDraw/before-pin");
        _savedFontGlobalScale = FontScalePin.Pin();
        FontDiagnostics.Sample("MiniWindow.PreDraw/after-pin");
    }

    /// <summary>Builds what the full-size phone will need while the bubble is still up. A font handle is only
    /// built once something pushes it, and the wallpaper and every app icon only load once something draws
    /// them, so a phone that boots minimised does all of that in the single frame it is enlarged: on a slow
    /// machine that reads as a freeze. A handful per frame here spreads it over the time the bubble sits idle.
    /// Everything is cached by its owner, so this is a warm-up rather than a second copy.</summary>
    private void PreWarm()
    {
        if (_preWarmed)
        {
            return;
        }
        // Pushing a handle is what makes Dalamud build it; the pairs are empty, so nothing is drawn.
        using (UiFonts.Body?.Push()) { }
        using (UiFonts.Reader?.Push()) { }
        using (UiFonts.H1?.Push()) { }
        using (UiFonts.H2?.Push()) { }
        using (UiFonts.H3?.Push()) { }
        using (UiFonts.Clock?.Push()) { }
        using (UiFonts.Icon?.Push()) { }
        if (!UiFonts.Ready)
        {
            return;
        }

        // A few icons per frame: each one is a file read plus a texture upload, and doing the lot at once
        // would just move the stall here.
        var apps = _osShell.Apps;
        var end = System.Math.Min(_preWarmIndex + IconsPerFrame, apps.Count);
        for (; _preWarmIndex < end; _preWarmIndex++)
        {
            AppIcons.Tile(apps[_preWarmIndex].Id);
        }
        if (_preWarmIndex >= apps.Count)
        {
            _preWarmed = true;
        }
    }

    /// <summary>What the bubble counts: every app's own unread model plus the OS badges laid on top, the same
    /// sum the home screen's tiles show. It deliberately does NOT read <see cref="NotificationCenter"/>, which
    /// only ever knew about AetherLove and so left the messenger, Yapper and everything since uncounted.
    /// An app taken off the home screen keeps running and keeps its own count, so it is skipped here: the
    /// bubble must not announce something the player has no tile for.</summary>
    private int TotalBadge()
    {
        var total = 0;
        foreach (var app in _osShell.Apps)
        {
            if (_osShell.IsAppRemoved(app.Id))
            {
                continue;
            }
            total += System.Math.Max(0, app.Badge) + System.Math.Max(0, _osShell.OsBadge(app.Id));
        }
        return total;
    }

    public override void Draw()
    {
        PreWarm();
        using var bodyFont = UiFonts.Body?.Push();

        if (_recenterRequested)
        {
            _recenterRequested = false;
            var vp = ImGui.GetMainViewport();
            ImGui.SetWindowPos(vp.Pos + (vp.Size - ImGui.GetWindowSize()) * 0.5f);
        }

        ApplyShake();

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var mousePos = ImGui.GetMousePos();

        _shell.DrawBackground(pos, size);

        var LogoSize = MiniScale.Px(64f);
        var LogoTopMargin = MiniScale.Px(18f);
        var IconAreaH = MiniScale.Px(24f);
        var IconBottomMargin = MiniScale.Px(18f);

        var logoTL = pos + new Vector2((size.X - LogoSize) * 0.5f, LogoTopMargin);
        var logoBR = logoTL + new Vector2(LogoSize, LogoSize);
        var transportHovered = false;
        if (_media.MiniVisible)
        {
            transportHovered = DrawMediaBar(dl, logoTL, new Vector2(LogoSize, LogoSize));
        }
        else
        {
            EnsureLogo();
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
                ImGui.SetTooltip(Loc.T("os.mini_open"));
            }
        }

        var DotR = MiniScale.Px(9f);
        var dotCenter = pos + new Vector2(size.X - DotR - MiniScale.Px(3f), DotR + MiniScale.Px(3f));
        // Draw the badge label at an explicit mini-scaled size, not the main-phone-scaled body font.
        var badgeFont = ImGui.GetFont();
        var badgePx = MiniScale.Px(13f);
        var badgeScale = badgePx / ImGui.GetFontSize();
        if (_notifications.HasPendingWarning)
        {
            dl.AddCircleFilled(dotCenter, DotR, ImGui.ColorConvertFloat4ToU32(UiColors.Amber));
            var warnSz = ImGui.CalcTextSize("!") * badgeScale;
            dl.AddText(badgeFont, badgePx, dotCenter - warnSz * 0.5f, 0xFF111111u, "!");
        }
        else if (TotalBadge() is > 0 and var badgeCount)
        {
            dl.AddCircleFilled(dotCenter, DotR, UiColors.UnreadBadge);
            var badgeLabel = badgeCount > 9 ? "9+" : badgeCount.ToString();
            var badgeSz = ImGui.CalcTextSize(badgeLabel) * badgeScale;
            dl.AddText(badgeFont, badgePx, dotCenter - badgeSz * 0.5f, 0xFFFFFFFF, badgeLabel);
        }

        var iconBoxTL = pos + new Vector2(0f, size.Y - IconAreaH - IconBottomMargin);
        var iconBoxSize = new Vector2(size.X, IconAreaH);
        ImGui.SetCursorScreenPos(iconBoxTL);
        ImGui.InvisibleButton("##miniShutdown", iconBoxSize);
        var iconHovered = ImGui.IsItemHovered();
        var iconClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (iconHovered)
        {
            ImGui.SetTooltip(Loc.T("os.mini_power"));
        }

        var iconCol = iconHovered ? 0xFFFF6060u : 0xFFAAAAAAu;
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconRenderSz = ImGui.GetFontSize() * 1.15f * MiniScale.S;
        ImGui.PopFont();
        IconDraw.AddCentered(dl, FontAwesomeIcon.PowerOff, iconRenderSz, iconBoxTL + iconBoxSize * 0.5f, iconCol);

        if (iconClicked)
        {
            // Confirm via the shared close modal (or close immediately if the user opted out).
            _mouseDownOnWindow = false;
            _main.RequestClose();
            return;
        }

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            OsMenu.Open(MenuId);
        }
        DrawMenu();

        // <=5px movement counts as a tap (restore), anything more is a drag.
        if (!iconHovered && !transportHovered
            && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
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

            if (_dragThresholdMet && !Plugin.Configuration.LockMiniPosition)
            {
                ImGui.SetWindowPos(_windowPosAtDown + delta);
            }
        }

        if (_mouseDownOnWindow && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (!_dragThresholdMet)
            {
                _main.Restore();
                IsOpen = false;
            }
            _mouseDownOnWindow = false;
        }
    }

    /// <summary>The bubble's own menu, the phone's three answers as they read from out here: it is already
    /// minimised, so the middle row opens it instead. The lock is the bubble's, not the phone's: the two
    /// windows keep their own positions, so pinning one has never meant pinning the other.</summary>
    private void DrawMenu()
    {
        var locked = Plugin.Configuration.LockMiniPosition;
        var rows = new OsMenu.MenuRow[]
        {
            new(FontAwesomeIcon.PowerOff, Loc.T("os.phone_menu_exit")),
            new(FontAwesomeIcon.MobileAlt, Loc.T("os.phone_menu_open")),
            new(locked ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock,
                Loc.T(locked ? "os.phone_menu_unlock" : "os.phone_menu_lock")),
        };

        // The first two close the window the popup belongs to, so they land after the menu is done drawing.
        switch (OsMenu.Draw(MenuId, rows))
        {
            case 0:
                _mouseDownOnWindow = false;
                _main.RequestClose();
                break;
            case 1:
                _mouseDownOnWindow = false;
                _main.Restore();
                IsOpen = false;
                break;
            case 2:
                Plugin.Configuration.LockMiniPosition = !locked;
                Plugin.Configuration.Save();
                break;
        }
    }

    public override void PostDraw()
    {
        FontDiagnostics.Sample("MiniWindow.PostDraw/before-restore");
        FontScalePin.Restore(_savedFontGlobalScale);
        FontDiagnostics.Sample("MiniWindow.PostDraw/after-restore");
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


    /// <summary>The logo's slot, borrowed by a now-playing readout while the PC is playing something: art,
    /// a truncated title, and the transport. Returns whether a control is hovered, which the caller uses to
    /// keep the tap-to-restore from firing underneath it.</summary>
    private bool DrawMediaBar(ImDrawListPtr dl, Vector2 tl, Vector2 size)
    {
        var artSide = size.X * 0.66f;
        var artTL = tl + new Vector2((size.X - artSide) * 0.5f, 0f);
        var artBR = artTL + new Vector2(artSide, artSide);
        var rounding = MiniScale.Px(6f);
        if (_media.Art is { } art)
        {
            dl.AddImageRounded(art, artTL, artBR, Vector2.Zero, Vector2.One, 0xFFFFFFFFu, rounding);
        }
        else
        {
            dl.AddRectFilled(artTL, artBR, 0x40FFFFFFu, rounding);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Music, artSide * 0.45f,
                (artTL + artBR) * 0.5f, 0xFFDDDDDDu);
        }

        var labelPx = MiniScale.Px(10f);
        var title = Truncate(_media.Title, size.X, labelPx);
        var titleW = ImGui.CalcTextSize(title).X * (labelPx / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), labelPx,
            new Vector2(tl.X + (size.X - titleW) * 0.5f, artBR.Y + MiniScale.Px(3f)), 0xFFEEEEEEu, title);

        var rowY = artBR.Y + MiniScale.Px(3f) + labelPx + MiniScale.Px(3f);
        var button = MiniScale.Px(16f);
        var gap = MiniScale.Px(4f);
        var rowW = (button * 3f) + (gap * 2f);
        var x = tl.X + (size.X - rowW) * 0.5f;
        var hovered = false;
        hovered |= DrawTransportButton(dl, "##miniPrev", new Vector2(x, rowY), button,
            FontAwesomeIcon.StepBackward, _media.Previous);
        x += button + gap;
        hovered |= DrawTransportButton(dl, "##miniPlay", new Vector2(x, rowY), button,
            _media.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, _media.TogglePlayPause);
        x += button + gap;
        hovered |= DrawTransportButton(dl, "##miniNext", new Vector2(x, rowY), button,
            FontAwesomeIcon.StepForward, _media.Next);
        return hovered;
    }

    private bool DrawTransportButton(
        ImDrawListPtr dl, string id, Vector2 tl, float side, FontAwesomeIcon icon, Action onClick)
    {
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton(id, new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        var enabled = _media.CanControl;
        if (hovered && enabled)
        {
            SharedUiHelpers.HandOnHover();
        }
        var tint = !enabled ? 0x66FFFFFFu : hovered ? 0xFFFFFFFFu : 0xCCDDDDDDu;
        IconDraw.AddCentered(dl, icon, side * 0.62f, tl + new Vector2(side * 0.5f, side * 0.5f), tint);
        if (pressed && enabled)
        {
            onClick();
        }
        return hovered;
    }

    private static string Truncate(string text, float maxWidth, float renderPx)
    {
        var scale = renderPx / ImGui.GetFontSize();
        if (ImGui.CalcTextSize(text).X * scale <= maxWidth)
        {
            return text;
        }
        for (var length = text.Length - 1; length > 1; length--)
        {
            var candidate = text[..length] + "…";
            if (ImGui.CalcTextSize(candidate).X * scale <= maxWidth)
            {
                return candidate;
            }
        }
        return "…";
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
