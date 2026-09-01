using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The Store's half of the boost flow: a sheet listing the venues and ads the account could spend
/// a boost on, and the same confirm the owner screens use. Buying and spending are one errand here, which
/// is why the shop offers the spending as well as the buying.</summary>
internal sealed class BoostsSheet(IStoreHost host, StoreState state)
{
    private readonly BoostConfirmOverlay _confirm = new();
    private float _panelH;
    private string? _done;
    private float _doneTimer;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        IsOpen = true;
        _panelH = 0f;
        state.RefreshBoosts();
    }

    public void Close()
    {
        IsOpen = false;
        _confirm.Close();
    }

    public void Draw()
    {
        if (_doneTimer > 0f)
        {
            _doneTimer -= ImGui.GetIO().DeltaTime;
        }
        if (!IsOpen)
        {
            return;
        }
        if (_confirm.IsOpen)
        {
            DrawConfirm();
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Close();
            return;
        }

        var boosts = state.Boosts;
        var dismissed = SharedUiHelpers.DrawPageOverlayPanel(
            "storeBoosts", ImGui.GetWindowPos(), ImGui.GetWindowSize(), ref _panelH, Px(300f), innerW =>
            {
                ModalUi.Header(innerW, FontAwesomeIcon.Bolt, Loc.T("os.boost_shelf"), StorePalette.Blue);

                if (boosts is null)
                {
                    ImGui.TextColored(UiColors.Muted, Loc.T("common.loading"));
                    return;
                }

                ImGui.TextColored(UiColors.Subtle, Loc.T("os.boost_pick"));
                ImGui.Spacing();
                if (_doneTimer > 0f && _done is not null)
                {
                    ImGui.TextColored(UiColors.Success, _done);
                    ImGui.Spacing();
                }

                var venues = boosts.Targets.Where(t => t.Target == (short)BoostTarget.Venue).ToArray();
                var ads = boosts.Targets.Where(t => t.Target == (short)BoostTarget.Levemete).ToArray();
                if (venues.Length == 0 && ads.Length == 0)
                {
                    ImGui.PushTextWrapPos(innerW);
                    ImGui.TextColored(UiColors.Muted, Loc.T("os.boost_no_targets"));
                    ImGui.PopTextWrapPos();
                }
                DrawGroup(innerW, Loc.T("os.boost_venues"), venues, boosts.VenueBoosts, BoostTarget.Venue);
                DrawGroup(innerW, Loc.T("os.boost_ads"), ads, boosts.LevemeteBoosts, BoostTarget.Levemete);

                ImGui.Spacing();
                if (ModalUi.Button($"{Loc.T("common.close")}##boostsClose", innerW))
                {
                    Close();
                }
            });
        if (dismissed)
        {
            Close();
        }
    }

    private void DrawGroup(float innerW, string title, BoostTargetDto[] targets, int owned, BoostTarget target)
    {
        if (targets.Length == 0)
        {
            return;
        }
        ImGui.Spacing();
        ImGui.TextColored(StorePalette.BlueLight, $"{title} · {owned}");
        ImGui.Spacing();
        foreach (var row in targets)
        {
            DrawTargetRow(innerW, row, owned, target);
        }
    }

    private void DrawTargetRow(float innerW, BoostTargetDto row, int owned, BoostTarget target)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowH = Px(44f);
        var tl = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##boostTarget{row.Id:N}", new Vector2(innerW, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var br = tl + new Vector2(innerW, rowH);
        var rounding = Px(9f);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.09f : 0.05f)), rounding);

        var lineH = ImGui.GetTextLineHeight();
        var textX = tl.X + Px(10f);
        dl.AddText(new Vector2(textX, tl.Y + Px(6f)), 0xFFFFFFFFu,
            SharedUiHelpers.TruncateToWidth(row.Name, innerW - Px(96f)));

        var second = BoostUi.RemainingLabel(row.BoostedUntilUtc)
            ?? (row.Subtitle.Length > 0 ? row.Subtitle : string.Empty);
        if (second.Length > 0)
        {
            dl.AddText(new Vector2(textX, tl.Y + Px(6f) + lineH + Px(2f)),
                ImGui.GetColorU32(UiColors.Subtle), SharedUiHelpers.TruncateToWidth(second, innerW - Px(96f)));
        }

        var label = Loc.T("os.boost_go");
        var labelSize = ImGui.CalcTextSize(label);
        var chipW = labelSize.X + Px(18f);
        var chipH = lineH + Px(8f);
        var chipTL = new Vector2(br.X - chipW - Px(8f), tl.Y + ((rowH - chipH) * 0.5f));
        var live = owned > 0;
        var chipCol = live ? StorePalette.Blue : UiColors.Muted;
        dl.AddRectFilled(chipTL, chipTL + new Vector2(chipW, chipH),
            ImGui.GetColorU32(chipCol with { W = 0.24f }), chipH * 0.5f);
        dl.AddText(chipTL + new Vector2(Px(9f), Px(4f)), ImGui.GetColorU32(chipCol), label);

        if (pressed && live)
        {
            _confirm.Open(target, row.Id, row.Name, row.BoostedUntilUtc, owned);
        }
        ImGui.Dummy(new Vector2(innerW, Px(4f)));
    }

    private void DrawConfirm()
    {
        if (!_confirm.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize()))
        {
            return;
        }
        var target = _confirm.Target;
        var targetId = _confirm.TargetId;
        var style = _confirm.Style;
        _confirm.Busy = true;
        _confirm.Error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await host.ApplyBoostAsync(target, targetId, style).ConfigureAwait(false);
                _done = Loc.T("os.boost_done",
                    BoostUi.FormatRemaining(result.BoostedUntilUtc - DateTimeOffset.UtcNow));
                _doneTimer = 4f;
                _confirm.Close();
                state.RefreshBoosts();
            }
            catch (Exception ex)
            {
                _confirm.Busy = false;
                _confirm.Error = HubErrorText.Localize(ex);
            }
        });
    }
}
