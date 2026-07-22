using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace AetherOS.Sdk;

/// <summary>Bridges a <see cref="ShareItem"/> onto the existing <see cref="OsIntent"/> transport, so share
/// delivery reuses <see cref="IOsShell.SendIntent"/> and the target's <see cref="IAetherApp.OnIntent"/> with no
/// new plumbing. A target handles the reserved <see cref="Type"/> in its OnIntent via <see cref="TryUnwrap"/>.</summary>
public static class ShareIntent
{
    /// <summary>The reserved intent type that carries a shared item.</summary>
    public const string Type = "os.share";

    public static OsIntent Wrap(ShareItem item) => new()
    {
        Type = Type,
        PayloadJson = JsonSerializer.Serialize(item),
    };

    public static bool TryUnwrap(OsIntent intent, [NotNullWhen(true)] out ShareItem? item)
    {
        item = null;
        if (intent.Type != Type || string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<ShareItem>(intent.PayloadJson);
            if (parsed is null || string.IsNullOrEmpty(parsed.Type))
            {
                return false;
            }
            item = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
