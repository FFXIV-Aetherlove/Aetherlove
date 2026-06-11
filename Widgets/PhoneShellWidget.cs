using System;
using System.IO;
using System.Numerics;
using AetherLove;
using AetherLove.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>Draws the phone-frame background image for the current theme. Reloads when the theme changes.</summary>
public sealed class PhoneShellWidget : IDisposable
{
    private ISharedImmediateTexture? _texture;
    private string _loadedFile = "";

    public void DrawBackground(Vector2 windowPos, Vector2 windowSize)
    {
        EnsureTexture();
        var wrap = _texture?.GetWrapOrDefault();
        if (wrap == null)
        {
            return;
        }

        ImGui.GetWindowDrawList().AddImage(wrap.Handle, windowPos, windowPos + windowSize);
    }

    private void EnsureTexture()
    {
        var targetFile = ThemeService.Current.BackgroundImageFile;
        if (_loadedFile == targetFile)
        {
            return;
        }

        _loadedFile = targetFile;
        _texture = null;

        try
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? string.Empty;
            var path = Path.Combine(dir, "Media", targetFile);

            if (File.Exists(path))
            {
                _texture = Plugin.TextureProvider.GetFromFile(path);
            }
            else
            {
                Plugin.Log.Warning($"[PhoneShell] Background image not found: {path}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[PhoneShell] Failed to load {targetFile}");
        }
    }

    public void Dispose() { }
}
