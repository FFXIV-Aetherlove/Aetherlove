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
            Version: new Version(1, 1, 0),
            ReleaseDate: new DateOnly(2026, 06, 21),
            NewFeatures:
            [
                "Players can now select \"Japanese\" as a language spoken and language filter.",
                "It is now possible to copy ones profile text by right clicking on it and selecting the copy action.",
                "It is now possible to copy chat messages by right clicking on them and selecting the copy action.",
                "Players can now disable notifications while being in combat.",
                "Onboarding has been optimized - players can choose size + theme right from the start."
            ],
            BugFixes:
            [
                "Notifications (in any form) will only be sent when you are logged into a character.",
                "Youtube Music links now process correctly.",
                "Improved the contrast for the chat messages in the yellow theme."
            ],
            Important:
            [
                "We are overwhelmed with your support and swiping in the past 24 hours!",
                "Thank you for your support and enthusiasm, and thank you for swiping!",
                "After this release, we'll upgrade the amount of cards you'll get to 20!"
            ]
        ),
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
