using System;
using System.Numerics;
using AetherLove;
using AetherLove.Services.Hangouts;
using AetherLove.Shared.Hangouts;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Hangouts;

/// <summary>The hangout directory app: the public directory plus the user's create/manage view, the shared
/// detail overlay, and the match share picker. Fully self-contained; the host supplies the plugin bridge.</summary>
public sealed class HangoutsApp : IAetherApp, IAppSettings
{
    private enum View { Directory, Manage }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IHangoutsHost _host;
    private readonly AetherLove.Os.ISocialBridge _social;
    private readonly HangoutsScreen _screen;
    private readonly HangoutDetailOverlay _detail;
    private readonly MyHangoutView _manage;
    private IShareService? _shareSvc;
    private View _view = View.Directory;

    public HangoutsApp(Func<string> name, Func<bool> available, IHangoutsHost host, AetherLove.Os.ISocialBridge social, HangoutStateService state)
    {
        _name = name;
        _available = available;
        _host = host;
        _social = social;
        _detail = new HangoutDetailOverlay(host, social, state);
        _manage = new MyHangoutView(host, state, GoDirectory);
        _detail.ShareHandler = OfferHangout;
        _manage.ShareHandler = OfferHangout;
        _screen = new HangoutsScreen(host, state, OpenDetail, OpenManage);
    }

    private void OfferHangout(HangoutSummaryDto h)
    {
        _shareSvc?.Offer(new ShareItem
        {
            Type = ShareTypes.Hangout,
            RefId = h.Id.ToString("D"),
            Title = h.Description,
            Subtitle = $"{h.DataCenter} · {h.World}",
            SourceAppId = "hangouts",
        }, title: h.Description);
    }

    public string Id => "hangouts";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Bullhorn;
    public Vector4 TileTop => new(0.99f, 0.65f, 0.25f, 1f);
    public Vector4 TileBottom => new(0.75f, 0.32f, 0.08f, 1f);
    public int Badge => 0;
    public bool Available => _available();
    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground() => ShowCurrentView();

    public void Draw(OsAppContext ctx)
    {
        _shareSvc = ctx.Capabilities.Share;

        if (_host.TakePendingOpenHangout() is { } pending)
        {
            OpenDetail(pending.Hangout, pending.FromChat);
        }

        if (_view == View.Manage)
        {
            _manage.Draw();
        }
        else
        {
            _screen.Draw();
        }
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        _detail.Draw(winPos, winSize);
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenManage)
        {
            OpenManage();
        }
    }

    private void OpenDetail(HangoutSummaryDto hangout) => OpenDetail(hangout, fromChat: false);

    private void OpenDetail(HangoutSummaryDto hangout, bool fromChat)
    {
        _view = View.Directory;
        _detail.Open(hangout, fromChat);
    }

    private void OpenManage()
    {
        _view = View.Manage;
        _manage.OnShow();
    }

    private void GoDirectory()
    {
        _view = View.Directory;
        _screen.OnShow();
    }

    private void ShowCurrentView()
    {
        if (_view == View.Manage)
        {
            _manage.OnShow();
        }
        else
        {
            _screen.OnShow();
        }
    }

    /// <summary>The hangout notification and match-list preferences. Hosted as a page inside the OS Settings app
    /// (<paramref name="onBack"/> returns to its app list); the four toggles persist to <see cref="UiHost.Configuration"/>.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        const float padX = 16f;

        if (onBack != null)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.SetCursorPosX(Px(padX));
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), ctx.Localize("settings.back_arrow"), FontAwesomeIcon.Cog))
            {
                onBack();
            }
            ImGui.Spacing();
        }

        DrawSubpageHeading(ctx.Localize("settings.hangouts_heading"), padX);

        var scrollH = ImGui.GetContentRegionAvail().Y;
        using var scroll = ImRaii.Child("##hangoutSettings", new Vector2(0f, scrollH), false);
        if (!scroll.Success)
        {
            return;
        }

        var cfg = UiHost.Configuration.Hangouts;
        ImGui.Spacing();
        SettingCheckbox(padX, ctx.Localize("settings.hangout_notify_rsvp"),
            () => cfg.NotifyRsvp, v => cfg.NotifyRsvp = v);
        SettingCheckbox(padX, ctx.Localize("settings.hangout_notify_ended"),
            () => cfg.NotifyEnded, v => cfg.NotifyEnded = v);
        SettingCheckbox(padX, ctx.Localize("settings.hangout_notify_match"),
            () => cfg.NotifyMatchStarted, v => cfg.NotifyMatchStarted = v);
        ImGui.Spacing();
    }

}
