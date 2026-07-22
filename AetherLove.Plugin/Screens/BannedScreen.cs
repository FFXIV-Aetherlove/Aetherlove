using System;
using System.IO;
using System.Numerics;
using AetherLove;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Shown when the active dating profile is banned. The account session stays alive and the OS home
/// indicator is kept, so the user can leave and switch to another profile; only this profile is blocked.</summary>
public sealed class BannedScreen
{
    private readonly SessionBootstrapper _bootstrap;
    private ISharedImmediateTexture? _image;
    private bool _imageResolved;

    public BannedScreen(SessionBootstrapper bootstrap)
    {
        _bootstrap = bootstrap;
    }

    public void OnShow() { }

    private ISharedImmediateTexture? Illustration()
    {
        if (_imageResolved)
        {
            return _image;
        }
        _imageResolved = true;
        try
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", "icons", "blocked.png");
            if (File.Exists(path))
            {
                _image = UiHost.TextureProvider.GetFromFile(path);
            }
        }
        catch
        {
            _image = null;
        }
        return _image;
    }

    public void Draw()
    {
        DrawGate(Loc.T("common.banned_title"), Loc.T("common.banned_body"), _bootstrap.LastConnection?.BanReason);
    }

    /// <summary>Renders the banned illustration + heading/body/reason + home hint. Shared by the terminal
    /// per-profile banned screen and the in-app account-ban gate (which passes its own account-level copy).</summary>
    public void DrawGate(string title, string body, string? reason)
    {
        var padX = Px(24f);

        using var scroll = ImRaii.Child("##bannedGate", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        var winW = ImGui.GetContentRegionAvail().X;

        ImGui.Dummy(new Vector2(0f, Px(30f)));

        if (Illustration()?.GetWrapOrDefault() is { } wrap && wrap.Height > 0)
        {
            var imgW = MathF.Min(winW - padX * 2f, Px(220f));
            var imgH = imgW * ((float)wrap.Height / wrap.Width);
            ImGui.SetCursorPosX((winW - imgW) * 0.5f);
            ImGui.Image(wrap.Handle, new Vector2(imgW, imgH));
            ImGui.Dummy(new Vector2(0f, Px(20f)));
        }

        using (UiFonts.H1?.Push())
        {
            var tw = ImGui.CalcTextSize(title).X;
            ImGui.SetCursorPosX(MathF.Max(padX, (winW - tw) * 0.5f));
            ImGui.TextColored(new Vector4(0.95f, 0.40f, 0.40f, 1f), title);
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(winW - padX);
        ImGui.TextColored(new Vector4(0.90f, 0.90f, 0.90f, 1f), body);
        ImGui.PopTextWrapPos();

        if (!string.IsNullOrWhiteSpace(reason))
        {
            ImGui.Dummy(new Vector2(0f, Px(16f)));
            ImGui.SetCursorPosX(padX);
            ImGui.TextColored(UiColors.Muted, Loc.T("common.banned_reason_label"));
            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(new Vector4(0.88f, 0.88f, 0.88f, 1f), reason);
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, Px(18f)));
        ImGui.SetCursorPosX(padX);
        ImGui.PushTextWrapPos(winW - padX);
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Loc.T("common.banned_uninstall_hint"));
        ImGui.PopTextWrapPos();
    }
}
