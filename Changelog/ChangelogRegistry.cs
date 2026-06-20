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
            Version: new Version(1, 0, 0),
            ReleaseDate: new DateOnly(2026, 06, 20),
            NewFeatures:
            [
                "The launch of Aetherlove v1.0.0",
                "This marks a beautiful moment after weeks of testing, laughing, coding and dreaming.",
                "Aetherlove is now available to everyone - Sweep with joy and share the love with your friends!",
                "We hope you'll make LOTS and LOTS of new friends, and meet beautiful new people through Aetherlove"
            ],
            BugFixes:
            [
                "All beta testers who helped report issues - thank you so very much!",
                "Without all of your hard work, this release wouldn't be possible.",
                "If you find any issues, please report them in the #bug-reports channel on our Discord server or via the in-app feedback form.",
            ],
            Important:
            [
                "Share Aetherlove with your friends - point them to www.aetherlove.space!",
                "Thank you for your support, and we hope you enjoy using Aetherlove as much as we enjoyed making it!",
                "Project team: Astraea & Nihal"
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
