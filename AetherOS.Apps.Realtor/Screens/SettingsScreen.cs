using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>The Realtor app's settings, laid out like the Photos app's: a back-and-title bar, then one
/// card per toggle carrying its own explanation.</summary>
internal sealed class SettingsScreen
{
    private const float PadX = 14f;

    private readonly RealtorSettings _settings;

    public SettingsScreen(RealtorSettings settings) => _settings = settings;

    public void Draw(OsAppContext ctx, Action? onBack)
    {
        // The hosted caller swallows exceptions, so an unbalanced child would corrupt the window stack for
        // the rest of the frame; the try/finally keeps Begin and End paired whatever the body does.
        ImGui.BeginChild("##realtorSettings", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.None);
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winW = ImGui.GetWindowSize().X;
            var pad = ctx.Px(PadX);
            var barTL = ImGui.GetCursorScreenPos();
            var barH = ctx.Px(46f);
            var titleX = winPos.X + pad;

            if (onBack != null)
            {
                ImGui.SetCursorScreenPos(new Vector2(winPos.X + pad, barTL.Y + (barH - ctx.Px(28f)) * 0.5f));
                if (ImGui.InvisibleButton("##realtorSettingsBack", new Vector2(ctx.Px(28f), ctx.Px(28f))))
                {
                    onBack();
                }
                HandOnHover();
                var backC = ImGui.GetItemRectMin() + new Vector2(ctx.Px(14f), ctx.Px(14f));
                ImGui.GetWindowDrawList().AddCircleFilled(backC, ctx.Px(14f),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, ImGui.IsItemHovered() ? 0.16f : 0.08f)));
                IconDraw.AddCentered(ImGui.GetWindowDrawList(), FontAwesomeIcon.ChevronLeft, ctx.Px(12f), backC,
                    ImGui.GetColorU32(UiColors.Body));
                titleX = winPos.X + pad + ctx.Px(40f);
            }

            using (ctx.HeadingFont?.Push())
            {
                var title = Loc.T("os.realtor_settings");
                var ts = ImGui.CalcTextSize(title);
                ImGui.GetWindowDrawList().AddText(new Vector2(titleX, barTL.Y + (barH - ts.Y) * 0.5f),
                    ImGui.GetColorU32(ThemeService.Current.AccentLight), title);
            }
            ImGui.SetCursorScreenPos(barTL);
            ImGui.Dummy(new Vector2(winW, barH));

            DrawBody(ctx, winPos.X + pad, winW - (pad * 2f));
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawBody(OsAppContext ctx, float x, float width)
    {
        SectionLabel(ctx, x, Loc.T("os.realtor_settings_data"));
        var stale = _settings.ShowStale;
        if (Toggle(ctx, "realtorStale", Loc.T("os.realtor_set_stale"), Loc.T("os.realtor_set_stale_hint"),
            x, width, ref stale))
        {
            _settings.ShowStale = stale;
        }

        ImGui.Dummy(new Vector2(width, ctx.Px(10f)));
        SectionLabel(ctx, x, Loc.T("os.realtor_settings_alerts"));
        var notify = _settings.NotifyPhase;
        if (Toggle(ctx, "realtorNotify", Loc.T("os.realtor_set_notify"), Loc.T("os.realtor_set_notify_hint"),
            x, width, ref notify))
        {
            _settings.NotifyPhase = notify;
        }

        var estate = _settings.NotifyEstate;
        if (Toggle(ctx, "realtorEstate", Loc.T("os.realtor_set_estate"), Loc.T("os.realtor_set_estate_hint"),
            x, width, ref estate))
        {
            _settings.NotifyEstate = estate;
        }
        ImGui.Dummy(new Vector2(width, ctx.Px(14f)));
    }

    private static void SectionLabel(OsAppContext ctx, float x, string text)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, ImGui.GetCursorScreenPos().Y + ctx.Px(4f)));
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.AccentLight);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
    }

    /// <summary>One settings card: label, wrapped explanation, checkbox on the right. The whole card is a
    /// click target, submitted after the box so the box keeps its own clicks.</summary>
    private static bool Toggle(OsAppContext ctx, string id, string label, string hint, float x, float width,
        ref bool value)
    {
        var dl = ImGui.GetWindowDrawList();
        var padIn = ctx.Px(12f);
        var boxSide = ImGui.GetFrameHeight();
        var textW = width - (padIn * 2f) - boxSide - ctx.Px(12f);
        var labelH = ImGui.CalcTextSize(label, false, textW).Y;
        var hintH = ImGui.CalcTextSize(hint, false, textW).Y;
        var tl = new Vector2(x, ImGui.GetCursorScreenPos().Y);
        var size = new Vector2(width, padIn + labelH + ctx.Px(4f) + hintH + padIn);
        var br = tl + size;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), ctx.Px(12f));
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), ctx.Px(12f),
            ImDrawFlags.RoundCornersAll, 1f);

        ImGui.SetCursorScreenPos(new Vector2(br.X - padIn - boxSide, tl.Y + (size.Y - boxSide) * 0.5f));
        var changed = ImGui.Checkbox($"##{id}", ref value);
        HandOnHover();

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##{id}Row", size))
        {
            value = !value;
            changed = true;
        }
        HandOnHover();

        ImGui.SetCursorScreenPos(tl + new Vector2(padIn, padIn));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextUnformatted(label);
        ImGui.SetCursorScreenPos(new Vector2(tl.X + padIn, tl.Y + padIn + labelH + ctx.Px(4f)));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Hint);
        ImGui.TextUnformatted(hint);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();

        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y + ctx.Px(8f)));
        return changed;
    }
}
