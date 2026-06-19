using System.Collections.Generic;
using System.Numerics;
using AetherLove.Changelog;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Standalone "What's New" window shown once after an update; a plain Dalamud window,
/// independent of the phone-shell UI.</summary>
public sealed class ChangelogWindow : Window
{
    public ChangelogWindow() : base($"{Loc.T("common.changelog_window_title")}##Changelog",
        ImGuiWindowFlags.NoDocking)
    {
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(900, 1100),
        };
        IsOpen = false;
    }

    public override void Draw()
    {
        var t = ThemeService.Current;

        DrawHeader(t);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        const float FooterH = 48f;
        var scrollH = ImGui.GetContentRegionAvail().Y - FooterH;
        if (ImGui.BeginChild("##changelogScroll", new Vector2(0, scrollH), false))
        {
            var entries = ChangelogRegistry.Entries;
            if (entries.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Loc.T("common.changelog_empty"));
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    DrawEntry(entries[i], i, t);
                    ImGui.Spacing();
                }
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Spacing();
        DrawFooter(t);
    }

    private static void DrawHeader(ThemeDefinition t)
    {
        var winW = ImGui.GetContentRegionAvail().X;

        using (UiFonts.H2?.Push())
        {
            var Title = Loc.T("common.whats_new");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorPosX((winW - titleSz.X) * 0.5f);
            ImGui.TextColored(t.AccentLight, Title);
        }

        var v = ChangelogRegistry.CurrentVersion;
        var versionText = v is null
            ? "AetherLove"
            : $"AetherLove v{v.Major}.{v.Minor}.{v.Build}";
        var vSz = ImGui.CalcTextSize(versionText);
        ImGui.SetCursorPosX((winW - vSz.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), versionText);
    }

    private static void DrawEntry(ChangelogEntry entry, int index, ThemeDefinition t)
    {
        var isLatest = index == 0;
        var date = entry.ReleaseDate.ToString("d MMM yyyy", LanguageProvider.CurrentCulture);
        var label = isLatest
            ? $"v{entry.VersionString}  ·  {date}   ({Loc.T("common.changelog_latest")})##chlog{index}"
            : $"v{entry.VersionString}  ·  {date}##chlog{index}";

        var flags = isLatest ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        ImGui.PushStyleColor(ImGuiCol.Text, isLatest ? t.AccentLight : new Vector4(0.82f, 0.82f, 0.82f, 1f));
        var open = ImGui.CollapsingHeader(label, flags);
        ImGui.PopStyleColor();
        if (!open)
        {
            return;
        }

        ImGui.Indent(14f);
        ImGui.Spacing();
        if (entry.Important.Count > 0)
        {
            DrawSection(Loc.T("common.changelog_important"), FontAwesomeIcon.ExclamationTriangle, new Vector4(1.0f, 0.72f, 0.32f, 1f), entry.Important);
            ImGui.Spacing();
        }
        if (entry.NewFeatures.Count > 0)
        {
            DrawSection(Loc.T("common.changelog_new_features"), FontAwesomeIcon.Star, new Vector4(0.42f, 0.80f, 0.52f, 1f), entry.NewFeatures);
            ImGui.Spacing();
        }
        if (entry.BugFixes.Count > 0)
        {
            DrawSection(Loc.T("common.changelog_bug_fixes"), FontAwesomeIcon.Bug, new Vector4(0.90f, 0.52f, 0.42f, 1f), entry.BugFixes);
        }
        ImGui.Unindent(14f);
        ImGui.Spacing();
    }

    private static void DrawSection(string title, FontAwesomeIcon icon, Vector4 color, IReadOnlyList<string> items)
    {
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(color, icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(0, 6f);
        ImGui.TextColored(color, title);
        ImGui.Spacing();

        foreach (var item in items)
        {
            ImGui.TextColored(UiColors.Muted, "•");
            ImGui.SameLine(0, 6f);
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextWrapped(item);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawFooter(ThemeDefinition t)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        const float BtnW = 140f;
        ImGui.SetCursorPosX((winW - BtnW) * 0.5f);

        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        if (ImGui.Button(Loc.T("common.got_it"), new Vector2(BtnW, 32f)))
        {
            IsOpen = false;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }
}
