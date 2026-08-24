using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Sdk;

/// <summary>One row of an app's home-screen widget: a title and a short right-aligned detail (e.g. an event
/// name and its start time).</summary>
public sealed record OsWidgetItem(string Title, string Detail);

/// <summary>A button on the second row of an app's home-screen widget (e.g. media transport). Drawn only
/// when the app also returns widget items.</summary>
public sealed record OsWidgetAction(FontAwesomeIcon Icon, string Tooltip, Action Invoke, bool Primary = false);

/// <summary>An app installed on the AetherOS home screen.</summary>
public interface IAetherApp
{
    string Id { get; }

    /// <summary>Display name under the tile. Resolved per frame so language switches apply live.</summary>
    string Name { get; }

    FontAwesomeIcon Icon { get; }

    /// <summary>Tile gradient, top and bottom colors.</summary>
    Vector4 TileTop { get; }
    Vector4 TileBottom { get; }

    /// <summary>A full-square, opaque icon image that replaces the gradient-plus-glyph tile. The shell applies
    /// the rounded corners, shadow, badge, and hover ring, so the image is edge-to-edge with no baked rounding
    /// or shadow. Null (default) keeps the FontAwesome-on-gradient tile. Resolve the handle per frame; never
    /// cache a raw shared-texture handle across frames.</summary>
    ImTextureID? TileImage => null;

    /// <summary>Unread count shown on the tile; 0 hides the badge. OS-posted badges are added on top by the shell.</summary>
    int Badge { get; }

    /// <summary>False hides the app from the home screen (e.g. a server kill switch). Deep links keep working.</summary>
    bool Available => true;

    /// <summary>True when the app renders its own surface via <see cref="Draw"/>; false when <see cref="Open"/>
    /// navigates to a host-owned screen instead.</summary>
    bool HasSurface { get; }

    /// <summary>Host-side navigation entry for non-surface apps. Unused when <see cref="HasSurface"/> is true.</summary>
    void Open();

    /// <summary>Renders the app surface for the current frame. Only called when <see cref="HasSurface"/> is true.</summary>
    void Draw(OsAppContext ctx);

    /// <summary>Receives a cross-app payload; the shell opens the app right after. Ignore unknown types. A share
    /// target handles the reserved <see cref="ShareIntent.Type"/> here via <see cref="ShareIntent.TryUnwrap"/>.</summary>
    void OnIntent(OsIntent intent);

    /// <summary>The share content-type keys (see <see cref="ShareTypes"/>) this app can receive. Non-empty makes
    /// the app a share target: it appears in the OS share sheet for those types and receives the picked item as
    /// an <see cref="ShareIntent"/> in <see cref="OnIntent"/>. Default: not a target.</summary>
    IReadOnlyList<string> AcceptedShareTypes => Array.Empty<string>();

    /// <summary>Label shown under this app's tile on the share sheet for the given share type. Override when the
    /// app does something more specific with the content than "open" (e.g. Settings turning a photo into the
    /// wallpaper). Default: the app name.</summary>
    string ShareTargetLabel(string shareType) => Name;

    /// <summary>Rows for the home screen's widgets page (e.g. the calendar's next events). A non-empty list
    /// renders as a card headed by the app's tile and name; tapping it opens the app. Queried every frame the
    /// widgets page is visible, so keep it cheap. Default: no widget.</summary>
    IReadOnlyList<OsWidgetItem> WidgetItems => Array.Empty<OsWidgetItem>();

    /// <summary>Optional buttons drawn as a second row inside the widget card, for apps whose widget is
    /// worth acting on without opening them. Ignored when <see cref="WidgetItems"/> is empty; queried every
    /// frame the widgets page is visible, so keep it cheap.</summary>
    IReadOnlyList<OsWidgetAction> WidgetActions => Array.Empty<OsWidgetAction>();

    /// <summary>True while the surface app is running an immersive flow (e.g. first-run onboarding), so the
    /// host suppresses the OS status bar until the app clears it.
    /// <para>It does NOT suppress the home indicator: leaving must always be possible, or an app that asks
    /// for an account holds the whole phone hostage until one is made. An app whose flow can be walked out
    /// of half-way is responsible for restarting it cleanly on the next open (both onboardings reset in
    /// their own <c>OnShow</c>).</para></summary>
    bool LocksShell => false;

    /// <summary>True while the app wants drags on its surface for itself, so the host stops ImGui reading them as
    /// "move the phone". Needed by anything that drags to aim: with no ImGui item under the cursor a press on the
    /// surface moves the window, and claiming one instead would take the active id that the keyboard capture
    /// needs. Only honoured while the app is the visible surface.</summary>
    bool LocksWindowDrag => false;

    /// <summary>True when the app is unusable without the AetherLove connection: while the hub is down the host
    /// draws the offline panel in place of the surface. Offline-capable apps keep the default and stay usable.</summary>
    bool RequiresConnection => false;

    /// <summary>True when the app is backed by the AetherAccount, so a disabled account gets the ban card instead
    /// of the surface. Defaults to <see cref="RequiresConnection"/> because the two normally coincide; an app that
    /// serves account-backed and purely local content side by side overrides them apart, or a banned user is told
    /// they have a network problem.</summary>
    bool UsesAccount => RequiresConnection;

    /// <summary>Called when the phone minimises or the app goes background; drop transient state.</summary>
    void OnBackground()
    {
    }

    /// <summary>Called when the app becomes visible (except brief pauses like combat), always before Draw; use to re-arm animations or refresh data.</summary>
    void OnForeground()
    {
    }

    /// <summary>Optional app-owned localization packs (ISO language code to key-to-text table); the host merges them so keys resolve via <see cref="OsAppContext.Localize"/> with language-then-English fallback.</summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Strings => null;
}
