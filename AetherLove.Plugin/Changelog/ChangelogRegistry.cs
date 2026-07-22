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
            Version: new Version(2, 0, 0),
            ReleaseDate: new DateOnly(2026, 07, 22),
            NewFeatures:
            [
                "AetherOS is here - a beautiful and wonderful Phone OS meant for more than just AetherLove!",
                "Lots of ways to personalize your phone, check out the settings app for more information!",
                "Add your favorite plugins to the homescreen for quick access!",
                "AetherLove, Places and Hangouts have now been split into 3 separate apps to further align with our vision for AetherOS.",
                "A new Messenger app has been added. Messenger allows for chats, group chats and sharing selfies with your besties. Everyone seems to love our chat, so we've made it available for chatting outside of AetherLove!",
                "If you've lost your passphrase, you're now able to reset it (accepting the loss of old chats).",
                "The settings app now leads to settings for all apps - one place to control it all."
            ],
            BugFixes:
            [
                "We fixed a number of performance issues, mostly regarding phone size, resizing, and font sizes.",
                "Too much to name to be honest!"
            ],
            Important:
            [
                "AetherLove is now AetherOS & AetherLove. We've worked hard to offer you a fantastic matching experience, and our hard work now continues in offering you so much more!",
                "With this update we're celebrating over 6 million swipes, 160,000 matches and almost 3 million messages sent!",
                "Please check our Discord for more information on AetherOS, but our guided tour inside the app will help you get started quickly and easily!"
            ]
        ),
        new(
            Version: new Version(1, 6, 0),
            ReleaseDate: new DateOnly(2026, 07, 16),
            NewFeatures:
            [
                "Places: a full directory of player-run venues, all from the comfort of your game. Browse what's happening now and later this week, filter by vibe and region, RSVP to events, and see who else is going.",
                "Find venues that cater to your likes with the filters you're familiar with. Rate and review venues, like your favorites, hide the ones that aren't for you, and share places with your matches to perhaps meet up.",
                "Venue owners can list their venue with a banner, logo, opening times, and location, and manage everything from the new My Venues editor. Venue owners can request access to the venues feature via a support ticket on our Discord.",
                "Hangouts: broadcast what you're up to right now (\"Ocean fishing, come join!\") and browse everyone else's hangouts in the new Hangouts tab. One tap tells the host you're on your way.",
                "Live hangouts also show up on your matches' profiles and in your matches list. Quickly invite people to a certain type of content, a certain type of activity, or find someone to gpose with!",
                "Supporting us: if you would like to support our project financially, you can do so via Patreon. Link your Patreon from inside the app to unlock supporter perks and help cover the server costs.",
                "Supporter perks include five extra profile photos, five reswipes a day, three superlikes a day, exclusive name styles and a supporter star, up to three images per RP character, a venue banner carousel, and personal profile statistics.",
                "Superlikes for everyone: each week you get one free superlike. When you superlike someone, you land right on top of their deck with a special reveal.",
                "Two new phone themes, Crystal Void and World of Lovecraft, plus refreshed backgrounds for every theme.",
                "A new Blocked Users page in Settings lets you review who you've blocked and unblock them in case you made a mistake blocking someone.",
                "New bezel buttons to minimize or close the phone."
            ],
            BugFixes:
            [
                "Ctrl+V now pastes into every multiline text field.",
                "Fixed ghost chats that could linger after the other person deleted their account.",
                "A brief server hiccup while connecting no longer drops you back into onboarding.",
                "You no longer get notified about news published before you registered.",
                "Large icons render crisply now.",
                "Signing in through XIVAuth is now more reliable."
            ],
            Important:
            [
                "AetherLove 1.6.0 is here! Our biggest update yet: This update adds \"places\", \"Hangouts\" and an option to support the project!",
                "With this update we will be celebrating 5,000,000 swipes and over 145,000 matches made. You just keep on swiping, it's ridiculous ❤️ !"
            ]
        ),
        new(
            Version: new Version(1, 5, 0),
            ReleaseDate: new DateOnly(2026, 07, 08),
            NewFeatures:
            [
                "You can now create multiple roleplay character profiles (OCs), each with its own bio and image, shown on your profile.",
                "Organize your matches into custom named, colored categories: drag chats into a category or right-click to move them, and reorder categories however you like.",
                "Take a profile or gallery photo straight from the game with the new in-game selfie tool; frame your shot with a live viewfinder while nameplates and hotbars hide automatically for a clean picture.",
                "A new phone appearance is available: the YoRHa Æ theme.",
                "Right-click an emoji to mark it as a favorite, and a Favorites row appears at the top of every emoji picker.",
                "Start typing an emoji shortcode in chat to get suggestions and autocomplete it.",
                "You can now search within a single conversation, not just across the matches list.",
                "Whenever we are doing planned server maintenance, the offline screen now relays the expected end time so you know what to expect.",
                "The chat message box now wraps and grows to multiple lines as you type.",
                "New settings let you keep AetherLove visible during group pose and cutscenes.",
                "Unsent chat text is now remembered when you navigate away and come back, and across reconnects.",
                "AetherLove now restores the phone to where you left off after a plugin update or restart."
            ],
            BugFixes:
            [
                "Picking a OneDrive-hosted file that hasn't been downloaded to your PC now shows a clear message instead of a wall of error text.",
                "The emoji hover tooltip is now legible on light-accent themes such as Yellow.",
                "AetherLove no longer changes other Dalamud plugins' font sizes, hopefully, we think.",
                "Switching phone size now shows a brief loader instead of a flash of wrong-sized text.",
                "Pressing Escape no longer unexpectedly closes the AetherLove windows.",
                "Moved the novelty notification opt-out into notification settings, a more logical place for it."
            ],
            Important:
            [
                "Thank you for updating to 1.5.0, one of our bigger UX updates yet!",
                "With this update we will be celebrating 3,500,000 swipes and over 100,000 matches made.",
                "This update focuses on adding multiple OC profiles and further improvements to chat and the organization of chats."
            ]
        ),
        new(
            Version: new Version(1, 4, 0),
            ReleaseDate: new DateOnly(2026, 07, 02),
            NewFeatures:
            [
                "Reswipe: accidentally swiped your future wife into the no pile? You can now undo your last swipe once every 24 hours!",
                "Decide later: send the card you're on to the back of your deck so you can come back and decide on it later.",
                "You can now quote messages, add emoji reactions, and pin messages in chat.",
                "Chats load faster, and searching is now instantaneous!",
                "Emoji reactions now surface your favorite and most-used reactions, just like Discord!",
                "Two new emoji collections: Memes, and Pepe and Friends.",
                "New content interests for matching: Ultimate, Field Operations, Deep Dungeon, and Variant/Criterion.",
                "Retired the \"Prefer not to say\" region option, which was causing problems with matching.",
                "New matches you haven't opened yet are highlighted with a subtle shine on the matches screen until you open them.",
                "The matches list no longer rearranges when you open a conversation, reply, and go back; it is much more fluid now.",
                "You can now lock the AetherLove phone in place from Settings, so it can't be moved by accident."
            ],
            BugFixes:
            [
                "Fixed an issue where Japanese characters could be cut off in the wrong places. AetherLove で素敵なご縁を！",
                "When your new deck of profiles appears, the profile you're currently looking at can still be swiped, so it is never lost.",
                "Improvements have been made to the notification system; let's hope this fixes some reported errors.",
                "Fixed situations where deleting an account wouldn't properly work and left accounts in half-deleted states.",
                "Optimized performance, queries, and indexes after you all decided to send hundreds of thousands of messages."
            ],
            Important:
            [
                "Time for a rather big update! We've listened to your feedback and added a bunch of new and fun things, plus performance improvements in several places. There is a lot to go over, so enjoy!",
                "New Terms of Service rules on race and gender: you must select your main race and gender, and your first profile picture must reflect them as set on your profile. The relevant screens now emphasize this. Breaking this rule can lead to a warning, and repeat offenses to a ban. See the Terms of Service for details.",
                "Thank you all for your continued support, your feedback, and your swipes. This update is for you!"
            ]
        ),
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
