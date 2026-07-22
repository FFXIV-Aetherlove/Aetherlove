using System;

namespace AetherOS.Sdk;

/// <summary>Capability for apps to expose settings with one implementation hosted in two places (inside the app or in OS Settings).</summary>
public interface IAppSettings
{
    /// <summary>Draw settings content into the current region; draw a back pill only if onBack is non-null (in OS Settings context).</summary>
    void DrawSettings(OsAppContext ctx, Action? onBack);
}
