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

/// <summary>News reader. Two entry modes: the startup/live "unseen flow" (steps through each unseen item, marks
/// it seen, then hands back to the regular flow) and the Settings "list" mode (all published news grouped per
/// day, tap to read). The body itself is drawn by <see cref="NewsBodyRenderer"/>.</summary>
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
    private Guid _currentEntryId;
    private volatile NewsDto? _entry;
    private volatile bool _entryMissing;
    private Guid? _pendingPreviewId;
    private bool _isPreview;

    public NewsScreen(ScreenRouter router, SessionBootstrapper bootstrap, AetherLoveHubClient hub)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
    }

    /// <summary>Ask the next <see cref="OnShow"/> to open the per-day list (Settings / chat-link entry) instead
    /// of the unseen flow.</summary>
    public void RequestListView() => _requestListView = true;

    /// <summary>Queue a staff-only preview (the admin "test push to staff") to show on the next
    /// <see cref="OnShow"/>. The preview shows any status, marks nothing seen, and returns to the deck.</summary>
    public void QueuePreview(Guid id) => _pendingPreviewId = id;

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
            StartPreview(previewId);
            return;
        }

        _listMode = _requestListView;
        _requestListView = false;

        if (_listMode)
        {
            StartLoadList();
        }
        else
        {
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
            _router.Navigate(_bootstrap.ResolveNextStartupScreen());
            return;
        }
        LoadEntry(_unseenQueue[0]);
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

    /// <summary>The "Next / Got it / Back" action on an open entry: marks it seen, then either steps to the
    /// next unseen item (or hands back to the regular flow) or returns to the list.</summary>
    private void DismissEntry()
    {
        if (_isPreview)
        {
            // A staff preview never marks the item seen — just return to the regular flow.
            _router.Navigate(_bootstrap.ResolveNextStartupScreen());
            return;
        }

        MarkSeen(_currentEntryId);

        if (_listMode)
        {
            _entry = null;
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
            _router.Navigate(_bootstrap.ResolveNextStartupScreen());
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
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;

        PushScrollbarStyle();
        using var scroll = ImRaii.Child("##newsList", new Vector2(0f, scrollH), false);
        PopScrollbarStyle();
        if (!scroll.Success)
        {
            return;
        }

        ImGui.Spacing();
        DrawBackRow(() => _router.Navigate(Screen.Settings), Loc.T("news.back_to_settings"));
        DrawHeading(Loc.T("news.title"));

        foreach (var day in _list.GroupBy(n => n.PublishedAtUtc.ToLocalTime().Date).OrderByDescending(g => g.Key))
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(UiColors.Muted, day.Key.ToString("MMMM d, yyyy"));
            ImGui.Spacing();

            foreach (var item in day.OrderByDescending(n => n.PublishedAtUtc))
            {
                ImGui.SetCursorPosX(PadX);
                if (ImGui.Selectable($"{item.Title}##news_{item.Id:N}", false, ImGuiSelectableFlags.None,
                        new Vector2(winW - PadX * 2f, 0f)))
                {
                    LoadEntry(item.Id);
                }
            }
        }
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

        // [News icon] Title
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

        // Divider
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

        ImGui.Spacing();
        ImGui.Spacing();

        var label = _isPreview
            ? Loc.T("news.got_it")
            : _listMode
                ? Loc.T("news.back")
                : (_unseenIndex < _unseenQueue.Count - 1 ? Loc.T("news.next") : Loc.T("news.got_it"));

        if (!_listMode && !_isPreview && _unseenQueue.Count > 1)
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
            DrawBackRow(() => _router.Navigate(Screen.Settings), Loc.T("news.back_to_settings"));
        }
        DrawHeading(Loc.T("news.title"));
        ImGui.Spacing();
        ImGui.SetCursorPosX(PadX);
        ImGui.PushTextWrapPos(winW - PadX);
        ImGui.TextColored(UiColors.Body, text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawBackRow(Action onBack, string label)
    {
        ImGui.SetCursorPosX(PadX);
        if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label) + new Vector2(Px(6f), 0f)))
        {
            onBack();
        }
        ImGui.Spacing();
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
