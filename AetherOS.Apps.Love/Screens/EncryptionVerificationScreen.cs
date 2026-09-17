using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Crypto;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Encryption verification for one conversation: a deterministic weave and safety code derived
/// from both public keys.</summary>
public class EncryptionVerificationScreen
{
    private readonly LoveRouter _router;
    private readonly KeyStorageService _keys;

    private string _peerName = string.Empty;
    private Guid _peerId;
    private byte[]? _peerPublicKey;

    private const float HeaderH = 44f;

    public EncryptionVerificationScreen(LoveRouter router, KeyStorageService keys)
    {
        _router = router;
        _keys = keys;
    }

    public void SetContext(Guid peerId, string peerName, byte[]? peerPublicKey)
    {
        _peerId = peerId;
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
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var winW = ImGui.GetContentRegionAvail().X;

        using (UiFonts.H3?.Push())
        {
            var title = Loc.T("verify.title");
            var titleSz = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(origin.X + (winW - titleSz.X) * 0.5f, origin.Y + (Px(HeaderH) - titleSz.Y) * 0.5f),
                0xFFFFFFFF, title);
        }

        if (DrawFloatingBackPill(new Vector2(origin.X, origin.Y + Px(6f)), Loc.T("verify.back"), FontAwesomeIcon.Comment))
        {
            _router.Navigate(LoveView.Chat);
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
        EncryptionVerificationPanel.Draw("love/" + UiHost.Configuration.Auth.ActiveProfileId + "/" + _peerId, _peerName, fp);
        ImGui.PopTextWrapPos();
        return;

    }

    private static void DrawKeyRow(string label, string value, ThemeDefinition t)
    {
        ImGui.TextColored(t.AccentLight, label);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.88f, 0.88f, 0.88f, 1f));
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }
}
