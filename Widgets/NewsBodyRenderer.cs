using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Shared.News;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>Renders a news body — an ordered list of lines, each either text-with-emoji (reusing the chat
/// <see cref="ParsedMessage"/> renderer) or a centered image. Image textures are disk-cached, so scope one
/// renderer to the open article. Layout-only: the caller owns the surrounding scroll child + header, so the
/// same widget serves both the startup immediate-show and the Settings entry view. Each line self-positions at
/// <c>leftPad</c> (ImGui resets the cursor X after a wrapped-text child, so a one-time indent isn't enough).</summary>
public sealed class NewsBodyRenderer
{
    private readonly Dictionary<string, ISharedImmediateTexture?> _images = new();
    private readonly string _cacheDir =
        Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "NewsCache");

    public void Draw(string idScope, NewsLineDto[] lines, float leftPad, float contentWidth)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            ImGui.SetCursorPosX(leftPad);
            if (line.Kind == NewsLineKind.Image && line.ImageBytes is { Length: > 0 })
            {
                DrawImage($"{idScope}_{i}", line, leftPad, contentWidth);
            }
            else if (line.Kind == NewsLineKind.Text && !string.IsNullOrEmpty(line.Text))
            {
                ParsedMessage.Parse(line.Text).DrawWrapped($"{idScope}_t{i}", contentWidth);
            }
            ImGui.Spacing();
        }
    }

    private void DrawImage(string key, NewsLineDto line, float leftPad, float contentWidth)
    {
        if (!_images.TryGetValue(key, out var tex))
        {
            tex = AvatarDiskCache.Store(_cacheDir, key, line.ImageBytes!);
            _images[key] = tex;
        }
        var wrap = tex?.GetWrapOrDefault();
        if (wrap is null)
        {
            return;
        }

        float natW = line.Width is > 0 ? line.Width.Value : contentWidth;
        float natH = line.Height is > 0 ? line.Height.Value : contentWidth;
        var drawW = Math.Min(contentWidth, natW);
        var drawH = natH * (drawW / natW);

        var indent = Math.Max(0f, (contentWidth - drawW) * 0.5f);
        ImGui.SetCursorPosX(leftPad + indent);
        ImGui.Image(wrap.Handle, new Vector2(drawW, drawH));
    }
}
