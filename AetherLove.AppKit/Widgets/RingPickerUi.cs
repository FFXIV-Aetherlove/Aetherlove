using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Store;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>The shared avatar-ring picker section: a live preview of the caller's avatar wearing the
/// selected ring, a None row plus one row per owned ring, and a Save button. With zero owned rings it
/// shows the cross-app explainer and an "Open the Store" button instead. The host screen owns fetching
/// (SetOwned/FailLoad), saving (BeginSave/NotifySaved/NotifyError) and both callbacks.</summary>
public sealed class RingPickerUi
{
    public string? Selected;

    private AvatarRingDto[]? _owned;
    private bool _loadFailed;
    private volatile bool _saving;
    private float _savedTimer;
    private string? _error;

    public void Open(string? currentRef)
    {
        Selected = currentRef;
        _owned = null;
        _loadFailed = false;
        _saving = false;
        _savedTimer = 0f;
        _error = null;
    }

    public void SetOwned(AvatarRingDto[] rings) => _owned = rings;

    public void FailLoad(string message)
    {
        _loadFailed = true;
        _error = message;
    }

    public bool Saving => _saving;

    public void BeginSave() => _saving = true;

    public void NotifySaved()
    {
        _saving = false;
        _savedTimer = 2.5f;
    }

    public void NotifyError(string message)
    {
        _saving = false;
        _error = message;
    }

    /// <summary>Draws the picker body at the current cursor. <paramref name="save"/> receives the selected
    /// ref (null = clear); <paramref name="openStore"/> fires the store deep link on the explainer.</summary>
    public void Draw(ISharedImmediateTexture? avatar, float availW, float padX, Action<string?> save, Action openStore)
    {
        if (_savedTimer > 0f)
        {
            _savedTimer -= ImGui.GetIO().DeltaTime;
        }

        if (_owned is null && !_loadFailed)
        {
            LoadingIndicator.Draw();
            return;
        }

        var t = ThemeService.Current;
        if (_error is not null && _loadFailed)
        {
            DrawWrapped(_error, UiColors.Danger, availW, padX);
            return;
        }

        if (_owned is { Length: 0 })
        {
            DrawWrapped(Loc.T("rings.none_owned_title"), new Vector4(1f, 1f, 1f, 0.92f), availW, padX);
            ImGui.Spacing();
            DrawWrapped(Loc.T("rings.none_owned_body"), UiColors.Muted, availW, padX);
            ImGui.Spacing();
            ImGui.SetCursorPosX(padX);
            using (var _ = new StyleButton(t))
            {
                if (SharedUiHelpers.Button($"{Loc.T("rings.open_store")}##ringsOpenStore",
                        new Vector2(availW - padX * 2f, Px(30f))))
                {
                    openStore();
                }
            }
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var previewR = Px(46f);
        var previewPad = previewR * AvatarRings.Overhang - previewR;
        var center = new Vector2(ImGui.GetWindowPos().X - ImGui.GetScrollX() + availW * 0.5f,
            ImGui.GetCursorScreenPos().Y + previewPad + previewR);
        var wrap = avatar?.GetWrapOrDefault();
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(previewR), center + new Vector2(previewR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, previewR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(center, previewR, UiColors.AvatarFallback);
        }
        dl.AddCircle(center, previewR, 0x33FFFFFFu, 64, Px(1.5f));
        AvatarRings.Draw(dl, center, previewR, Selected);
        ImGui.Dummy(new Vector2(availW, (previewR + previewPad) * 2f + Px(6f)));

        DrawWrapped(Loc.T("rings.preview_hint"), UiColors.Hint, availW, padX);
        ImGui.Spacing();

        DrawRow(dl, null, Loc.T("rings.none"), padX, availW);
        foreach (var ring in _owned!)
        {
            DrawRow(dl, ring.FrameRef, LocalizedName(ring), padX, availW);
        }

        ImGui.Spacing();
        if (_error is not null)
        {
            DrawWrapped(_error, UiColors.Danger, availW, padX);
            ImGui.Spacing();
        }

        var label = _saving ? Loc.T("profile.saving")
                  : _savedTimer > 0f ? Loc.T("profile.saved")
                  : Loc.T("profile.save_changes");
        ImGui.SetCursorPosX(padX);
        using (var _ = new StyleButton(t))
        {
            if (SharedUiHelpers.Button($"{label}##ringsSave", new Vector2(availW - padX * 2f, Px(30f))) && !_saving)
            {
                _error = null;
                save(Selected);
            }
        }
    }

    private void DrawRow(ImDrawListPtr dl, string? frameRef, string label, float padX, float availW)
    {
        var rowH = Px(38f);
        ImGui.SetCursorPosX(padX);
        var tl = ImGui.GetCursorScreenPos();
        var rowW = availW - padX * 2f;
        var selected = string.Equals(Selected, frameRef, StringComparison.Ordinal);
        if (ImGui.InvisibleButton($"##ringRow_{frameRef ?? "none"}", new Vector2(rowW, rowH)))
        {
            Selected = frameRef;
        }
        SharedUiHelpers.HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var t = ThemeService.Current;
        dl.AddRectFilled(tl, tl + new Vector2(rowW, rowH),
            selected ? t.AccentWithAlpha(0.16f) : hovered ? 0x14FFFFFFu : 0x0AFFFFFFu, Px(9f));
        if (selected)
        {
            dl.AddRect(tl, tl + new Vector2(rowW, rowH), t.AccentWithAlpha(0.7f), Px(9f), ImDrawFlags.None, Px(1.2f));
        }

        var thumbR = Px(12f);
        var thumbC = tl + new Vector2(Px(10f) + thumbR * AvatarRings.Overhang, rowH * 0.5f);
        dl.AddCircleFilled(thumbC, thumbR, UiColors.AvatarFallback);
        AvatarRings.Draw(dl, thumbC, thumbR, frameRef);

        var textX = thumbC.X + thumbR * AvatarRings.Overhang + Px(10f);
        dl.AddText(new Vector2(textX, tl.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f), 0xFFFFFFFFu, label);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
    }

    private static void DrawWrapped(string text, Vector4 color, float availW, float padX)
    {
        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(availW - padX);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawWrapped(string text, uint color, float availW, float padX) =>
        DrawWrapped(text, ImGui.ColorConvertU32ToFloat4(color), availW, padX);

    private static string LocalizedName(AvatarRingDto ring)
    {
        var lang = Enum.TryParse<Language>(UiHost.Configuration.PluginLanguage, ignoreCase: true, out var l)
            ? l
            : Language.English;
        var s = lang switch
        {
            Language.Spanish => ring.NameSpanish,
            Language.French => ring.NameFrench,
            Language.Russian => ring.NameRussian,
            Language.German => ring.NameGerman,
            Language.Portuguese => ring.NamePortuguese,
            _ => ring.NameEnglish,
        };
        return string.IsNullOrWhiteSpace(s) ? ring.NameEnglish : s!;
    }

    private readonly ref struct StyleButton
    {
        public StyleButton(ThemeDefinition t)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        }

        public void Dispose() => ImGui.PopStyleColor(4);
    }
}
