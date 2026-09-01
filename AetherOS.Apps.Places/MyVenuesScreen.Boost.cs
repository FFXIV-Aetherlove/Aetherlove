using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Places;

/// <summary>The venue editor's boost row: what the boost is doing right now, and the one button that either
/// spends a boost the account already holds or sends the owner to the shelf that sells them.</summary>
public partial class MyVenuesScreen
{
    private readonly BoostConfirmOverlay _boostConfirm = new();
    private volatile int _boostsOwned = -1;
    private volatile bool _boostsLoading;
    private string? _boostDone;
    private float _boostDoneTimer;

    private void StartBoostCountFetch()
    {
        if (_boostsLoading)
        {
            return;
        }
        _boostsLoading = true;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var mine = await _host.GetMyBoostsAsync(ct).ConfigureAwait(false);
                _boostsOwned = mine?.VenueBoosts ?? 0;
            }
            catch (Exception)
            {
                _boostsOwned = 0;
            }
            finally
            {
                _boostsLoading = false;
            }
        }, ct);
    }

    private void DrawBoostRow(float w)
    {
        var venue = _venues?.FirstOrDefault(v => v.Id == _editId);
        if (venue is null)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var style = (BoostStyle)venue.BoostStyle;
        var active = BoostRules.IsActive(venue.BoostedUntilUtc, DateTimeOffset.UtcNow);
        if (active)
        {
            var origin = ImGui.GetCursorScreenPos();
            var pillW = BoostUi.DrawBoostedPill(dl, origin, style);
            var remaining = BoostUi.RemainingLabel(venue.BoostedUntilUtc);
            if (remaining is not null)
            {
                dl.AddText(origin + new Vector2(pillW + Px(8f), Px(3f)),
                    ImGui.GetColorU32(UiColors.Subtle), remaining);
            }
            ImGui.Dummy(new Vector2(w, ImGui.GetTextLineHeight() + Px(10f)));
        }

        if (_boostDoneTimer > 0f && _boostDone is not null)
        {
            ImGui.TextColored(UiColors.Success, _boostDone);
            ImGui.Spacing();
        }

        var owned = _boostsOwned;
        var label = owned > 0 || owned < 0
            ? Loc.T("os.boost_venue_action")
            : Loc.T("os.boost_get");
        if (SharedUiHelpers.Button($"{label}##venueBoost", new Vector2(w, Px(32f))))
        {
            if (owned > 0)
            {
                _boostConfirm.Open(BoostTarget.Venue, venue.Id, venue.Name, venue.BoostedUntilUtc, owned);
            }
            else
            {
                _shell?.SendIntent("store", OsIntents.CreateStoreProduct(
                    (short)StoreItemKind.Powerup, StoreItemRefs.PowerupVenueBoost));
            }
        }
        ImGui.Spacing();
    }

    /// <summary>Drawn last so the overlay layers over the editor. Spends the boost on confirm.</summary>
    private void DrawBoostOverlay()
    {
        if (_boostDoneTimer > 0f)
        {
            _boostDoneTimer -= ImGui.GetIO().DeltaTime;
        }
        if (!_boostConfirm.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize()))
        {
            return;
        }

        var venueId = _boostConfirm.TargetId;
        var style = _boostConfirm.Style;
        _boostConfirm.Busy = true;
        _boostConfirm.Error = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _host.ApplyBoostAsync(BoostTarget.Venue, venueId, style, ct).ConfigureAwait(false);
                _boostsOwned = result.Remaining;
                _boostDone = Loc.T("os.boost_done",
                    BoostUi.FormatRemaining(result.BoostedUntilUtc - DateTimeOffset.UtcNow));
                _boostDoneTimer = 4f;
                _boostConfirm.Close();
                StartListFetch();
            }
            catch (Exception ex)
            {
                _boostConfirm.Busy = false;
                _boostConfirm.Error = HubErrorText.Localize(ex);
            }
        }, ct);
    }
}
