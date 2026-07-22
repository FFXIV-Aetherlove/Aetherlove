using System.IO;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public sealed partial class OsOnboardingScreen
{
    private readonly ISharedImmediateTexture?[] _langFlags =
        new ISharedImmediateTexture?[LanguageEntries.Length];
    private bool _langFlagsLoaded;
    private int _langIdx;

    private void EnsureLangFlags()
    {
        if (_langFlagsLoaded)
        {
            return;
        }
        _langFlagsLoaded = true;
        var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
        for (int i = 0; i < LanguageEntries.Length; i++)
        {
            var path = Path.Combine(dir, "Media", LanguageEntries[i].FlagFile);
            if (File.Exists(path))
            {
                _langFlags[i] = Plugin.TextureProvider.GetFromFile(path);
            }
        }
    }

    /// <summary>The plugin-language picker, part of the OS onboarding "make it yours" step (language is a shell
    /// setting, so it lives here rather than in the AetherLove profile onboarding). Applies immediately and
    /// persists to config.</summary>
    private void DrawDesignLanguage()
    {
        var t = ThemeService.Current;
        DrawDesignSectionLabel(Loc.T("os_onboarding.design_language"), t);
        ImGui.SetCursorPosX(Px(16f));

        EnsureLangFlags();
        DrawLanguagePillsCore(
            _langFlags,
            flagW: Px(36f),
            flagH: Px(27f),
            useCode: true,
            idPrefix: "osLang",
            isSelected: i => i == _langIdx,
            onToggle: i =>
            {
                _langIdx = i;
                LanguageProvider.SetLanguage(LanguageEntries[i].Name);
                Plugin.Configuration.PluginLanguage = LanguageEntries[i].Name;
                Plugin.Configuration.Save();
            },
            count: LanguageProvider.UiLanguageCount);
    }
}
