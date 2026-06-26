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
            Version: new Version(1, 3, 0),
            ReleaseDate: new DateOnly(2026, 06, 26),
            NewFeatures:
            [
                "You can now customize your chat bubble colors, with a new \"Language & Theme\" menu to manage them in one place.",
                "Tap your avatar to see new profile statistics, including how many people have loved your profile!",
                "The \"My Profile\" and \"Settings\" screens have been redesigned into cleaner, easier-to-navigate hubs.",
                "You can now search your matches by name or message content, and the matches list is smoother and faster even with lots of matches.",
                "You can verify a chat's end-to-end encryption from a new in-app screen, so you can confirm no one is intercepting your messages.",
                "Added XL and XXL phone sizes for high-resolution and 4K displays, plus a configurable size for the minimized phone.",
                "The chat screen now has a back button in its header and a tidier layout.",
                "The swipe deck now warns you when your next batch of cards is less than 5 minutes away.",
                "Minimize the phone instantly by double-clicking any of its edges."
            ],
            BugFixes:
            [
                "We fine-tuned a lot of rate limits that could previously cause odd behavior in the app, such as failing to save profile edits and/or pictures.",
                "When the AetherLove server can't be reached, you now always see an Offline screen. This wasn't previously the case.",
                "Uploaded photos with transparency no longer show up with gray blocks; they're now placed on a solid background."
            ],
            Important:
            [
                "This release is all about quality of life: lots of little improvements to make AetherLove smoother and nicer to use.",
                "Did you know we've already had over 1,000,000 swipes? Absolutely crazy!",
                "We've added a strict zero-tolerance policy on NSFL content (real or graphic gore, death, and similarly shocking imagery): uploading it results in an immediate and permanent ban, with no warning and no appeal. See the Terms of Service for details."
            ]
        ),
        new(
            Version: new Version(1, 2, 0),
            ReleaseDate: new DateOnly(2026, 06, 23),
            NewFeatures:
            [
                "Added a news and moderator-message system, an add-on to the warnings system already in place, so the team can reach you with updates and notes right inside AetherLove."
            ],
            BugFixes:
            [
                "Some users couldn't send or receive chats because of a problem with their end-to-end encryption setup. AetherLove now helps them fix it the next time they open the app.",
                "Some users were rate limited by mistake when updating their favorite songs. This is now fixed.",
                "Small improvements to some error messages."
            ],
            Important:
            [
                "AetherLove is ONLY available inside the game as a plugin. Do not trust any websites or Discord servers that promise to be a web version of the app. They are NOT safe."
            ]
        ),
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
