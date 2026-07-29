using System;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Levemetes;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float LevemeteCardH = 120f;

    private sealed record LevemeteCardVisual(LevemeteCardDto? Card, ISharedImmediateTexture? Tex);

    // One fetched card per ad id (session cache); null Card marks a delisted/removed ad.
    private readonly ConcurrentDictionary<Guid, LevemeteCardVisual> _levemeteCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _levemeteCardFetches = new();

    private void ResetFailedLevemeteCards()
    {
        foreach (var kv in _levemeteCards)
        {
            if (kv.Value.Card is null)
            {
                _levemeteCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartLevemeteCardFetch(Guid adId)
    {
        if (_levemeteCards.ContainsKey(adId) || !_levemeteCardFetches.TryAdd(adId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetLevemeteCardAsync(adId, CancellationToken.None).ConfigureAwait(false);
                ISharedImmediateTexture? tex = null;
                if (dto?.CoverWebp is { Length: > 0 })
                {
                    var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "LevemetesCache");
                    tex = AvatarDiskCache.Store(cacheDir, $"chatleve_{adId:N}", dto.CoverWebp);
                }
                _levemeteCards[adId] = new LevemeteCardVisual(dto, tex);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] Levemete card fetch failed for {adId}.");
                _levemeteCards[adId] = new LevemeteCardVisual(null, null);
            }
            finally
            {
                _levemeteCardFetches.TryRemove(adId, out _);
            }
        });
    }

    internal static string LevemeteCategoryLabel(short category) =>
        Enum.IsDefined((LevemeteCategory)category)
            ? Loc.T($"chat.leve_cat_{category}")
            : Loc.T("chat.leve_cat_unknown");

    internal static string LevemeteKindLabel(short kind) =>
        Loc.T(kind == (short)LevemeteKind.Offering ? "chat.leve_kind_offering" : "chat.leve_kind_looking");

    /// <summary>A shared classified ad rendered as a card in place of a bubble; clicking deep-links into the
    /// Levemetes detail, with back returning to this chat.</summary>
    private void DrawLevemeteCardMessage(DisplayedMessage msg, Guid adId, float windowWidth, bool isGroupEnd)
    {
        StartLevemeteCardFetch(adId);
        _levemeteCards.TryGetValue(adId, out var visual);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(LevemeteCardH);

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var left = msg.IsOwn ? cursorPos.X + windowWidth - cardW - Px(10) : cursorPos.X + Px(10);
        var tl = new Vector2(left, cursorPos.Y + entryDy);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##leveCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        if (visual?.Card is { } card)
        {
            var wrap = visual.Tex?.GetWrapOrDefault();
            if (wrap != null)
            {
                var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, cardW, cardH);
                dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(14f), ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
            }

            dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(70f)), br,
                0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            var textX = tl.X + Px(14f);
            var textMaxW = cardW - Px(28f);
            float nameH;
            using (UiFonts.H3?.Push())
            {
                nameH = ImGui.GetFontSize();
                dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(52f)),
                    0xFFFFFFFFu, TruncateToWidth(card.Title, textMaxW));
            }
            var line2 = $"{LevemeteKindLabel(card.Kind)} · {LevemeteCategoryLabel(card.Category)}";
            dl.AddText(new Vector2(textX, br.Y - Px(52f) + nameH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(line2, textMaxW - Px(12f)));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("chat.leve_card_view"));
            }
            if (clicked)
            {
                _levemeteShareCtx.PendingOpenLevemeteId = adId;
                _levemeteShareCtx.PendingOpenReturnApp = null;
                _shell.Shell?.OpenApp("levemetes");
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = visual is null ? Loc.T("chat.leve_card_loading") : Loc.T("chat.leve_card_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(tl + (new Vector2(cardW, cardH) - textSz) * 0.5f, ImGui.GetColorU32(UiColors.Muted), text);
        }

        if (isGroupEnd)
        {
            var local = msg.SentAt.LocalDateTime;
            var seenSuffix = msg.IsOwn && msg.ReadByOtherAtUtc is not null ? Loc.T("chat.seen_suffix") : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = msg.IsOwn ? tl.X + cardW - timeSize.X : tl.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, br.Y + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }
}
