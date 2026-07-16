using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.News;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>News reader with two entry modes: the startup/live unseen flow and the browse-all list mode.</summary>
public sealed class NewsScreen : IDisposable
{
    private enum View { Loading, List, Entry, Empty, Error }

    private static float PadX => Px(16f);

    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherLoveHubClient _hub;
    private readonly NewsBodyRenderer _renderer = new();

    private bool _requestListView;
    private bool _listMode;

    private View _view = View.Loading;
    private volatile string? _error;
    private CancellationTokenSource _cts = new();

    private List<Guid> _unseenQueue = new();
    private int _unseenIndex;

    private NewsSummaryDto[] _list = [];
    private readonly EntranceAnimation _entrance = new();
    private Guid _currentEntryId;
    private volatile NewsDto? _entry;
    private volatile bool _entryMissing;
    private Guid? _pendingPreviewId;
    private bool _isPreview;
    private bool _pendingLiveUnseen;
    private bool _liveUnseen;

    public NewsScreen(ScreenRouter router, SessionBootstrapper bootstrap, AetherLoveHubClient hub)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
    }

    /// <summary>The next OnShow opens the list view instead of the unseen flow.</summary>
    public void RequestListView() => _requestListView = true;

    /// <summary>Queues a staff-only preview for the next OnShow; a preview marks nothing seen.</summary>
    public void QueuePreview(Guid id) => _pendingPreviewId = id;

    /// <summary>Marks the unseen flow as a live mid-session push: on completion it returns to the deck
    /// instead of re-running the startup gate ladder.</summary>
    public void RequestLiveUnseenFlow() => _pendingLiveUnseen = true;

    public void OnShow()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _error = null;
        _entry = null;
        _entryMissing = false;
        _isPreview = false;
        _view = View.Loading;

        if (_pendingPreviewId is Guid previewId)
        {
            _pendingPreviewId = null;
            _pendingLiveUnseen = false;
            StartPreview(previewId);
            return;
        }

        _listMode = _requestListView;
        _requestListView = false;

        if (_listMode)
        {
            _pendingLiveUnseen = false;
            StartLoadList();
        }
        else
        {
            _liveUnseen = _pendingLiveUnseen;
            _pendingLiveUnseen = false;
            StartUnseenFlow();
        }
    }

    private void StartUnseenFlow()
    {
        _unseenQueue = (_bootstrap.LastConnection?.UnseenNews ?? [])
            .Select(n => n.Id)
            .ToList();
        _unseenIndex = 0;
        if (_unseenQueue.Count == 0)
        {
            FinishUnseenFlow();
            return;
        }
        LoadEntry(_unseenQueue[0]);
    }

    private void FinishUnseenFlow()
    {
        if (_liveUnseen)
        {
            _router.Navigate(Screen.Deck);
            return;
        }
        _router.Navigate(_bootstrap.ResolveNextStartupScreen());
    }

    private void NavigateBack()
    {
        if (_listMode)
        {
            _router.Navigate(Screen.MyProfile);
            return;
        }
        if (_isPreview)
        {
            _router.Navigate(Screen.Deck);
            return;
        }
        FinishUnseenFlow();
    }

    private void StartPreview(Guid id)
    {
        _isPreview = true;
        _listMode = false;
        _currentEntryId = id;
        _view = View.Loading;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetNewsPreviewAsync(id, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _entry = dto;
                _entryMissing = dto is null;
                _view = View.Entry;
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                _view = View.Error;
            }
        });
    }

    private void StartLoadList()
    {
        _view = View.Loading;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var list = await _hub.GetNewsListAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _list = list;
                _entrance.Arm();
                _view = list.Length == 0 ? View.Empty : View.List;
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                _view = View.Error;
            }
        });
    }

    private void LoadEntry(Guid id)
    {
        _currentEntryId = id;
        _entry = null;
        _entryMissing = false;
        _view = View.Loading;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetNewsAsync(id, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _entry = dto;
                _entryMissing = dto is null;
                _view = View.Entry;
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                _view = View.Error;
            }
        });
    }

    private void MarkSeen(Guid id)
    {
        _bootstrap.MarkNewsSeenInSnapshot(new[] { id });
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.MarkNewsSeenAsync(new[] { id }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[News] MarkNewsSeenAsync failed.");
            }
        });
    }

    private void DismissEntry()
    {
        if (_isPreview)
        {
            // Previews mark nothing seen and must not re-enter the startup gate ladder.
            _router.Navigate(Screen.Deck);
            return;
        }

        MarkSeen(_currentEntryId);

        if (_listMode)
        {
            _entry = null;
            _entrance.Arm();
            _view = View.List;
            return;
        }

        _unseenIndex++;
        if (_unseenIndex < _unseenQueue.Count)
        {
            LoadEntry(_unseenQueue[_unseenIndex]);
        }
        else
        {
            FinishUnseenFlow();
        }
    }

    public void Draw()
    {
        switch (_view)
        {
            case View.List:
                DrawList();
                break;
            case View.Entry:
                DrawEntry();
                break;
            case View.Empty:
                DrawCentered(Loc.T("news.empty"), back: true);
                break;
            case View.Error:
                DrawCentered(_error ?? Loc.T("news.load_error", string.Empty), back: true);
                break;
            default:
                Widgets.LoadingIndicator.Draw();
                break;
        }
    }

    private void DrawList()
    {
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##newsList", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll.Success)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(PadX);
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("profile.back_to_my"), FontAwesomeIcon.User))
        {
            NavigateBack();
        }
        ImGui.Spacing();
        DrawHeading(Loc.T("news.title"));
        ImGui.Spacing();

        var listW = ImGui.GetContentRegionAvail().X;
        _entrance.BeginFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
        foreach (var item in _list)
        {
            DrawNewsCard(item, listW);
        }
        ImGui.PopStyleVar();
        _entrance.EndFrame();
    }

    private void DrawNewsCard(NewsSummaryDto item, float listW)
    {
        var t = ThemeService.Current;
        var pad = PadX;
        var cardW = listW - pad * 2f;
        var innerX = Px(12f);
        var contentW = cardW - innerX * 2f;
        var rounding = Px(10f);
        var gap = Px(7f);
        var btnH = Px(26f);

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var lineH = ImGui.GetTextLineHeight();
        var hasPreview = !string.IsNullOrWhiteSpace(item.Preview);
        var previewH = hasPreview ? ImGui.CalcTextSize(item.Preview, false, contentW).Y : 0f;
        var cardH = Px(12f) + lineH + (hasPreview ? gap + previewH : 0f) + gap + btnH + Px(12f);

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = new Vector2(tl.X + cardW, tl.Y + cardH);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), rounding);
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), rounding, ImDrawFlags.None, Px(1f));

        var cx = tl.X + innerX;
        var y = tl.Y + Px(12f);

        var dateStr = LanguageProvider.FormatDate(item.PublishedAtUtc.ToLocalTime().Date, "d MMM yyyy");
        dl.AddText(font, fontSize, new Vector2(cx, y), ImGui.GetColorU32(UiColors.Muted), dateStr);
        var dateW = ImGui.CalcTextSize(dateStr).X;
        const string sep = "  -  ";
        dl.AddText(font, fontSize, new Vector2(cx + dateW, y), ImGui.GetColorU32(UiColors.Muted), sep);
        var sepW = ImGui.CalcTextSize(sep).X;
        var title = TruncateToWidth(item.Title, contentW - dateW - sepW);
        dl.AddText(font, fontSize, new Vector2(cx + dateW + sepW, y), ImGui.GetColorU32(t.AccentLight), title);
        y += lineH + gap;

        if (hasPreview)
        {
            dl.AddText(font, fontSize, new Vector2(cx, y), ImGui.GetColorU32(UiColors.Body), item.Preview, contentW);
            y += previewH + gap;
        }

        ImGui.SetCursorScreenPos(new Vector2(cx, y));
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(6f));
        if (ImGui.Button($"{Loc.T("news.read_more")}##nr_{item.Id:N}", new Vector2(Px(104f), btnH)))
        {
            LoadEntry(item.Id);
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(cardW, cardH));
    }

    private void DrawEntry()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        var contentW = winW - PadX * 2f;

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##newsEntry", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll.Success)
        {
            return;
        }

        ImGui.Spacing();

        if (_listMode)
        {
            ImGui.SetCursorPosX(PadX);
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("profile.back_to_my"), FontAwesomeIcon.User))
            {
                DismissEntry();
            }
            ImGui.Spacing();
        }

        ImGui.SetCursorPosX(PadX);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(t.Accent, FontAwesomeIcon.Newspaper.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(0, Px(8f));
        var title = _entry?.Title ?? Loc.T("news.unavailable");
        using (UiFonts.H3?.Push())
        {
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(t.AccentLight, title);
            ImGui.PopTextWrapPos();
        }

        if (_isPreview)
        {
            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(new Vector4(0.95f, 0.78f, 0.30f, 1f), Loc.T("news.preview_badge"));
        }

        ImGui.Spacing();
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(PadX);
        var p = ImGui.GetCursorScreenPos();
        dl.AddLine(p, new Vector2(p.X + contentW, p.Y), 0x55FFFFFFu, 1f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Px(8f));
        ImGui.Spacing();

        if (_entry is { Lines.Length: > 0 })
        {
            _renderer.Draw($"news_{_entry.Id:N}", _entry.Lines, PadX, contentW);
        }
        else if (_entryMissing)
        {
            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(UiColors.Body, Loc.T("news.unavailable"));
            ImGui.PopTextWrapPos();
        }

        // List mode has the top back pill instead of a bottom action button.
        if (_listMode)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Spacing();

        var label = _isPreview
            ? Loc.T("news.got_it")
            : (_unseenIndex < _unseenQueue.Count - 1 ? Loc.T("news.next") : Loc.T("news.got_it"));

        if (!_isPreview && _unseenQueue.Count > 1)
        {
            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(UiColors.Muted, Loc.T("news.progress", _unseenIndex + 1, _unseenQueue.Count));
            ImGui.Spacing();
        }

        ImGui.SetCursorPosX(PadX);
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(label, new Vector2(contentW, Px(36f))))
        {
            DismissEntry();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void DrawCentered(string text, bool back)
    {
        var winW = ImGui.GetWindowSize().X;
        ImGui.Spacing();
        if (back)
        {
            ImGui.SetCursorPosX(PadX);
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("profile.back_to_my"), FontAwesomeIcon.User))
            {
                NavigateBack();
            }
            ImGui.Spacing();
        }
        DrawHeading(Loc.T("news.title"));
        ImGui.Spacing();
        ImGui.SetCursorPosX(PadX);
        ImGui.PushTextWrapPos(winW - PadX);
        ImGui.TextColored(UiColors.Body, text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawHeading(string heading)
    {
        ImGui.SetCursorPosX(PadX);
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, heading);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
