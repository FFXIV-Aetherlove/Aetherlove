using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Echo;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.EchoVidya;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using static AetherLove.UI.OnboardingUi;
using static AetherLove.UI.SharedUiHelpers;
using static AetherLove.UI.UiScale;

namespace AetherOS.Apps.EchoVidya.Screens;

/// <summary>Echo's landing surface: watch a link on your own, or start or join a room. While the user is in a
/// room the two room tiles collapse into one live card for it.</summary>
internal sealed class HomeScreen
{
    private const float PadX = 16f;
    private const float CardRounding = 14f;
    private const float NavCardHeight = 82f;
    private const float RowHeight = 34f;
    private const float ActionHeight = 40f;
    private const float RoomCardHeight = 96f;
    private const int VideoIdLength = 11;
    private const int WatchInputMaxLength = 240;

    private static readonly string[] PathVideoPrefixes = ["shorts", "embed", "live", "v"];

    private readonly AetherHubContext _hub;
    private readonly EchoStateService _state;
    private readonly IEchoHost _host;
    private readonly Action _openRoom;
    private readonly Action _openSetup;
    private readonly EntranceAnimation _entrance = new();

    private string _watchInput = string.Empty;
    private string _joinCode = string.Empty;
    private string? _watchError;
    private volatile string? _joinError;
    private volatile bool _busy;
    private volatile bool _joined;
    private bool _joinOpen;

    public HomeScreen(AetherHubContext hub, EchoStateService state, IEchoHost host, Action openRoom,
        Action openSetup)
    {
        _hub = hub;
        _state = state;
        _host = host;
        _openRoom = openRoom;
        _openSetup = openSetup;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _watchError = null;
        _joinError = null;
    }

    /// <summary>Entry point for a tapped room share card: reveals the join field prefilled and submits.</summary>
    public void BeginJoin(string code)
    {
        _joinOpen = true;
        _joinCode = code;
        _joinError = null;
        SubmitJoin();
    }

    public void Draw(OsAppContext ctx)
    {
        if (_joined)
        {
            _joined = false;
            _joinOpen = false;
            _joinCode = string.Empty;
            _openRoom();
        }

        _entrance.BeginFrame();

        var winW = ImGui.GetWindowSize().X;
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        DrawWatchCard(winW);
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        if (!_host.RuntimeReady)
        {
            DrawRuntimeNotice(winW);
        }

        if (_state.Room is { } room && _state.EndReason is null)
        {
            DrawCurrentRoomCard(ctx, winW, room);
        }
        else
        {
            DrawRoomActions(winW);
        }

        ImGui.Dummy(new Vector2(0f, Px(18f)));
        _entrance.EndFrame();
    }

    private void DrawWatchCard(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(12f);
        var cardW = winW - Px(PadX) * 2f;
        var innerW = cardW - pad * 2f;
        var rowH = Px(RowHeight);
        var lineH = ImGui.GetTextLineHeight();
        var btnW = Px(78f);
        var errorH = _watchError is { } err ? Px(8f) + ImGui.CalcTextSize(err, false, innerW).Y : 0f;

        var titleY = pad;
        var subtitleY = titleY + lineH + Px(4f);
        var rowY = subtitleY + lineH + Px(10f);
        var errorY = rowY + rowH;
        var cardH = errorY + errorH + pad;

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + Px(PadX), origin.Y);
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, OsDrawShared.White(0.05f), Px(CardRounding));
        dl.AddRect(tl, br, OsDrawShared.White(0.08f), Px(CardRounding), ImDrawFlags.None, Px(1f));

        ImGui.SetCursorScreenPos(tl + new Vector2(pad, titleY));
        ImGui.TextColored(t.AccentLight, Loc.T("os.echo_home_watch_title"));
        ImGui.SetCursorScreenPos(tl + new Vector2(pad, subtitleY));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.echo_home_watch_sub"));

        var enabled = _host.RuntimeReady;
        ImGui.SetCursorScreenPos(tl + new Vector2(pad, rowY));
        ImGui.SetNextItemWidth(innerW - btnW - Px(8f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, OsDrawShared.White(0.07f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(10f), Px(8f)));
        var submitted = ImGui.InputTextWithHint("##echoWatchInput", Loc.T("os.echo_home_watch_hint"),
            ref _watchInput, WatchInputMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        ImGui.SameLine(0f, Px(8f));
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }
        ImGui.PushStyleColor(ImGuiCol.Button, t.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.AccentLight);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.AccentDark);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        var clicked = Button($"{Loc.T("os.echo_home_watch_btn")}##echoWatchGo", new Vector2(btnW, rowH));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        if (_watchError is { } message)
        {
            ImGui.SetCursorScreenPos(tl + new Vector2(pad, errorY + Px(8f)));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
            ImGui.TextColored(UiColors.Danger, message);
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, br.Y));
        if (enabled && (clicked || submitted))
        {
            Watch();
        }
    }

    private void DrawRuntimeNotice(float winW)
    {
        DrawInfoCallout(Loc.T("os.echo_home_runtime_missing"), UiColors.Amber, FontAwesomeIcon.CloudDownloadAlt);
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        PushThemeButton(ThemeService.Current);
        if (Button($"{Loc.T("os.echo_home_runtime_setup")}##echoSetup",
                new Vector2(winW - Px(PadX) * 2f, Px(RowHeight))))
        {
            _openSetup();
        }
        PopThemeButton();
        ImGui.PopStyleVar();
        ImGui.Dummy(new Vector2(0f, Px(14f)));
    }

    private void DrawRoomActions(float winW)
    {
        var t = ThemeService.Current;
        var gap = Px(10f);
        var cardW = winW - Px(PadX) * 2f;
        var size = new Vector2(cardW, Px(NavCardHeight));
        var enabled = _hub.IsConnected && _host.RuntimeReady && !_busy;

        ImGui.SetCursorPosX(Px(PadX));
        if (DrawNavCard("##echoStartRoom", size, t.Accent, t.AccentDark, FontAwesomeIcon.Crown,
                Loc.T("os.echo_home_start_title"), Loc.T("os.echo_home_start_sub"), enabled))
        {
            _openRoom();
        }
        ImGui.Dummy(new Vector2(0f, gap));
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawNavCard("##echoJoinRoom", size, t.SecondaryStart, t.SecondaryEnd, FontAwesomeIcon.DoorOpen,
                Loc.T("os.echo_home_join_title"), Loc.T("os.echo_home_join_sub"), enabled))
        {
            _joinOpen = !_joinOpen;
            _joinError = null;
        }

        if (!_hub.IsConnected)
        {
            ImGui.Dummy(new Vector2(0f, Px(12f)));
            DrawInfoCallout(Loc.T("os.echo_home_offline"), UiColors.Muted, FontAwesomeIcon.ExclamationTriangle);
        }

        if (_joinOpen)
        {
            DrawJoinField(winW, enabled);
        }
    }

    private void DrawJoinField(float winW, bool enabled)
    {
        ImGui.Dummy(new Vector2(0f, Px(12f)));
        ImGui.SetCursorPosX(Px(PadX));
        DrawFieldLabel(Loc.T("os.echo_home_join_label"), ThemeService.Current);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var btnW = Px(78f);
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f - btnW - Px(8f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, OsDrawShared.White(0.07f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Px(10f), Px(8f)));
        bool submitted;
        using (UiFonts.H3?.Push())
        {
            submitted = ImGui.InputTextWithHint("##echoJoinCode", Loc.T("os.echo_home_join_hint"), ref _joinCode,
                EchoLimits.RoomCodeLength * 2,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsUppercase
                | ImGuiInputTextFlags.CharsNoBlank);
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        var ready = enabled && NormalizedJoinCode().Length == EchoLimits.RoomCodeLength;
        ImGui.SameLine(0f, Px(8f));
        if (!ready)
        {
            ImGui.BeginDisabled();
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        PushThemeButton(ThemeService.Current);
        var clicked = Button($"{Loc.T(_busy ? "os.echo_home_joining" : "os.echo_home_join_btn")}##echoJoinGo",
            new Vector2(btnW, Px(RowHeight)));
        PopThemeButton();
        ImGui.PopStyleVar();
        if (!ready)
        {
            ImGui.EndDisabled();
        }

        if (_joinError is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            DrawCenteredParagraph(error, winW - Px(48f), UiColors.Danger);
        }

        if (ready && (clicked || submitted))
        {
            SubmitJoin();
        }
    }

    private void DrawCurrentRoomCard(OsAppContext ctx, float winW, EchoRoomSnapshotDto room)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var pad = Px(14f);
        var cardH = Px(RoomCardHeight);
        var lineH = ImGui.GetTextLineHeight();

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + Px(PadX), origin.Y);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var opened = ImGui.InvisibleButton("##echoRoomCard", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        OsDrawShared.RoundedGradient(dl, tl, br, Px(CardRounding), t.Accent, t.AccentDark, hovered ? 1f : 0.92f);
        if (hovered)
        {
            dl.AddRect(tl, br, OsDrawShared.White(0.30f), Px(CardRounding), ImDrawFlags.None, Px(1.2f));
        }

        var watermarkPx = Px(46f);
        var watermarkSz = IconDraw.Measure(FontAwesomeIcon.Film, watermarkPx);
        IconDraw.Add(dl, FontAwesomeIcon.Film, watermarkPx,
            new Vector2(br.X - watermarkSz.X - Px(10f), tl.Y + Px(8f)), OsDrawShared.White(0.10f));

        dl.AddText(tl + new Vector2(pad, pad), OsDrawShared.White(0.72f), Loc.T("os.echo_home_in_room"));
        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(pad, pad + lineH + Px(2f)),
                OsDrawShared.White(0.98f), TruncateToWidth(room.Name, cardW - pad * 2f - watermarkSz.X));
        }

        var dotY = br.Y - Px(18f);
        var pulse = ctx.ReduceMotion ? 1f : 0.72f + 0.28f * MathF.Sin((float)ImGui.GetTime() * 2.4f);
        dl.AddCircleFilled(new Vector2(tl.X + pad + Px(4f), dotY), Px(4f),
            ImGui.ColorConvertFloat4ToU32(UiColors.LiveGreen with { W = pulse }));
        dl.AddText(new Vector2(tl.X + pad + Px(14f), dotY - lineH * 0.5f), OsDrawShared.White(0.80f),
            Loc.T("os.echo_home_members", room.Members.Length, EchoLimits.MaxMembers));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, br.Y));
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        var gap = Px(10f);
        var halfW = (cardW - gap) * 0.5f;
        var playable = _host.RuntimeReady;
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        ImGui.PushStyleColor(ImGuiCol.Button, t.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.AccentLight);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.AccentDark);
        if (!playable)
        {
            ImGui.BeginDisabled();
        }
        if (Button($"{Loc.T("os.echo_home_open")}##echoOpenPlayer", new Vector2(halfW, Px(ActionHeight))))
        {
            _host.OpenRoom();
        }
        if (!playable)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine(0f, gap);
        PushDangerButton();
        if (Button($"{Loc.T("os.echo_home_leave")}##echoLeaveRoom", new Vector2(halfW, Px(ActionHeight))) && !_busy)
        {
            Leave(room.Id);
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        if (opened)
        {
            _openRoom();
        }
    }

    /// <summary>A gradient action tile: icon chip, title, one-line subtitle. A disabled tile dims and swallows
    /// the click rather than using ImGui's disabled stack, which a draw-list tile ignores.</summary>
    private static bool DrawNavCard(string id, Vector2 size, Vector4 gradTop, Vector4 gradBottom,
        FontAwesomeIcon icon, string title, string subtitle, bool enabled)
    {
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = enabled && ImGui.IsItemHovered();
        if (enabled)
        {
            HandOnHover();
        }

        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetItemRectMin();
        var br = ImGui.GetItemRectMax();
        var rounding = Px(CardRounding);
        var alpha = enabled ? (hovered ? 1f : 0.94f) : 0.38f;
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, gradTop, gradBottom, alpha);

        var watermarkPx = MathF.Min(size.Y * 0.62f, Px(52f));
        var watermarkSz = IconDraw.Measure(icon, watermarkPx);
        IconDraw.Add(dl, icon, watermarkPx, new Vector2(br.X - watermarkSz.X - Px(6f), tl.Y + Px(6f)),
            OsDrawShared.White(0.09f));

        var chipR = Px(15f);
        var chipC = tl + new Vector2(Px(12f) + chipR, Px(12f) + chipR);
        dl.AddCircleFilled(chipC, chipR * 1.9f, OsDrawShared.White(0.05f));
        dl.AddCircleFilled(chipC, chipR, OsDrawShared.Black(0.22f));
        IconDraw.AddCentered(dl, icon, chipR * 1.1f, chipC, OsDrawShared.White(0.95f));

        if (hovered)
        {
            dl.AddRect(tl, br, OsDrawShared.White(0.30f), rounding, ImDrawFlags.None, Px(1.2f));
        }

        dl.AddText(new Vector2(tl.X + Px(12f), br.Y - Px(10f) - ImGui.GetTextLineHeight()),
            OsDrawShared.White(0.74f), TruncateToWidth(subtitle, size.X - Px(24f)));

        using (UiFonts.H3?.Push())
        {
            var titleX = chipC.X + chipR + Px(10f);
            var titleText = TruncateToWidth(title, br.X - Px(12f) - titleX);
            var titleSz = ImGui.CalcTextSize(titleText);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(titleX, chipC.Y - titleSz.Y * 0.5f),
                OsDrawShared.White(0.98f), titleText);
        }
        return clicked && enabled;
    }

    private void Watch()
    {
        if (!TryNormalizeVideoRef(_watchInput, out var videoId))
        {
            // A playlist has nowhere to go outside a room: watching alone is one video, not a queue.
            _watchError = EchoPlaylistIds.TryParse(_watchInput, out _, out _)
                ? Loc.T("os.echo_home_watch_playlist")
                : Loc.T("os.echo_home_watch_invalid");
            return;
        }
        _watchError = null;
        _host.OpenSolo(videoId);
    }

    private string NormalizedJoinCode()
    {
        var sb = new StringBuilder(EchoLimits.RoomCodeLength);
        foreach (var c in _joinCode)
        {
            if (char.IsLetterOrDigit(c) && sb.Length < EchoLimits.RoomCodeLength)
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }
        return sb.ToString();
    }

    private void SubmitJoin()
    {
        var code = NormalizedJoinCode();
        _joinError = null;
        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _state.ApplySnapshot(await _hub.JoinEchoRoomAsync(code).ConfigureAwait(false));
                _joined = true;
            }
            catch (Exception ex)
            {
                _joinError = HubErrorText.Localize(ex);
                UiHost.Log.Warning(ex, "[EchoHome] Joining a room failed.");
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private void Leave(Guid roomId)
    {
        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.LeaveEchoRoomAsync(roomId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[EchoHome] Leaving the room failed.");
            }
            finally
            {
                _state.Clear();
                _busy = false;
            }
        });
    }

    /// <summary>Accepts a bare video id or any of the YouTube link shapes, so a junk paste is caught here rather
    /// than opening a player window that fails.</summary>
    private static bool TryNormalizeVideoRef(string input, out string videoId)
    {
        videoId = string.Empty;
        var text = input.Trim();
        if (text.Length == 0)
        {
            return false;
        }
        if (IsVideoId(text))
        {
            videoId = text;
            return true;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && !Uri.TryCreate($"https://{text}", UriKind.Absolute, out uri))
        {
            return false;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Length > 0 && TakeVideoId(segments[0], out videoId);
        }
        if (!host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.StartsWith("v=", StringComparison.OrdinalIgnoreCase) && TakeVideoId(pair[2..], out videoId))
            {
                return true;
            }
        }
        return segments.Length >= 2
            && Array.Exists(PathVideoPrefixes, p => p.Equals(segments[0], StringComparison.OrdinalIgnoreCase))
            && TakeVideoId(segments[1], out videoId);
    }

    private static bool TakeVideoId(string candidate, out string videoId)
    {
        videoId = IsVideoId(candidate) ? candidate : string.Empty;
        return videoId.Length > 0;
    }

    private static bool IsVideoId(string text)
    {
        if (text.Length != VideoIdLength)
        {
            return false;
        }
        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }
        return true;
    }
}
