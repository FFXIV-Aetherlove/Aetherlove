using System;
using System.Numerics;
using System.Collections.Generic;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

public static class EncryptionVerificationPanel
{
    public static void Draw(string scope, string peerName, byte[]? fingerprint)
    {
        if (fingerprint is not { Length: 32 })
        {
            ImGui.TextWrapped(Loc.T("verify.unavailable"));
            return;
        }
        var encoded = Convert.ToHexString(fingerprint);
        var saved = UiHost.Configuration.VerifiedEncryptionPeers.GetValueOrDefault(scope);
        ImGui.TextWrapped(Loc.T(saved is null ? "account.unverified" : saved == encoded ? "account.verified" : "account.key_changed"));
        ImGui.TextWrapped(Loc.T("verify.intro", peerName));
        var size = MathF.Min(ImGui.GetContentRegionAvail().X, Px(200));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size));
        SafetyImage.DrawTruchet(ImGui.GetWindowDrawList(), origin, size, fingerprint);
        ImGui.TextUnformatted(SafetyImage.SafetyCode(fingerprint));
        ImGui.TextWrapped(Loc.T("verify.how", peerName));
        if (SharedUiHelpers.Button(Loc.T("account.compared"), new Vector2(0, Px(34))))
        {
            UiHost.Configuration.VerifiedEncryptionPeers[scope] = encoded;
            UiHost.Configuration.Save();
        }
        ImGui.TextWrapped(Loc.T("verify.keys_explain"));
    }
}
