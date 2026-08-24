using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>Per-screen translation state and the shared right-click plumbing. One instance per screen
/// (like <see cref="Widgets.EntranceAnimation"/>): the screen renders text through <see cref="Display"/>
/// (replace-with-toggle by decision), adds <see cref="DrawMenuItems"/> to its message context menu, and
/// calls <see cref="DrawConsentOverlay"/> once after its content so the opt-in popup can layer on top.
/// Results land from a worker thread, so the state dictionary is lock-guarded. <paramref name="openSettings"/>
/// deep-links into the settings languages page carrying the calling app as the return target.</summary>
public sealed class TranslateUi(string surfaceId, ITranslationBridge translation, Action openSettings)
{
    private enum Phase
    {
        Pending,
        Shown,
        Hidden,
        Failed,
    }

    private sealed class Entry
    {
        public Phase Phase;
        public string? Translated;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private bool _consentOpen;
    private float _consentPanelH;
    private string? _consentId;
    private string? _consentText;
    private long _version;

    /// <summary>Bumps whenever any item's shown text changes, so screens caching text-derived layout
    /// (bubble heights) know to drop those caches.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>The text the surface should render right now: the translation while it is shown, the
    /// original otherwise.</summary>
    public string Display(string id, string original)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(id, out var entry)
                && entry is { Phase: Phase.Shown, Translated: { } translated }
                ? translated
                : original;
        }
    }

    public string Display(Guid id, string original) => Display(id.ToString("N"), original);

    /// <summary>Whether the given item currently renders as a translation (for optional markers).</summary>
    public bool IsShowingTranslation(string id)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(id, out var entry) && entry.Phase == Phase.Shown;
        }
    }

    /// <summary>The three context-menu rows: Translate / Show original / Translation settings. Call inside
    /// an open popup.</summary>
    public void DrawMenuItems(string id, string text)
    {
        Phase? phase;
        lock (_lock)
        {
            phase = _entries.TryGetValue(id, out var entry) ? entry.Phase : null;
        }

        switch (phase)
        {
            case Phase.Shown:
                if (DrawIconMenuItem(FontAwesomeIcon.Undo, Loc.T("os.translate_show_original")))
                {
                    ImGui.CloseCurrentPopup();
                    SetPhase(id, Phase.Hidden);
                }
                break;
            case Phase.Pending:
                DrawIconMenuItem(FontAwesomeIcon.Language, Loc.T("os.translate_pending"), enabled: false);
                break;
            default:
                var label = phase == Phase.Failed ? Loc.T("os.translate_retry") : Loc.T("os.translate");
                if (DrawIconMenuItem(FontAwesomeIcon.Language, label))
                {
                    ImGui.CloseCurrentPopup();
                    if (!translation.Enabled)
                    {
                        _consentOpen = true;
                        _consentPanelH = 0f;
                        _consentId = id;
                        _consentText = text;
                    }
                    else if (phase == Phase.Hidden)
                    {
                        SetPhase(id, Phase.Shown);
                    }
                    else
                    {
                        Start(id, text);
                    }
                }
                break;
        }

        if (DrawIconMenuItem(FontAwesomeIcon.Cog, Loc.T("os.translate_settings")))
        {
            ImGui.CloseCurrentPopup();
            openSettings();
        }
    }

    public void DrawMenuItems(Guid id, string text) => DrawMenuItems(id.ToString("N"), text);

    /// <summary>The ADR 9 opt-in, right where the intent to translate happened: a plain explainer with
    /// Enable and Cancel. Enabling persists the setting and immediately runs the translation that
    /// triggered it. Draw after the screen's content.</summary>
    public void DrawConsentOverlay(Vector2 winPos, Vector2 winSize)
    {
        if (!_consentOpen)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _consentOpen = false;
            return;
        }

        var enable = false;
        var cancel = false;
        var dismissed = DrawPageOverlayPanel("translateConsent", winPos, winSize, ref _consentPanelH, Px(240f),
            innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.Language, Loc.T("os.translate_consent_title"),
                ThemeService.Current.Accent);
            ImGui.Spacing();
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, Loc.T("os.translate_consent_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var btnW = (innerW - Px(10f)) * 0.5f;
            if (ModalUi.Button($"{Loc.T("os.translate_consent_enable")}##trConsentOk", btnW))
            {
                enable = true;
            }
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button($"{Loc.T("os.translate_consent_cancel")}##trConsentNo", btnW))
            {
                cancel = true;
            }
        });

        if (enable)
        {
            _consentOpen = false;
            translation.Enable();
            if (_consentId is { } id && _consentText is { } text)
            {
                Start(id, text);
            }
        }
        else if (cancel || dismissed)
        {
            _consentOpen = false;
        }
    }

    private void Start(string id, string text)
    {
        SetPhase(id, Phase.Pending);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await translation.TranslateAsync(text).ConfigureAwait(false);
                lock (_lock)
                {
                    if (!_entries.TryGetValue(id, out var entry))
                    {
                        return;
                    }
                    if (result is null)
                    {
                        entry.Phase = Phase.Failed;
                    }
                    else
                    {
                        entry.Phase = Phase.Shown;
                        entry.Translated = result.Text;
                    }
                }
                Interlocked.Increment(ref _version);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Translate] {Surface} item failed.", surfaceId);
                SetPhase(id, Phase.Failed);
            }
        });
    }

    private void SetPhase(string id, Phase phase)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                entry = new Entry();
                _entries[id] = entry;
            }
            entry.Phase = phase;
        }
        Interlocked.Increment(ref _version);
    }
}
