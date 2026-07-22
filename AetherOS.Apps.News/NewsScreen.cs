using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.News;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.News;

/// <summary>Daily Eorzean's surface: a gazette masthead over a stack of announcement cards, and a reader
/// detail view. Server-side seen state drives the unread accents; opening an entry marks it seen.</summary>
public sealed class NewsScreen
{
    private enum View
    {
        List,
        Detail,
    }

    private const float Pad = 6f;

    private readonly INewsHost _host;
    private IOsShell? _shell;
    private IShareService? _share;
    private readonly Widgets.NewsBodyRenderer _renderer = new();
    private readonly EntranceAnimation _listEntrance = new();
    private readonly EntranceAnimation _detailEntrance = new();

    private View _view = View.List;
    private bool _seeded;
    private CancellationTokenSource _cts = new();

    private volatile NewsSummaryDto[]? _list;
    private volatile bool _listLoading;
    private volatile string? _listError;
    private DateTimeOffset _listFetchedAtUtc;

    private volatile NewsDto? _entry;
    private volatile bool _entryLoading;
    private volatile bool _entryMissing;
    private volatile string? _entryError;
    private bool _isPreview;

    private readonly object _pendingLock = new();
    private Guid? _pendingOpenId;
    private bool _pendingPreview;
    private string? _pendingReturnApp;

    /// <summary>Origin app of the open entry ("aetherlove", "messenger"); the entry's back button returns
    /// there instead of the news list. Null for entries opened from within the app.</summary>
    private string? _entryReturnApp;

    // Entering the app marks everything seen (the tile badge clears), so the "new" card accents come from
    // this session-local capture of what was unseen at that moment, not the live snapshot.
    private readonly HashSet<Guid> _accented = new();
    private readonly List<string> _pendingDismissTags = new();
    private NewsSummaryDto[]? _knownAtOpen;

    public NewsScreen(INewsHost host)
    {
        _host = host;
    }

    /// <summary>Queues a deep link from an intent; consumed at the start of the next frame so nothing touches
    /// ImGui off the UI thread (the test push arrives on the hub callback thread).</summary>
    public void RequestOpenEntry(Guid id, bool preview, string? returnApp = null)
    {
        lock (_pendingLock)
        {
            _pendingOpenId = id;
            _pendingPreview = preview;
            _pendingReturnApp = returnApp;
        }
    }

    public void OnForeground()
    {
        _entryReturnApp = null;
        _listEntrance.Arm();

        // Capture the unseen set BEFORE marking, so the instant-paint seed and the accents survive the clear.
        var known = _host.KnownNews;
        if (known.Count > 0)
        {
            _knownAtOpen ??= known.OrderByDescending(n => n.PublishedAtUtc).ToArray();
            foreach (var n in known)
            {
                _accented.Add(n.Id);
                _pendingDismissTags.Add($"news:{n.Id:N}");
            }
            _host.MarkAllSeen();
        }

        if (_seeded && (_list is null || DateTimeOffset.UtcNow - _listFetchedAtUtc > TimeSpan.FromMinutes(2)))
        {
            StartLoadList();
        }
    }

    public void Draw(OsAppContext ctx)
    {
        _shell = ctx.Shell;
        _share = ctx.Capabilities.Share;

        if (_pendingDismissTags.Count > 0)
        {
            foreach (var tag in _pendingDismissTags)
            {
                _shell.DismissByTag(tag);
            }
            _pendingDismissTags.Clear();
        }

        if (!_seeded)
        {
            _seeded = true;
            if (_knownAtOpen is { Length: > 0 })
            {
                _list = _knownAtOpen;
            }
            StartLoadList();
            _listEntrance.Arm();
        }

        ConsumePendingDeepLink();

        switch (_view)
        {
            case View.Detail:
                DrawDetail();
                break;
            default:
                DrawList();
                break;
        }
    }

    private void ConsumePendingDeepLink()
    {
        Guid? id;
        bool preview;
        string? returnApp;
        lock (_pendingLock)
        {
            id = _pendingOpenId;
            preview = _pendingPreview;
            returnApp = _pendingReturnApp;
            _pendingOpenId = null;
            _pendingReturnApp = null;
        }
        if (id is { } gid)
        {
            _entryReturnApp = returnApp;
            OpenEntry(gid, preview);
        }
    }

    private void StartLoadList()
    {
        if (_listLoading)
        {
            return;
        }
        _listLoading = true;
        _listError = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var list = await _host.GetNewsListAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _list = list.OrderByDescending(n => n.PublishedAtUtc).ToArray();
                _listFetchedAtUtc = DateTimeOffset.UtcNow;
                _listEntrance.Arm();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[NewsScreen] News list fetch failed.");
                _listError = HubErrorText.Localize(ex);
            }
            finally
            {
                _listLoading = false;
            }
        }, ct);
    }

    private void OpenEntry(Guid id, bool preview)
    {
        _view = View.Detail;
        _isPreview = preview;
        if (!preview)
        {
            _host.MarkSeen(id);
            _shell?.DismissByTag($"news:{id:N}");
        }
        _detailEntrance.Arm();
        LoadEntry(id, preview);
    }

    private void LoadEntry(Guid id, bool preview)
    {
        _entry = null;
        _entryMissing = false;
        _entryError = null;
        _entryLoading = true;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = preview
                    ? await _host.GetNewsPreviewAsync(id, ct).ConfigureAwait(false)
                    : await _host.GetNewsAsync(id, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _entry = dto;
                _entryMissing = dto is null;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[NewsScreen] News entry fetch failed.");
                _entryError = HubErrorText.Localize(ex);
            }
            finally
            {
                _entryLoading = false;
            }
        }, ct);
    }

    /// <summary>An entry opened from another app's card returns to that app; the Love chat re-selects its
    /// open conversation via the OpenChat intent.</summary>
    private void BackToList()
    {
        if (_entryReturnApp is { } returnApp)
        {
            _entryReturnApp = null;
            _view = View.List;
            if (returnApp == "aetherlove")
            {
                _shell?.SendIntent(returnApp, OsIntents.Create(OsIntents.OpenChat));
            }
            else
            {
                _shell?.OpenApp(returnApp);
            }
            return;
        }
        _view = View.List;
        _listEntrance.Arm();
    }

    private void DrawList()
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var list = _list;
        var latest = list is { Length: > 0 } ? list[0].PublishedAtUtc : (DateTimeOffset?)null;

        DrawMasthead(winW, latest);
        DrawRefreshButton(winW);

        if (list is null && _listLoading)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (list is null && _listError is not null)
        {
            DrawCentered(FontAwesomeIcon.Newspaper, Loc.T("news.load_error", _listError));
            return;
        }
        if (list is { Length: 0 })
        {
            DrawCentered(FontAwesomeIcon.Newspaper, Loc.T("news.empty"));
            return;
        }
        if (list is null)
        {
            return;
        }

        _listEntrance.BeginFrame();
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##newsScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                var listW = ImGui.GetContentRegionAvail().X;
                ImGui.Dummy(new Vector2(1f, Px(2f)));
                DrawFeaturedCard(list[0], listW);
                if (list.Length > 1)
                {
                    DrawSectionDivider(Loc.T("os.news_more"), listW);
                    for (int i = 1; i < list.Length; i++)
                    {
                        DrawNewsCard(list[i], listW);
                    }
                }
                ImGui.Dummy(new Vector2(1f, Px(8f)));
            }
        }
        PopScrollbarStyle();
        _listEntrance.EndFrame();
    }

    private static void DrawMasthead(float winW, DateTimeOffset? latest)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(1f, Px(4f)));

        var title = Loc.T("os.app_news");
        using (UiFonts.H1?.Push())
        {
            var tw = ImGui.CalcTextSize(title).X;
            ImGui.SetCursorPosX(MathF.Max(Px(Pad), (winW - tw) * 0.5f));
            ImGui.TextColored(t.AccentLight, title);
        }

        var tagline = Loc.T("os.news_tagline");
        var tagW = ImGui.CalcTextSize(tagline).X;
        ImGui.SetCursorPosX(MathF.Max(Px(Pad), (winW - tagW) * 0.5f));
        ImGui.TextColored(UiColors.Subtle, tagline);

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(Pad));
        var ruleP = ImGui.GetCursorScreenPos();
        var ruleW = winW - Px(Pad) * 2f;
        var cx = ruleP.X + ruleW * 0.5f;
        var ornGap = Px(9f);
        var thickCol = ImGui.GetColorU32(t.Accent with { W = 0.60f });
        dl.AddLine(ruleP, new Vector2(cx - ornGap, ruleP.Y), thickCol, Px(1.6f));
        dl.AddLine(new Vector2(cx + ornGap, ruleP.Y), new Vector2(ruleP.X + ruleW, ruleP.Y), thickCol, Px(1.6f));
        DrawDiamond(dl, new Vector2(cx, ruleP.Y), Px(4f), ImGui.GetColorU32(t.AccentLight));
        var second = ruleP + new Vector2(0f, Px(4f));
        dl.AddLine(second, second + new Vector2(ruleW, 0f), ImGui.GetColorU32(t.Accent with { W = 0.28f }), Px(1f));
        ImGui.Dummy(new Vector2(ruleW, Px(7f)));

        if (latest is { } d)
        {
            var edition = Loc.T("os.news_edition", LanguageProvider.FormatDate(d.ToLocalTime().Date, "d MMMM yyyy"));
            var ew = ImGui.CalcTextSize(edition).X;
            ImGui.SetCursorPosX(MathF.Max(Px(Pad), (winW - ew) * 0.5f));
            ImGui.TextColored(UiColors.Subtle, edition);
        }
        ImGui.Dummy(new Vector2(1f, Px(6f)));
    }

    private static void DrawDiamond(ImDrawListPtr dl, Vector2 c, float r, uint col)
    {
        dl.AddQuadFilled(c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f), col);
    }

    /// <summary>A pinned circular refresh control at the top-right of the surface.</summary>
    private void DrawRefreshButton(float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var size = Px(30f);
        var winPos = ImGui.GetWindowPos();
        var tl = new Vector2(winPos.X + winW - size, winPos.Y + Px(6f));

        var restore = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##newsRefresh", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(restore);

        if (hovered)
        {
            ImGui.SetTooltip(Loc.T("os.news_refresh"));
        }
        dl.AddRectFilled(tl, tl + new Vector2(size, size),
            ImGui.GetColorU32(hovered ? t.Accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));

        var center = tl + new Vector2(size, size) * 0.5f;
        if (_listLoading)
        {
            Widgets.LoadingSpinner.Draw(center, size * 0.26f, Px(2.4f), ImGui.GetColorU32(t.AccentLight));
        }
        else
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.SyncAlt, size * 0.42f, center, ImGui.GetColorU32(t.AccentLight));
        }

        if (clicked && !_listLoading)
        {
            StartLoadList();
        }
    }

    private void DrawNewsCard(NewsSummaryDto item, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var unread = _accented.Contains(item.Id);

        var pad = Px(Pad);
        var cardW = listW - pad * 2f;
        var rounding = Px(12f);
        var barW = Px(4f);
        var innerX = Px(16f);
        var contentW = cardW - innerX - Px(34f);

        var lineH = ImGui.GetTextLineHeight();
        float h3H;
        using (UiFonts.H3?.Push())
        {
            h3H = ImGui.GetTextLineHeight();
        }
        var hasPreview = !string.IsNullOrWhiteSpace(item.Preview);
        var fullPreviewH = hasPreview ? ImGui.CalcTextSize(item.Preview, false, contentW).Y : 0f;
        var previewH = MathF.Min(fullPreviewH, lineH * 2f);
        var cardH = Px(12f) + h3H + Px(5f) + lineH + (hasPreview ? Px(7f) + previewH : 0f) + Px(12f);

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##news_{item.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        if (unread)
        {
            dl.AddRect(tl - new Vector2(Px(1.5f), Px(1.5f)), br + new Vector2(Px(1.5f), Px(1.5f)),
                ImGui.GetColorU32(t.Accent with { W = hovered ? 0.22f : 0.14f }), rounding + Px(1.5f),
                ImDrawFlags.None, Px(2.5f));
        }

        var fillA = unread ? 0.22f : 0.10f;
        if (hovered)
        {
            fillA += 0.06f;
        }
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, t.SecondaryStart, t.SecondaryEnd, fillA);
        dl.AddLine(new Vector2(tl.X + rounding, tl.Y + Px(1f)), new Vector2(br.X - rounding, tl.Y + Px(1f)),
            OsDrawShared.White(hovered ? 0.16f : 0.10f), Px(1f));

        var border = unread ? t.Accent with { W = hovered ? 0.85f : 0.55f } : new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.08f);
        dl.AddRect(tl, br, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, Px(1.2f));

        var barCol = unread ? t.Accent : t.Accent with { W = 0.32f };
        dl.AddRectFilled(new Vector2(tl.X + Px(6f), tl.Y + Px(11f)),
            new Vector2(tl.X + Px(6f) + barW, br.Y - Px(11f)), ImGui.GetColorU32(barCol), barW * 0.5f);

        var textX = tl.X + innerX;
        var y = tl.Y + Px(12f);

        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, y),
                unread ? 0xFFFFFFFFu : ImGui.GetColorU32(UiColors.Body), TruncateToWidth(item.Title, contentW));
        }
        y += h3H + Px(5f);

        var dateStr = LanguageProvider.FormatDate(item.PublishedAtUtc.ToLocalTime().Date, "d MMM yyyy");
        dl.AddText(new Vector2(textX, y), ImGui.GetColorU32(unread ? t.AccentLight : UiColors.Muted), dateStr);
        if (unread)
        {
            var dateW = ImGui.CalcTextSize(dateStr).X;
            DrawNewPill(dl, new Vector2(textX + dateW + Px(8f), y - Px(1.5f)), t);
        }
        y += lineH + (hasPreview ? Px(7f) : 0f);

        if (hasPreview)
        {
            dl.PushClipRect(new Vector2(textX, y), new Vector2(textX + contentW, y + previewH + Px(1f)), true);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textX, y),
                ImGui.GetColorU32(unread ? UiColors.Body : UiColors.Muted), item.Preview, contentW);
            dl.PopClipRect();
        }

        var chevCol = (unread ? t.AccentLight : UiColors.Muted) with { W = hovered ? 1f : 0.7f };
        var chevX = br.X - Px(16f) + (hovered ? Px(3f) : 0f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(12f),
            new Vector2(chevX, tl.Y + cardH * 0.5f), ImGui.GetColorU32(chevCol));

        if (clicked)
        {
            OpenEntry(item.Id, preview: false);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(10f)));
    }

    private static void DrawNewPill(ImDrawListPtr dl, Vector2 pos, ThemeDefinition t)
    {
        var label = Loc.T("os.news_new");
        var sz = ImGui.CalcTextSize(label);
        var padX = Px(7f);
        var h = sz.Y + Px(3f);
        var w = sz.X + padX * 2f;
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(t.Accent with { W = 0.90f }), h * 0.5f);
        dl.AddText(pos + new Vector2(padX, Px(1.5f)), 0xFFFFFFFFu, label);
    }

    /// <summary>Featured (newest) entry card with larger headline and "Latest" ribbon.</summary>
    private void DrawFeaturedCard(NewsSummaryDto item, float listW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var unread = _accented.Contains(item.Id);

        var pad = Px(Pad);
        var cardW = listW - pad * 2f;
        var rounding = Px(14f);
        var innerX = Px(18f);
        var contentW = cardW - innerX - Px(20f);

        float h1H;
        using (UiFonts.H1?.Push())
        {
            h1H = ImGui.GetTextLineHeight();
        }
        var lineH = ImGui.GetTextLineHeight();
        var ribbonH = lineH + Px(4f);
        var hasPreview = !string.IsNullOrWhiteSpace(item.Preview);
        var fullPreviewH = hasPreview ? ImGui.CalcTextSize(item.Preview, false, contentW).Y : 0f;
        var previewH = MathF.Min(fullPreviewH, lineH * 4f);
        var cardH = Px(14f) + ribbonH + Px(10f) + h1H + Px(7f) + lineH + (hasPreview ? Px(9f) + previewH : 0f) + Px(16f);

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##newsFeat_{item.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        for (int i = 3; i >= 1; i--)
        {
            var e = Px(2f * i);
            dl.AddRect(tl - new Vector2(e, e), br + new Vector2(e, e),
                ImGui.GetColorU32(t.Accent with { W = (hovered ? 0.11f : 0.06f) * (1f - (i - 1) / 3f) }),
                rounding + e, ImDrawFlags.None, Px(2f));
        }

        var fillA = (unread ? 0.28f : 0.18f) + (hovered ? 0.05f : 0f);
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, t.SecondaryStart, t.SecondaryEnd, fillA);
        dl.AddLine(new Vector2(tl.X + rounding, tl.Y + Px(1f)), new Vector2(br.X - rounding, tl.Y + Px(1f)),
            OsDrawShared.White(hovered ? 0.20f : 0.13f), Px(1f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.9f : 0.6f }), rounding, ImDrawFlags.None, Px(1.4f));

        var x = tl.X + innerX;
        var y = tl.Y + Px(14f);

        var ribbon = Loc.T("os.news_latest").ToUpperInvariant();
        var rw = ImGui.CalcTextSize(ribbon).X + Px(16f);
        dl.AddRectFilled(new Vector2(x, y), new Vector2(x + rw, y + ribbonH), ImGui.GetColorU32(t.Accent with { W = 0.92f }), ribbonH * 0.5f);
        dl.AddText(new Vector2(x + Px(8f), y + Px(2f)), 0xFFFFFFFFu, ribbon);
        y += ribbonH + Px(10f);

        using (UiFonts.H1?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(x, y),
                unread ? 0xFFFFFFFFu : ImGui.GetColorU32(UiColors.Body), TruncateToWidth(item.Title, contentW));
        }
        y += h1H + Px(7f);

        var dateStr = LanguageProvider.FormatDate(item.PublishedAtUtc.ToLocalTime().Date, "dddd, d MMMM yyyy");
        dl.AddText(new Vector2(x, y), ImGui.GetColorU32(unread ? t.AccentLight : UiColors.Muted), dateStr);
        y += lineH + (hasPreview ? Px(9f) : 0f);

        if (hasPreview)
        {
            dl.PushClipRect(new Vector2(x, y), new Vector2(x + contentW, y + previewH + Px(1f)), true);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(x, y),
                ImGui.GetColorU32(UiColors.Body), item.Preview, contentW);
            dl.PopClipRect();
        }

        if (clicked)
        {
            OpenEntry(item.Id, preview: false);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
        ImGui.Dummy(new Vector2(1f, Px(4f)));
    }

    private static void DrawSectionDivider(string label, float listW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(1f, Px(6f)));
        var w = listW - Px(Pad) * 2f;
        var p = ImGui.GetCursorScreenPos() + new Vector2(Px(Pad), Px(8f));
        var up = label.ToUpperInvariant();
        var lw = ImGui.CalcTextSize(up).X;
        var lineW = MathF.Max(0f, (w - lw - Px(20f)) * 0.5f);
        var col = ImGui.GetColorU32(UiColors.Muted with { W = 0.45f });
        dl.AddLine(p, p + new Vector2(lineW, 0f), col, Px(1f));
        dl.AddLine(new Vector2(p.X + w - lineW, p.Y), new Vector2(p.X + w, p.Y), col, Px(1f));
        dl.AddText(new Vector2(p.X + lineW + Px(10f), p.Y - ImGui.GetTextLineHeight() * 0.5f), ImGui.GetColorU32(UiColors.Muted), up);
        ImGui.Dummy(new Vector2(1f, Px(22f)));
    }

    /// <summary>The right-aligned "Share" pill on the detail header; offers the entry to the OS share sheet.</summary>
    private void DrawShareNewsPill(float rowY, NewsDto entry, IShareService share, float rightEdge)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var label = Loc.T("news.share");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = ImGui.GetFontSize() * 0.85f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Share, iconPx);
        var padX = Px(11f);
        var gap = Px(6f);
        var pillH = labelSz.Y + Px(9f);
        var pillW = padX * 2f + iconSz.X + gap + labelSz.X;
        var tl = new Vector2(rightEdge - Px(Pad) - pillW, rowY);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##newsShareBtn", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.45f : 0.22f }), pillH * 0.5f);
        dl.AddRect(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.95f : 0.60f }), pillH * 0.5f, ImDrawFlags.None, Px(1f));
        IconDraw.Add(dl, FontAwesomeIcon.Share, iconPx,
            new Vector2(tl.X + padX, tl.Y + (pillH - iconSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(tl.X + padX + iconSz.X + gap, tl.Y + (pillH - labelSz.Y) * 0.5f), 0xFFFFFFFFu, label);

        if (clicked)
        {
            share.Offer(new ShareItem
            {
                Type = ShareTypes.News,
                RefId = entry.Id.ToString("D"),
                Title = entry.Title,
                SourceAppId = "news",
            }, title: entry.Title);
        }
    }

    private void DrawDetail()
    {
        var t = ThemeService.Current;

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##newsDetail", ImGui.GetContentRegionAvail(), false);
        PopScrollbarStyle();
        if (!scroll.Success)
        {
            return;
        }

        // Measured inside the child: the outer width includes the scrollbar strip, and a column laid out
        // against it runs under the bar (clipped justified text, clipped pill).
        var innerRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var contentW = ImGui.GetContentRegionAvail().X - Px(Pad) * 2f;

        ImGui.Spacing();
        var headerRowY = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorPosX(Px(Pad));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.app_news"), FontAwesomeIcon.Newspaper))
        {
            BackToList();
        }
        // A published entry can be shared into a chat; a draft/preview has no public id others could open.
        if (_entry is { } shareEntry && !_isPreview && _share is { } share && share.CanShare(ShareTypes.News))
        {
            var restore = ImGui.GetCursorPos();
            DrawShareNewsPill(headerRowY, shareEntry, share, innerRight);
            ImGui.SetCursorPos(restore);
        }
        ImGui.Spacing();

        if (_entry is null && _entryLoading)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }
        if (_entry is null && _entryError is not null)
        {
            ImGui.SetCursorPosX(Px(Pad));
            ImGui.PushTextWrapPos(Px(Pad) + contentW);
            ImGui.TextColored(UiColors.Body, Loc.T("news.load_error", _entryError));
            ImGui.PopTextWrapPos();
            return;
        }

        _detailEntrance.BeginFrame();

        ImGui.SetCursorPosX(Px(Pad));
        using (UiFonts.H1?.Push())
        {
            ImGui.PushTextWrapPos(Px(Pad) + contentW);
            ImGui.TextColored(t.AccentLight, _entry?.Title ?? Loc.T("news.unavailable"));
            ImGui.PopTextWrapPos();
        }

        if (_entry?.PublishedAtUtc is { } pub)
        {
            ImGui.SetCursorPosX(Px(Pad));
            ImGui.TextColored(UiColors.Muted, LanguageProvider.FormatDate(pub.ToLocalTime().Date, "dddd, d MMMM yyyy"));
        }
        if (_isPreview)
        {
            ImGui.SetCursorPosX(Px(Pad));
            ImGui.TextColored(new Vector4(0.95f, 0.78f, 0.30f, 1f), Loc.T("news.preview_badge"));
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(Pad));
        var ruleP = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(ruleP, ruleP + new Vector2(contentW, 0f), ImGui.GetColorU32(t.Accent with { W = 0.55f }), Px(1.4f));
        var second = ruleP + new Vector2(0f, Px(4f));
        dl.AddLine(second, second + new Vector2(contentW, 0f), ImGui.GetColorU32(t.Accent with { W = 0.26f }), Px(1f));
        ImGui.Dummy(new Vector2(contentW, Px(9f)));
        ImGui.Spacing();

        var bodyRendered = false;
        if (_entry is { Lines.Length: > 0 })
        {
            bodyRendered = true;
            _renderer.Draw($"news_{_entry.Id:N}", _entry.Lines, Px(Pad), contentW);
        }
        else if (_entryMissing)
        {
            ImGui.SetCursorPosX(Px(Pad));
            ImGui.PushTextWrapPos(Px(Pad) + contentW);
            ImGui.TextColored(UiColors.Body, Loc.T("news.unavailable"));
            ImGui.PopTextWrapPos();
        }

        if (bodyRendered)
        {
            DrawEndOrnament(contentW, t);
        }

        ImGui.Dummy(new Vector2(1f, Px(12f)));
        _detailEntrance.EndFrame();
    }

    /// <summary>Ornamental diamond divider closing the article.</summary>
    private static void DrawEndOrnament(float contentW, ThemeDefinition t)
    {
        ImGui.Dummy(new Vector2(1f, Px(10f)));
        var dl = ImGui.GetWindowDrawList();
        var c = ImGui.GetCursorScreenPos() + new Vector2(Px(Pad) + contentW * 0.5f, 0f);
        var lineW = Px(26f);
        var gap = Px(10f);
        var col = ImGui.GetColorU32(t.Accent with { W = 0.5f });
        dl.AddLine(c - new Vector2(gap + lineW, 0f), c - new Vector2(gap, 0f), col, Px(1.2f));
        dl.AddLine(c + new Vector2(gap, 0f), c + new Vector2(gap + lineW, 0f), col, Px(1.2f));
        DrawDiamond(dl, c, Px(3.5f), ImGui.GetColorU32(t.AccentLight));
        ImGui.Dummy(new Vector2(1f, Px(4f)));
    }

    private static void DrawCentered(FontAwesomeIcon icon, string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var center = ImGui.GetCursorScreenPos() + new Vector2(winW * 0.5f, Px(36f));
        IconDraw.AddCentered(dl, icon, Px(40f), center, ImGui.GetColorU32(UiColors.Muted with { W = 0.6f }));

        ImGui.Dummy(new Vector2(1f, Px(70f)));
        var wrapped = ImGui.CalcTextSize(text, false, winW - Px(Pad) * 2f);
        ImGui.SetCursorPosX(MathF.Max(Px(Pad), (winW - wrapped.X) * 0.5f));
        ImGui.PushTextWrapPos(winW - Px(Pad));
        ImGui.TextColored(UiColors.Muted, text);
        ImGui.PopTextWrapPos();
    }
}
