using System;

namespace AetherOS.Sdk;

/// <summary>One line an app owns on FFXIV's server info bar. The app's only verb is
/// <see cref="Set"/>; every gate (the master switch, the phone being on, the app still being on the
/// home screen, the per-app and per-entry toggles) belongs to the host, so a publisher never has to
/// ask permission before speaking and never has to fall silent by hand.</summary>
public interface IServerBarEntry
{
    /// <summary>Publishes <paramref name="text"/>, or clears the line when it is null or empty. An
    /// entry with nothing to say does not exist on the bar; it is never a reserved blank slot.</summary>
    void Set(string? text);

    /// <summary>The player's per-entry toggle, for the app's own settings page. The host stores it.</summary>
    bool Enabled { get; set; }
}

/// <summary>An app's slice of the server info bar, from
/// <see cref="IAppCapabilities.ServerBar"/>.</summary>
public interface IServerBar
{
    /// <summary>The player's per-app toggle: one switch that silences every entry the app owns.</summary>
    bool AppEnabled { get; set; }

    /// <summary>Registers (or returns) the app's entry named <paramref name="entryId"/>.
    /// <paramref name="title"/> is the STABLE English name Dalamud lists in its own settings and keys
    /// the player's bar ordering on, so it is never localized; <paramref name="labelKey"/> is the
    /// localization key settings pages show the entry under. <paramref name="onOpen"/> runs when the
    /// player clicks the line.</summary>
    IServerBarEntry Entry(string entryId, string title, string labelKey, Action? onOpen = null);
}
