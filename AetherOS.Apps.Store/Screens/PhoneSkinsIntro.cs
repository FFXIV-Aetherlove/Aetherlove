using System;
using System.Linq;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The one-time notice that phone skins are on sale and everyone may take one for free. An
/// in-page overlay drawn last on every view, like the boosts sheet, so it lands over whatever the shop
/// opened on. It waits for the storefront and shows only once that storefront actually carries the skins
/// shelf, so a server without the collection never makes the promise. Seen is a client flag: the text is
/// fixed, and whether the free pick is still open is the product page's job to say.</summary>
internal sealed class PhoneSkinsIntro(IAppStorage storage, Action openSkins)
{
    internal const string SeenKey = "phoneSkinsIntroSeen";
    internal const string SkinsCategoryKey = "premium-themes";

    private bool? _seen;
    private float _panelH;

    public void Draw(StoreState state)
    {
        _seen ??= storage.Get<bool>(SeenKey);
        if (_seen == true || state.Front is not { } front)
        {
            return;
        }
        if (!front.Categories.Any(c => c.Key == SkinsCategoryKey))
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            MarkSeen();
            return;
        }

        var dismissed = SharedUiHelpers.DrawPageOverlayPanel(
            "storeSkinsIntro", ImGui.GetWindowPos(), ImGui.GetWindowSize(), ref _panelH, Px(220f), innerW =>
            {
                ModalUi.Header(innerW, FontAwesomeIcon.MobileAlt, Loc.T("os.store_skins_intro_title"), StorePalette.Blue);
                ImGui.PushTextWrapPos(innerW);
                ImGui.TextColored(UiColors.Body, Loc.T("os.store_skins_intro_body"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();
                if (ModalUi.Button($"{Loc.T("os.store_skins_intro_open")}##skinsIntroOpen", innerW))
                {
                    MarkSeen();
                    openSkins();
                }
                ImGui.Spacing();
                // Stacked full-width rather than the confirm overlay's side-by-side halves: at half a
                // panel these labels cut off in German and French.
                if (ModalUi.Button($"{Loc.T("os.store_skins_intro_later")}##skinsIntroLater", innerW))
                {
                    MarkSeen();
                }
            });
        if (dismissed)
        {
            MarkSeen();
        }
    }

    private void MarkSeen()
    {
        _seen = true;
        storage.Set(SeenKey, true);
    }
}
