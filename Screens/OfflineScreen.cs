using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Signal;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Blocking screen shown when the SignalR hub connection drops; clears once it's restored.</summary>
public sealed class OfflineScreen
{
    private readonly AetherSignalService _signal;

    private ISharedImmediateTexture? _logoTex;
    private bool _logoLoaded;
    private const string LogoFileName = "logo_mini.png";

    public OfflineScreen(AetherSignalService signal)
    {
        _signal = signal;
    }

    public void OnShow() { }

    public void Draw()
    {
        EnsureLogo();

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var centerX = winPos.X + winSize.X * 0.5f;
        var curY = winPos.Y + winSize.Y * 0.14f;

        var logoWrap = _logoTex?.GetWrapOrDefault();
        var LogoSz = Px(70f);
        if (logoWrap != null)
        {
            dl.AddImage(logoWrap.Handle,
                new Vector2(centerX - LogoSz * 0.5f, curY),
                new Vector2(centerX + LogoSz * 0.5f, curY + LogoSz),
                Vector2.Zero, Vector2.One, 0x66FFFFFFu);
        }
        curY += LogoSz + Px(18f);

        var iconCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.42f, 0.42f, 1f));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var icon = FontAwesomeIcon.Plug.ToIconString();
        var iconRender = ImGui.GetFontSize() * 3.0f;
        var iconBaseSz = ImGui.CalcTextSize(icon);
        var iconGlyphW = iconBaseSz.X * (iconRender / ImGui.GetFontSize());
        var iconGlyphH = iconBaseSz.Y * (iconRender / ImGui.GetFontSize());
        var iconFont = ImGui.GetFont();
        ImGui.PopFont();
        dl.AddText(iconFont, iconRender, new Vector2(centerX - iconGlyphW * 0.5f, curY), iconCol, icon);
        curY += iconGlyphH + Px(18f);

        using (UiFonts.H2?.Push())
        {
            var Title = Loc.T("common.offline_title");
            var titleSz = ImGui.CalcTextSize(Title);
            ImGui.SetCursorScreenPos(new Vector2(centerX - titleSz.X * 0.5f, curY));
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.55f, 1f), Title);
        }
        curY = ImGui.GetCursorScreenPos().Y + Px(12f);

        var textW = winSize.X - Px(56f);
        var Body = Loc.T("common.offline_body");
        ImGui.SetCursorScreenPos(new Vector2(centerX - textW * 0.5f, curY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.82f, 1f), Body);
        ImGui.PopTextWrapPos();
        curY = ImGui.GetCursorScreenPos().Y + Px(22f);

        // Status: auto-reconnecting (spinner) vs. fully disconnected (manual retry).
        var state = _signal.State;
        if (state is SignalConnectionState.Reconnecting or SignalConnectionState.Connecting)
        {
            LoadingSpinner.Draw(new Vector2(centerX - Px(46f), curY + Px(9f)), Px(8f), Px(2.5f), ImGui.ColorConvertFloat4ToU32(t.AccentLight));
            ImGui.SetCursorScreenPos(new Vector2(centerX - Px(30f), curY));
            ImGui.TextColored(t.AccentLight, Loc.T("common.offline_reconnecting"));
        }
        else
        {
            var Msg = Loc.T("common.offline_keep_trying");
            var msgSz = ImGui.CalcTextSize(Msg);
            ImGui.SetCursorScreenPos(new Vector2(centerX - msgSz.X * 0.5f, curY));
            ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Msg);
            curY = ImGui.GetCursorScreenPos().Y + Px(12f);

            var BtnW = Px(150f);
            ImGui.SetCursorScreenPos(new Vector2(centerX - BtnW * 0.5f, curY));
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button($"{Loc.T("common.try_again")}##offlineRetry", new Vector2(BtnW, Px(32f))))
            {
                _ = Task.Run(async () =>
                {
                    try { await _signal.EnsureConnectedAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Plugin.Log.Warning(ex, "[OfflineScreen] Retry connect failed."); }
                });
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
    }

    private void EnsureLogo()
    {
        if (_logoLoaded)
        {
            return;
        }
        _logoLoaded = true;
        try
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", LogoFileName);
            if (File.Exists(path))
            {
                _logoTex = Plugin.TextureProvider.GetFromFile(path);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[OfflineScreen] Failed to load logo.");
        }
    }
}
