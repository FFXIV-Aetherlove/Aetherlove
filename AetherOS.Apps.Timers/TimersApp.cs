using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Timers;

/// <summary>The Timers app: server resets, timed activities with their real schedules, every character's
/// retainers and workshop fleet, custom timers and upcoming commitments, all with calendar hand-off and
/// reminder settings. Fully offline-capable; the one server touch is the commitments feed.</summary>
public sealed partial class TimersApp : IAetherApp
{
    internal static readonly Vector4 TileTopColor = new(0.44f, 0.36f, 0.94f, 1f);
    internal static readonly Vector4 TileBottomColor = new(0.18f, 0.10f, 0.48f, 1f);

    private const float PadX = 16f;
    private const float CardRounding = 18f;
    private const float RowHeight = 46f;
    private const string CalendarAppId = "calendar";
    private const int CalendarLeadMinutes = 15;
    private const string TourSeenKey = "tourSeen";

    private enum View { Main, Reminders, Tour }

    private readonly Func<string> _name;
    private readonly ITimersHost _host;
    private readonly ITimersRetainers _retainers;
    private readonly IAppStorage _storage;
    private readonly Screens.TourScreen _tour;
    private readonly EntranceAnimation _entrance = new();

    private View _view = View.Main;
    private View? _pendingView;
    private readonly IOsShell _shell;
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private bool _tourSeen;
    private bool _tourSeenLoaded;
    private ReminderConfig? _config;

    public TimersApp(Func<string> name, ITimersHost host, ITimersRetainers retainers, IAppCapabilities caps,
        IOsShell shell)
    {
        _name = name;
        _host = host;
        _retainers = retainers;
        _storage = caps.Storage("timers");
        _shell = shell;
        _tour = new Screens.TourScreen(FinishTour);
    }

    public string Id => "timers";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.HourglassHalf;
    public Vector4 TileTop => TileTopColor;
    public Vector4 TileBottom => TileBottomColor;

    /// <summary>Timers deliberately never badges. The only candidate metric was ready ventures and returned
    /// vessels across every character, and nothing the player does on the phone can drive that to zero:
    /// clearing it means logging into each alt and collecting, so for anyone with a few retainers the badge
    /// simply never goes away. Reminders are opt in per kind and speak through notifications instead, which
    /// the player can dismiss. Do not reintroduce a badge here without a number the player can actually
    /// clear from inside the app.</summary>
    public int Badge => 0;

    public bool HasSurface => true;

    public bool RequiresConnection => false;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        _entrance.Arm();
        _revealStamp = -1.0;
        _config = _host.GetReminderConfig();
        ReloadCustomTimers();
        MaybeRefreshCommitments(force: true);
        InvalidateViews();
        DismissOwnNotifications();
    }

    public void OnBackground()
    {
        _addOpen = false;
    }

    public void OnIntent(OsIntent intent)
    {
    }

    public void Draw(OsAppContext ctx)
    {
        _culture = ctx.Culture;
        if (_pendingView is { } pending)
        {
            _pendingView = null;
            if (_view != pending)
            {
                _view = pending;
                _entrance.Arm();
            }
        }

        if (_view != View.Tour && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }
        if (_view == View.Tour)
        {
            _tour.Draw(ctx);
            return;
        }

        EnsureViews(DateTime.UtcNow);

        if (_view == View.Reminders)
        {
            DrawReminders(ctx);
            return;
        }

        DrawHeader(ctx);
        var bodyTL = ImGui.GetCursorScreenPos();
        var bodySize = ImGui.GetContentRegionAvail();
        PushScrollbarStyle();
        var flags = _addOpen ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;
        using (var body = ImRaii.Child("##timersBody", new Vector2(0f, 0f), false, flags))
        {
            if (body)
            {
                _entrance.BeginFrame();
                DrawHero(ctx);
                DrawResetsCard(ctx);
                DrawActivitiesCard(ctx);
                DrawRetainersCard(ctx);
                DrawFleetCard(ctx);
                DrawCustomCard(ctx);
                DrawComingUpCard(ctx);
                ImGui.Dummy(new Vector2(0f, Px(16f)));
                _entrance.EndFrame();
            }
        }
        PopScrollbarStyle();

        if (_addOpen)
        {
            DrawAddOverlay(ctx, bodyTL, bodySize);
        }
    }

    private void DrawHeader(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var originX = ImGui.GetWindowPos().X;
        var rowTop = ImGui.GetCursorScreenPos().Y;
        var title = Loc.T("os.app_timers");

        float titleH;
        using (ctx.TitleFont?.Push())
        {
            titleH = ImGui.CalcTextSize(title).Y;
        }
        var rowH = MathF.Max(titleH, Px(30f));
        var centerY = rowTop + rowH * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(originX + Px(PadX), centerY - titleH * 0.5f));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, title);
        }

        DrawMenu(centerY);
        ImGui.SetCursorScreenPos(new Vector2(originX, rowTop + rowH));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    private void DrawMenu(float centerY)
    {
        const string popupId = "##timersMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, PadX, popupId, centerY: centerY);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            var reminders = Loc.T("os.timers_menu_reminders");
            var tour = Loc.T("os.timers_menu_tour");
            var w = AppHeader.MenuWidth(reminders, tour);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(FontAwesomeIcon.Bell, reminders, w, rowH))
            {
                _view = View.Reminders;
                _entrance.Arm();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.HourglassHalf, tour, w, rowH))
            {
                _view = View.Tour;
                _tour.OnShow();
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    private static readonly ReminderKind[] AllKinds = Enum.GetValues<ReminderKind>();

    /// <summary>Clears every timers notification from the OS center, widget and bell. Calendar-event
    /// alerts belong to the Calendar app and are left alone.</summary>
    private void DismissOwnNotifications()
    {
        foreach (var kind in AllKinds)
        {
            if (kind == ReminderKind.CalendarEvent)
            {
                continue;
            }
            _shell.DismissByTag(TimersTags.ForKind(kind));
        }
        foreach (var character in _retainers.Characters)
        {
            _shell.DismissByTag(TimersTags.ForVenture(character.ContentId));
            _shell.DismissByTag(TimersTags.ForFleet(character.ContentId));
        }
        if (_customTimers is { } timers)
        {
            foreach (var timer in timers)
            {
                _shell.DismissByTag(TimersTags.ForCustom(timer.Id));
            }
        }
    }

    private void OpenRemindersFromWidget()
    {
        _pendingView = View.Reminders;
        _shell.OpenApp(Id);
    }

    private bool ShouldAutoRunTour()
    {
        if (!_tourSeenLoaded)
        {
            _tourSeen = _storage.Get<bool?>(TourSeenKey) ?? false;
            _tourSeenLoaded = true;
        }
        return !_tourSeen;
    }

    private void FinishTour(bool openReminders)
    {
        _tourSeen = true;
        _storage.Set(TourSeenKey, (bool?)true);
        _view = openReminders ? View.Reminders : View.Main;
        _entrance.Arm();
        _revealStamp = -1.0;
    }
}
