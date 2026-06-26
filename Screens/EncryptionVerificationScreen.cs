using System;
using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Crypto;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Full-page encryption-verification view for one conversation: a deterministic weave and safety
/// code derived from both public keys, plus key excerpts and an explanation. Opened from the chat menu;
/// the back button returns to the chat.</summary>
public class EncryptionVerificationScreen
{
    private readonly ScreenRouter _router;
    private readonly KeyStorageService _keys;

    private string _peerName = string.Empty;
    private byte[]? _peerPublicKey;

    private const float HeaderH = 44f;
    private const float BtnW = 36f;

    public EncryptionVerificationScreen(ScreenRouter router, KeyStorageService keys)
    {
        _router = router;
        _keys = keys;
    }

    public void SetContext(string peerName, byte[]? peerPublicKey)
    {
        _peerName = peerName;
        _peerPublicKey = peerPublicKey;
    }

    public void OnShow()
    {
    }

    public void Draw()
    {
        DrawHeader();
        ImGui.Spacing();
        DrawBody();
    }

    private void DrawHeader()
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var winW = ImGui.GetContentRegionAvail().X;

        var btnY = origin.Y + (Px(HeaderH) - Px(BtnW)) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, btnY));
        ImGui.InvisibleButton("##verifyBack", Px(BtnW, BtnW));
        var backHovered = ImGui.IsItemHovered();
        if (backHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("verify.back"));
        }
        if (ImGui.IsItemClicked())
        {
            _router.Navigate(Screen.Chat);
        }

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var backIcon = FontAwesomeIcon.ArrowLeft.ToIconString();
        var backSz = ImGui.CalcTextSize(backIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(origin.X + (Px(BtnW) - backSz.X) * 0.5f, btnY + (Px(BtnW) - backSz.Y) * 0.5f),
            backHovered ? t.AccentLightU32 : t.AccentU32, backIcon);
        ImGui.PopFont();

        using (UiFonts.H3?.Push())
        {
            var title = Loc.T("verify.title");
            var titleSz = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(origin.X + (winW - titleSz.X) * 0.5f, origin.Y + (Px(HeaderH) - titleSz.Y) * 0.5f),
                0xFFFFFFFF, title);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + Px(HeaderH)));
        ImGui.Separator();
    }

    private void DrawBody()
    {
        var t = ThemeService.Current;
        var grey = new Vector4(0.82f, 0.82f, 0.82f, 1f);

        using var child = ImRaii.Child("##verifyScroll", ImGui.GetContentRegionAvail(), false);
        if (!child.Success)
        {
            return;
        }

        var availW = ImGui.GetContentRegionAvail().X;
        ImGui.PushTextWrapPos(availW);

        var myKey = _keys.GetPublicKey();
        var peerKey = _peerPublicKey;

        if (myKey is null || myKey.Length == 0 || peerKey is null || peerKey.Length == 0)
        {
            ImGui.TextColored(grey, Loc.T("verify.unavailable"));
            ImGui.PopTextWrapPos();
            return;
        }

        var fp = CryptoService.VerificationFingerprint(myKey, peerKey);

        ImGui.TextColored(grey, Loc.T("verify.intro", _peerName));
        ImGui.Spacing();
        ImGui.Spacing();

        var imgSize = MathF.Floor(availW * 0.62f);
        ImGui.SetCursorPosX((availW - imgSize) * 0.5f);
        var imgTL = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(imgSize, imgSize));
        var dl = ImGui.GetWindowDrawList();
        SafetyImage.DrawTruchet(dl, imgTL, imgSize, fp);
        dl.AddRect(imgTL, imgTL + new Vector2(imgSize, imgSize), t.AccentWithAlpha(0.5f),
            imgSize * 0.06f, ImDrawFlags.RoundCornersAll, 1.5f);
        ImGui.Spacing();
        ImGui.Spacing();

        foreach (var line in SafetyImage.SafetyCode(fp).Split('\n'))
        {
            var sz = ImGui.CalcTextSize(line);
            ImGui.SetCursorPosX((availW - sz.X) * 0.5f);
            ImGui.TextColored(t.AccentLight, line);
        }
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextColored(grey, Loc.T("verify.how", _peerName));
        ImGui.Spacing();
        ImGui.Spacing();

        DrawKeyRow(Loc.T("verify.your_key"), SafetyImage.KeyExcerpt(myKey), t);
        ImGui.Spacing();
        DrawKeyRow(Loc.T("verify.their_key", _peerName), SafetyImage.KeyExcerpt(peerKey), t);
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("verify.keys_explain"));
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        ImGui.PopTextWrapPos();
    }

    private static void DrawKeyRow(string label, string value, ThemeDefinition t)
    {
        ImGui.TextColored(t.AccentLight, label);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.88f, 0.88f, 0.88f, 1f));
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }
}
