using System;
using System.Collections.Generic;
using System.Reflection;

namespace AetherLove.Changelog;

/// <summary>Static list of release notes. Newest first. Matched against the plugin's assembly version
/// to show a one-time "What's New" window after an update.</summary>
public static class ChangelogRegistry
{
    private static readonly List<ChangelogEntry> _entries =
    [
        new(
            Version: new Version(0, 9, 6),
            ReleaseDate: new DateOnly(2026, 6, 3),
            NewFeatures:
            [
                "New \"Phone size\" setting (Small / Medium / Large) under Settings → Appearance, so you can make the whole app bigger on larger screens.",
                "Pressing Enter now sends a chat message and keeps the text box focused, so you can fire off messages back-to-back.",
            ],
            BugFixes:
            [
                "The phone now renders at a consistent size for everyone instead of growing oversized with Dalamud's global font-scale setting — use the new Phone size setting to scale it instead.",
                "Fixed chat messages (both sent and received) being cut off at the bottom.",
                "Fixed a crash that could happen when quickly switching between the deck and your chat list.",
                "Opening the deck is now instant when you already have cards, instead of reloading every time.",
                "Fixed display glitches and the small, awkward \"Sign out\" button on the passphrase unlock screen.",
            ],
            Important:
            []
        ),
        new(
            Version: new Version(0, 9, 3),
            ReleaseDate: new DateOnly(2026, 6, 2),
            NewFeatures:
            [],
            BugFixes:
            [
                "Emoji shortcodes (like :smile:) now show as emoji in profile bios instead of the raw text.",
                "Added an emoji picker and live preview to the \"About Me\" field when editing your profile — previously emoji could only be added during first-time setup.",
                "Fixed a hard crash that could happen when enabling \"NSFW Profiles: YES\" during onboarding.",
                "The phone now scales with Dalamud's global font-scale setting (/xlsettings), so larger text no longer overflows the layout.",
            ],
            Important:
            []
        ),
        new(
            Version: new Version(0, 9, 0),
            ReleaseDate: new DateOnly(2026, 5, 31),
            NewFeatures:
            [
                "Welcome to the beta!",
                "Swipe through adventurer profiles and match when the interest is mutual.",
                "Private one-on-one chat with your matches, end-to-end encrypted — not even AetherLove staff can read it.",
                "Build your profile with photos, languages, region, and content interests.",
                "Real-time notifications for new matches and messages, with optional sounds.",
            ],
            BugFixes:
            [],
            Important:
            [
                "Welcome to the AetherLove public beta! Thank you for helping us test. Please share feedback and report anything that feels off — your input shapes the release.",
            ]
        ),
    ];

    public static IReadOnlyList<ChangelogEntry> Entries => _entries;

    public static ChangelogEntry? GetEntry(Version version)
        => _entries.Find(e => e.Version.Major == version.Major
                           && e.Version.Minor == version.Minor
                           && e.Version.Build == version.Build);

    public static Version? CurrentVersion
        => Assembly.GetExecutingAssembly().GetName().Version;
}
