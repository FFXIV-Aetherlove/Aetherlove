using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Assets;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Screens;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling;

/// <summary>The Aetherling: a crystal to break open, and the creature that comes out of it. One thing to
/// buy on the way in, and every page after that belongs to whatever came out.</summary>
public sealed class AetherlingApp : IAetherApp
{
    public const string AppId = "aetherling";

    /// <summary>The one-time notice for owners the 2.7 change put back in front of a crystal. The host
    /// posts the OS notification under this tag from the login snapshot; the app clears it when the
    /// notice has been read. Both stamp the same storage keys, against the core's creation time, so a
    /// reset-and-rebuy is a new crystal with its own notice.</summary>
    public const string BornNoticeTag = "aetherling:born";
    public const string BornNoticeSeenKey = "bornNoticeSeenFor";
    public const string BornNoticePostedKey = "bornNoticePostedFor";
    public const string HungerReminderTag = "aetherling:hunger";
    public const string HungerReminderEnabledKey = "hungerReminders";
    public const string HungerReminderPostedKey = "hungerReminderPostedFor";

    private const string CoreIconId = "lumi";
    private const string HatchedIconId = "unknown2";

    private const string MutedKey = "bgmMuted";
    private const string BgmVolumeKey = "bgmVolume";
    private const string SoundsMutedKey = "soundsMuted";
    private const string SoundVolumeKey = "soundVolume";
    private const string WheelSeenKey = "wheelSeenFor";
    private const string TourSeenKey = "lumiTourSeenFor";

    /// <summary>Whether the forms explainer has been shown. Not stamped against a birth like the wheel's:
    /// what a form does is the same lesson for every creature this account ever raises.</summary>
    private const string FormsIntroKey = "formsIntroSeen";
    private const string TrackedUnlockKey = "trackedUnlock";
    private const string FloatingKey = "floatingShow";
    private const string FloatingLockKey = "floatingLocked";
    private const string FloatingSizeKey = "floatingSize";
    private const string FloatingXKey = "floatingX";
    private const string FloatingYKey = "floatingY";
    private const string LearnsEmotesKey = "learnsEmotes";
    private const string WorldGlyphsKey = "worldGlyphs";

    private static readonly Vector4 TileTopColor = new(0.10f, 0.14f, 0.26f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.04f, 0.05f, 0.10f, 1f);

    private enum View { Adopt, Core, Pet, Onboarding, PetAbout, Unlocks, PetSettings, Wardrobe, Emotes, Games }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IAetherlingHost _host;
    private readonly IAppCapabilities _caps;
    private readonly IAppStorage _storage;
    private readonly PetRuntime _runtime = new();
    private readonly PetLiveliness _liveliness;
    private readonly AdoptScreen _adopt;
    private readonly CoreScreen _core;
    private readonly PetScreen _pet;
    private readonly PetAboutScreen _petAbout;
    private readonly UnlocksScreen _unlocks;
    private readonly PetSettingsScreen _petSettings;
    private readonly WardrobeScreen _wardrobe;
    private readonly PetOnboardingScreen _onboarding;
    private readonly LumiTour _tour = new();
    private readonly GamesScreen _games;
    private readonly FloatingPet _floating;

    private View _view = View.Adopt;
    private bool _refreshing;
    private AetherlingDto? _refreshed;
    private float _noticeIn;
    private double _lastNoticeFrame;

    private AetherlingDto? _badgeFor;
    private bool _badgePending;

    /// <summary>What the account owns, asked once at startup: the creature out on the game screen boops
    /// long before the phone has been opened, and without this it would have nothing but its birth
    /// flourish to play until somebody visited a page that reads the inventory. A failed read leaves the
    /// set unknown, which is exactly the state that falls back to that one flourish.</summary>
    private void SeedOwnedReactions()
    {
        if (_host.Snapshot is not { Adult: not null })
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (await _host.GetOwnedItemsAsync().ConfigureAwait(false) is { } items)
                {
                    _runtime.SetOwnedReactions(
                        Engine.PetState.OwnedRefs(items, AetherLove.Shared.Store.StoreItemKind.AetherlingReaction));
                }
            }
            catch
            {
                // An unknown set is the safe state; the pet page fills it in the moment it is opened.
            }
        });
    }

    public AetherlingApp(Func<string> name, Func<bool> available, IAetherlingHost host, IAppCapabilities caps,
        AetherLove.Os.IArcadeScores scores)
    {
        _name = name;
        _available = available;
        _host = host;
        _caps = caps;
        _storage = caps.Storage(AppId);
        _adopt = new AdoptScreen(host);
        _core = new CoreScreen(host, _runtime);
        _pet = new PetScreen(host, _runtime);
        _petAbout = new PetAboutScreen(host, _runtime);
        _unlocks = new UnlocksScreen(host, _runtime);
        _petSettings = new PetSettingsScreen(host);
        _wardrobe = new WardrobeScreen(host, _runtime);
        _onboarding = new PetOnboardingScreen(host, _runtime);
        _games = new GamesScreen(host, _runtime, scores, _storage);
        _floating = new FloatingPet(host, _runtime);
        _host.BgmMuted = _storage.Get<bool?>(MutedKey) ?? false;
        _host.BgmVolume = _storage.Get<float?>(BgmVolumeKey) ?? 1f;
        _host.SoundsMuted = _storage.Get<bool?>(SoundsMutedKey) ?? false;
        _host.SoundVolume = _storage.Get<float?>(SoundVolumeKey) ?? IAetherlingHost.DefaultSoundVolume;
        _petSettings.SoundsChanged += SaveSoundSettings;
        SeedOwnedReactions();

        _pet.NamingSettled += () => ResolveView(_host.Snapshot);
        _pet.WheelFirstOpened += () => _storage.Set(WheelSeenKey, HatchStamp());
        _pet.WardrobeRequested += () =>
        {
            _wardrobe.OnShow(_host.Snapshot);
            _view = View.Wardrobe;
        };
        _pet.UnlocksRequested += () => ShowUnlocks(AetherlingElement.None);
        _petAbout.UnlocksRequested += ShowUnlocks;
        _unlocks.TrackedRef = _storage.Get<string>(TrackedUnlockKey) ?? string.Empty;
        _pet.TrackedUnlockRef = _unlocks.TrackedRef;
        _floating.PreferredElementKey = ShellCatalog.ElementOf(_unlocks.TrackedRef);
        _unlocks.TrackRequested += itemRef =>
        {
            _unlocks.TrackedRef = itemRef;
            _pet.TrackedUnlockRef = itemRef;
            _floating.PreferredElementKey = ShellCatalog.ElementOf(itemRef);
            _storage.Set(TrackedUnlockKey, itemRef);
        };
        _unlocks.WearRequested += shellRef =>
        {
            _wardrobe.OnShow(_host.Snapshot);
            _wardrobe.WearShell(shellRef);
            _view = View.Wardrobe;
        };
        _unlocks.RevealRequested += slot =>
        {
            if (_host.Snapshot is not { } snapshot)
            {
                return;
            }
            _view = View.Pet;
            _pet.OnShow(snapshot, justBorn: false);
            _pet.Ticket.Open(snapshot, slot);
        };
        // A form ticket ends where the form goes on: the wardrobe, opened on the forms socket with
        // the new one already worn, so the reward is on the creature before the page has settled.
        _pet.WardrobeFormRequested += shellRef =>
        {
            _wardrobe.OnShow(_host.Snapshot);
            _wardrobe.WearShell(shellRef);
            // Said once, on the first form anybody ever wins: what a form is for is not something the
            // wardrobe's rows can say by themselves.
            if (_storage.Get<bool?>(FormsIntroKey) is not true)
            {
                _storage.Set(FormsIntroKey, (bool?)true);
                _wardrobe.ExplainForms();
            }
            _view = View.Wardrobe;
        };
        _games.MuteChanged += muted => _storage.Set(MutedKey, (bool?)muted);
        _games.VolumeChanged += volume => _storage.Set(BgmVolumeKey, (float?)volume);
        _onboarding.GiftsDone += () => ResolveView(_host.Snapshot);
        _onboarding.FloatingChosen += ApplyFloatingChoice;
        _onboarding.Finished += () => ShowPetPage(_host.Snapshot);
        _tour.Finished += () =>
        {
            if (_host.Snapshot is not { Adult: not null } toured)
            {
                return;
            }
            _storage.Set(TourSeenKey, TourStamp(toured));
            if (toured.OnboardingDoneAtUtc is null)
            {
                ResolveView(toured);
            }
        };
        _petSettings.TourRequested += () => _tourRequested = true;
        _liveliness = new PetLiveliness(host, _runtime);
        _runtime.OnTick = _liveliness.Tick;
        _liveliness.LearnsEmotes = _storage.Get<bool?>(LearnsEmotesKey) ?? true;
        _floating.WorldGlyphs = _storage.Get<bool?>(WorldGlyphsKey) ?? true;
        _petSettings.LearnsEmotes = _liveliness.LearnsEmotes;
        _petSettings.WorldGlyphs = _floating.WorldGlyphs;
        _petSettings.HungerReminders = _storage.Get<bool?>(HungerReminderEnabledKey) ?? true;
        _liveliness.EmoteLearned += key =>
        {
            if (Engine.EmoteChoreographies.Find(key) is { } learnedDef)
            {
                _pet.ShowToast(Loc.T("os.aetherling_emote_learned", learnedDef.Name));
            }
        };
        _petSettings.SettingsChanged += SaveFloatingSettings;
        _petSettings.RecentreRequested += _floating.Recentre;
        _floating.Moved += SaveFloatingPosition;
        _floating.HideRequested += () =>
        {
            _petSettings.FloatingEnabled = false;
            SaveFloatingSettings();
        };
        _floating.StatusRequested += _host.OpenOnPhone;
        _floating.LockToggled += locked =>
        {
            _petSettings.FloatingLocked = locked;
            _storage.Set(FloatingLockKey, (bool?)locked);
        };
        LoadFloatingSettings();
        _host.Overlay = _floating;
        _host.InteractLab = new InteractLab(_runtime);
        var renderer = new PetRendererService(_host, _runtime);
        _host.PetRenderer = renderer;
        _floating.AssetsReady = () => _caps.Assets.IsReady(AssetPacks.Aetherling)
            && _caps.Assets.IsReady(AssetPacks.AetherlingAccessories);
        _caps.Assets.PackReady += pack =>
        {
            if (pack is not (AssetPacks.Aetherling or AssetPacks.AetherlingAccessories))
            {
                return;
            }
            _runtime.ReloadCatalogue();
            renderer.ReloadCatalogue();
            _floating.ReloadCatalogue();
        };
    }

    /// <summary>The dev window's handle into the creature: the real runtime, gates forced open.</summary>
    private sealed class InteractLab(PetRuntime runtime) : IAetherlingInteractLab
    {
        public void PlayEmote(string key, float amplitude)
        {
            if (Engine.EmoteChoreographies.Find(key) is { } def)
            {
                runtime.PlayEmote(def, amplitude, force: true);
            }
        }

        public void ShowGlyph(string name, string? then, string element) =>
            runtime.AuditionGlyph(name, then, element);

        public string Status => runtime.Ready
            ? $"ready, mood {runtime.Mood}, napping {runtime.Napping}, emote {runtime.CurrentEmote?.Key ?? "none"}"
            : "runtime not loaded (open the app or the floating pet once)";
    }

    public string Id => AppId;

    public string Name => _name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Gem;

    /// <summary>The crystal until this account has broken it, then the creature it became.</summary>
    public ImTextureID? TileImage =>
        AppIcons.Tile(_host.Snapshot is { Adult: not null } ? HatchedIconId : CoreIconId);

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    /// <summary>One, while the born notice is unread. Memoised against the snapshot, because the home
    /// screen asks every frame and the answer lives in storage.</summary>
    public int Badge
    {
        get
        {
            var core = _host.Snapshot;
            if (!ReferenceEquals(core, _badgeFor))
            {
                _badgeFor = core;
                _badgePending = NoticePending(core);
            }
            return _badgePending ? 1 : 0;
        }
    }

    public bool HasSurface => true;

    public bool Available => _available();

    public bool RequiresConnection => true;

    public bool UsesAccount => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnIntent(OsIntent intent)
    {
        switch (intent.Type)
        {
            case OsIntents.AetherlingStatus:
                if (_host.Snapshot is { Adult: not null })
                {
                    _view = View.PetAbout;
                }
                break;
            case OsIntents.AetherlingFeed:
                if (_host.Snapshot is { Adult: not null })
                {
                    _view = View.Pet;
                    _pet.OnShow(_host.Snapshot, justBorn: false);
                    _pet.OpenFeeding();
                }
                break;
            case OsIntents.AetherlingRename:
                if (_host.Snapshot is { Adult: not null })
                {
                    _view = View.Pet;
                    _pet.OnShow(_host.Snapshot, justBorn: false);
                    _pet.OpenRename();
                }
                break;
        }
    }

    public void OnForeground()
    {
        _pet.AnimateWheelOnEntry();
        ResolveView(_host.Snapshot);
        Refresh();
        // ResolveView only arms a screen it actually switches to, so re-entering while already on the adopt
        // screen would keep showing the balance from the first visit.
        _adopt.RefreshBalance();
        // Coming back from the store, the basket is the first thing the player looks at, and what they
        // just bought is not in this app's copy of the inventory yet.
        _pet.RefreshInventory();
        _wardrobe.RefreshInventory();
        _pet.RefreshWheel();
    }

    public void OnBackground()
    {
        _wardrobe.Flush();
        _floating.Hidden = false;
        _host.GameSessionActive = false;
        _games.OnHide();
        _host.StopBgm();
    }

    public void Draw(OsAppContext ctx)
    {
        // Every page draws from the Lumi art pack; until it has downloaded there is nothing to show but the wait.
        if (!_caps.Assets.IsReady(AssetPacks.Aetherling))
        {
            DrawAssetsPendingCard(AssetPacks.Aetherling, _caps.Assets.Progress(AssetPacks.Aetherling), ctx.ReduceMotion);
            return;
        }

        // While the crystal page, the tour or the gifts are up, the same creature must not also be
        // standing out on the game screen. Cleared on the way out of the app, which is the only path that
        // stops drawing here.
        _floating.Hidden = _view == View.Core || _tour.Active
            || (_view == View.Onboarding && !_onboarding.FloatingBeatReached);

        // Written every frame rather than on transitions so no exit path can leave it stuck; backgrounding
        // clears it separately since drawing stops there.
        _host.GameSessionActive = _view == View.Games && _games.RunActive;

        // Both of these land from hub continuations, and both navigate; the round trips park them here so
        // the swap happens on the draw thread.
        if (_adopt.TryTakePurchased())
        {
            OnBought();
        }
        if (Interlocked.Exchange(ref _refreshed, null) is { } core)
        {
            if (_view == View.Core)
            {
                _core.Apply(core);
            }
            else if (_view == View.Pet)
            {
                _pet.Apply(core);
            }
            else if (_view == View.Onboarding)
            {
                _onboarding.Apply(core);
            }
            else if (_view == View.Adopt)
            {
                ResolveView(core);
            }
        }
        if (_core.TryTakeBirthDone())
        {
            _view = View.Pet;
            _pet.WheelSeen = _storage.Get<string>(WheelSeenKey) == HatchStamp();
            _pet.OnShow(_host.Snapshot, justBorn: true);
        }

        switch (_view)
        {
            case View.Adopt:
                _adopt.Draw(ctx, PriceHint);
                break;
            case View.Core:
                _core.Draw(ctx);
                break;
            case View.Pet:
                _pet.Draw(ctx);
                break;
            case View.PetAbout:
                if (_host.Snapshot is { } about)
                {
                    _petAbout.Draw(ctx, about);
                }
                break;
            case View.Unlocks:
                if (_host.Snapshot is { } unlocks)
                {
                    _unlocks.Draw(ctx, unlocks);
                }
                break;
            case View.PetSettings:
                if (_host.Snapshot is { } settings)
                {
                    _petSettings.Draw(ctx, settings);
                }
                break;
            case View.Wardrobe:
            case View.Emotes:
                _wardrobe.Draw(ctx, () => ShowPetPage(_host.Snapshot));
                break;
            case View.Onboarding:
                _onboarding.Draw(ctx);
                break;
            case View.Games:
                _games.Draw(ctx);
                break;
        }

        DrawNav(ctx);
        if (_pendingTour && _view == View.Pet && _pet.Settled && _pet.PetRect is not null
            && _host.Snapshot is { Adult: not null } touring)
        {
            // The wheel arrives a round trip after the page: the script decides its wheel step at the start,
            // so the start waits for the button, and gives up waiting rather than never starting.
            _pendingTourFrames++;
            if (_pet.WheelRect is not null || _pendingTourFrames > PendingTourWaitFrames)
            {
                _pendingTour = false;
                _pendingTourFrames = 0;
                StartTour(ctx, touring, PetNavAction.Home);
            }
        }
        _pet.InputHeld = _tour.Active;
        if (_tourRequested)
        {
            _tourRequested = false;
            if (_host.Snapshot is { Adult: not null } replay)
            {
                StartTour(ctx, replay, CurrentNav);
            }
        }
        _tour.Draw();

        // Anything that navigated off the games takes their music with it, intents included. A no-op when
        // there was none, which is every frame of the rest of the app.
        if (_view != View.Games)
        {
            _games.OnHide();
        }

        SyncBgm();

        if (_view == View.Core && NoticePending(_host.Snapshot))
        {
            DrawBornNotice(ctx);
        }
        else if (MuteVisible)
        {
            // The games carry their own chip, because a run holds ImGui's active id and would kill this one;
            // the moments (the onboarding, a held page) have no corner to spare.
            DrawMute(ctx);
        }
    }

    private bool _chirpOnRelease;

    private bool MuteVisible => _view switch
    {
        View.Games or View.Onboarding => false,
        View.Pet => !_pet.HoldingPage,
        _ => true,
    };

    /// <summary>The app's only navigation. Drawn over whichever page is up, and left out of the pages that
    /// are a moment rather than a place (the crystal, the gifts, the floating question) and of a live
    /// minigame, which holds ImGui's active id and would leave every entry here structurally dead.</summary>
    private void DrawNav(OsAppContext ctx)
    {
        if (_host.Snapshot is not { Adult: not null } core || !NavVisible)
        {
            return;
        }

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var picked = PetNavBar.Draw(ctx, ImGui.GetWindowDrawList(),
            new Vector2(origin.X + (size.X * 0.5f), origin.Y + size.Y - PetNavBar.Reserved),
            CurrentNav, core.Emotes is not null);
        if (picked == PetNavAction.None)
        {
            return;
        }
        Navigate(picked);
    }

    /// <summary>Goes to a page of the bar. The tour drives this too, so every step stands on the real
    /// page rather than a picture of one.</summary>
    private void Navigate(PetNavAction page)
    {
        // Leaving the wardrobe with an unsaved look is how a device dresses half a pet: the page batches
        // writes, so every exit through here has to push them first.
        if (_view is View.Wardrobe or View.Emotes)
        {
            _wardrobe.Flush();
        }

        switch (page)
        {
            case PetNavAction.Home:
                if (_view == View.Games && _games.ConsumePlayedSinceShow())
                {
                    _pet.ShowPostGameHint();
                }
                ShowPetPage(_host.Snapshot);
                break;
            case PetNavAction.Games:
                _games.OnShow(_host.Snapshot);
                _view = View.Games;
                break;
            case PetNavAction.Wardrobe:
                _wardrobe.OnShow(_host.Snapshot);
                _view = View.Wardrobe;
                break;
            case PetNavAction.Emotes:
                _wardrobe.OnShow(_host.Snapshot, WardrobeScreen.Face.Performance);
                _view = View.Emotes;
                break;
            case PetNavAction.Stats:
                _petAbout.OnShow();
                _view = View.PetAbout;
                break;
            case PetNavAction.Settings:
                _view = View.PetSettings;
                break;
        }
    }

    private void ShowUnlocks(AetherlingElement focus)
    {
        _unlocks.OnShow(focus);
        _view = View.Unlocks;
    }

    private void StartTour(OsAppContext ctx, AetherlingDto core, PetNavAction from)
    {
        if (_tour.Active)
        {
            return;
        }
        _tour.Start(ctx, core, new LumiTour.Surfaces
        {
            Navigate = Navigate,
            NavRect = PetNavBar.SegmentRect,
            PetRect = () => _pet.PetRect,
            WheelRect = () => _pet.WheelRect,
            FoodSlotsRect = () => _pet.FoodSlotsRect,
            BasketRect = () => _pet.BasketRect,
            UnlockRect = () => _pet.UnlockRect,
            FirstGameRect = () => _games.FirstCardRect,
            FirstTrophyRect = () => _games.FirstTrophyRect,
            PaletteLaneRect = () => _wardrobe.PaletteLaneRect,
            SlotsRect = () => _wardrobe.SlotsRect,
            EmotesHeadingRect = () => _wardrobe.EmotesHeadingRect,
            ReactionsHeadingRect = () => _wardrobe.ReactionsHeadingRect,
            ShopPillRect = () => _wardrobe.ShopPillRect,
            ElementRowRect = () => _petAbout.ElementRowRect,
            RadarRect = () => _petAbout.RadarRect,
            ShowOutsideRect = () => _petSettings.ShowOutsideRect,
            TourRowRect = () => _petSettings.TourRowRect,
            EmotesAvailable = () => _host.Snapshot?.Emotes is not null,
        }, from == PetNavAction.None ? PetNavAction.Home : from);
    }

    /// <summary>The pages the bar belongs on. The games hub is one of them; a run and its leaderboard are
    /// not, because a run owns the keyboard and the leaderboard sits over a run that is paused behind it.</summary>
    private bool NavVisible => _view switch
    {
        View.Pet => !_pet.HoldingPage,
        View.PetAbout or View.Unlocks or View.PetSettings or View.Wardrobe or View.Emotes => true,
        View.Games => _games.AtHub,
        _ => false,
    };

    private PetNavAction CurrentNav => _view switch
    {
        View.Pet => PetNavAction.Home,
        View.Games => PetNavAction.Games,
        View.Wardrobe => PetNavAction.Wardrobe,
        View.Emotes => PetNavAction.Emotes,
        View.PetAbout => PetNavAction.Stats,
        View.Unlocks => PetNavAction.Stats,
        View.PetSettings => PetNavAction.Settings,
        _ => PetNavAction.None,
    };

    /// <summary>What should be playing, decided from the state rather than fired at each transition. The
    /// crystal has music, from its arrival until the shell gives; nothing after it does, because the
    /// music belongs to the becoming and a grown pet's page is quiet on purpose.</summary>
    private void SyncBgm()
    {
        // The games own the track while they are up, and they reconcile it the same way.
        if (_view == View.Games)
        {
            return;
        }
        if (_view == View.Core && _host.Snapshot is not null && _core.MusicWanted)
        {
            _host.StartBgm(CoreScreen.Tempo);
            return;
        }
        _host.StopBgm();
    }

    /// <summary>The floating question's answer. It is the floating pet's on switch, so saying yes puts it
    /// out there immediately, at the size it was asked at.</summary>
    private void ApplyFloatingChoice(bool wantsFloating, int sizeIndex)
    {
        _floating.Enabled = wantsFloating;
        _floating.SizeIndex = sizeIndex;
        _petSettings.FloatingEnabled = wantsFloating;
        _petSettings.FloatingSize = sizeIndex;
        _onboarding.FloatingEnabled = wantsFloating;
        _onboarding.SizeIndex = sizeIndex;
        _storage.Set(FloatingKey, (bool?)wantsFloating);
        _storage.Set(FloatingSizeKey, (int?)sizeIndex);
        if (wantsFloating)
        {
            // It comes out in the middle of the screen and unpinned, so the first thing anybody tries with it,
            // dragging it somewhere of their own, works.
            _floating.Locked = false;
            _petSettings.FloatingLocked = false;
            _storage.Set(FloatingLockKey, (bool?)false);
            _floating.Recentre();
        }
    }

    /// <summary>The wheel's pip is remembered against the birth it belongs to, so a creature that was
    /// reset and born again gets its pip again.</summary>
    private string HatchStamp() =>
        _host.Snapshot?.HatchedAtUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string AdultStamp(AetherlingDto core) =>
        core.Adult?.AdultAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>The tour's seen-stamp: the adult it was seen on, and the script revision it was. A new
    /// revision replays the tour once for every owner, grown-up creatures included.</summary>
    private static string TourStamp(AetherlingDto core) =>
        AdultStamp(core) + ":" + LumiTour.Revision.ToString(CultureInfo.InvariantCulture);

    private bool TourSeen(AetherlingDto core) => _storage.Get<string>(TourSeenKey) == TourStamp(core);

    private static string CoreStamp(AetherlingDto core) =>
        core.CreatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether this crystal's owner still has the born notice to read: a crystal waiting to be
    /// broken whose notice was never dismissed. A fresh purchase stamps it read on the way in, so only the
    /// owners the update put back in front of a crystal ever see it.</summary>
    private bool NoticePending(AetherlingDto? core) =>
        core is { Adult: null } && _storage.Get<string>(BornNoticeSeenKey) != CoreStamp(core);

    private void LoadFloatingSettings()
    {
        _floating.Enabled = _storage.Get<bool?>(FloatingKey) ?? false;
        _floating.Locked = _storage.Get<bool?>(FloatingLockKey) ?? false;
        _floating.SizeIndex = _storage.Get<int?>(FloatingSizeKey) ?? FloatingPet.DefaultSizeIndex;
        _petSettings.FloatingEnabled = _floating.Enabled;
        _petSettings.FloatingLocked = _floating.Locked;
        _petSettings.FloatingSize = _floating.SizeIndex;
        _onboarding.FloatingEnabled = _floating.Enabled;
        _onboarding.SizeIndex = _floating.SizeIndex;
        if (_storage.Get<float?>(FloatingXKey) is { } x && _storage.Get<float?>(FloatingYKey) is { } y)
        {
            _floating.Position = new Vector2(x, y);
        }
    }

    private void SaveFloatingSettings()
    {
        _floating.Enabled = _petSettings.FloatingEnabled;
        _floating.Locked = _petSettings.FloatingLocked;
        _floating.SizeIndex = _petSettings.FloatingSize;
        _onboarding.FloatingEnabled = _floating.Enabled;
        _onboarding.SizeIndex = _floating.SizeIndex;
        _storage.Set(FloatingKey, (bool?)_floating.Enabled);
        _storage.Set(FloatingLockKey, (bool?)_floating.Locked);
        _storage.Set(FloatingSizeKey, (int?)_floating.SizeIndex);
        _liveliness.LearnsEmotes = _petSettings.LearnsEmotes;
        _floating.WorldGlyphs = _petSettings.WorldGlyphs;
        _storage.Set(LearnsEmotesKey, (bool?)_petSettings.LearnsEmotes);
        _storage.Set(WorldGlyphsKey, (bool?)_petSettings.WorldGlyphs);
        _storage.Set(HungerReminderEnabledKey, (bool?)_petSettings.HungerReminders);
    }

    private void SaveSoundSettings()
    {
        _storage.Set(SoundsMutedKey, (bool?)_host.SoundsMuted);
        _storage.Set(SoundVolumeKey, (float?)_host.SoundVolume);
        // The settings page owns the music switch too now, and it shares the key the ceremony's own chip
        // and the minigames' chip write, so all three stay one answer.
        _storage.Set(MutedKey, (bool?)_host.BgmMuted);
    }

    private void SaveFloatingPosition(Vector2 position)
    {
        _storage.Set(FloatingXKey, (float?)position.X);
        _storage.Set(FloatingYKey, (float?)position.Y);
    }

    /// <summary>The adopt price shown before the server has spoken; the server prices the purchase.</summary>
    private static int PriceHint => 100;

    private void ShowPetPage(AetherlingDto? core)
    {
        _view = View.Pet;
        _pet.OnShow(core, justBorn: false);
    }

    /// <summary>Where the app stands, decided from the snapshot alone. A crystal waiting to be broken,
    /// old or new, is the crystal page; a grown creature that has not finished being welcomed resumes at
    /// the first unfinished step (the name, the gifts, the tour, the floating question); everything else
    /// is the creature's own pages.</summary>
    private void ResolveView(AetherlingDto? core)
    {
        if (core is null)
        {
            if (_view != View.Adopt)
            {
                _view = View.Adopt;
                _adopt.OnShow();
            }
            return;
        }

        if (core.Adult is null)
        {
            if (_view != View.Core)
            {
                _view = View.Core;
                _core.OnShow(core);
            }
            return;
        }

        _pet.WheelSeen = _storage.Get<string>(WheelSeenKey) == HatchStamp();

        if (core.OnboardingDoneAtUtc is null)
        {
            if (_tour.Active)
            {
                return;
            }
            if (_view == View.Pet && (!_pet.Settled || _pet.NamingOpen))
            {
                return;
            }
            if (!core.NameChosen)
            {
                if (_view != View.Pet)
                {
                    ShowPetPage(core);
                }
                _pet.OpenNamingCard();
                return;
            }
            if (GiftsUnscratched(core))
            {
                if (_view != View.Onboarding || _onboarding.FloatingBeatReached)
                {
                    _onboarding.OnShow(core, PetOnboardingScreen.Page.Gifts);
                    _view = View.Onboarding;
                }
                return;
            }
            if (!TourSeen(core))
            {
                ShowPetPage(core);
                _pendingTour = true;
                return;
            }
            if (_view != View.Onboarding || !_onboarding.FloatingBeatReached)
            {
                _onboarding.OnShow(core, PetOnboardingScreen.Page.Floating);
                _view = View.Onboarding;
            }
            return;
        }

        if (_view is not (View.Pet or View.PetAbout or View.Unlocks or View.PetSettings or View.Wardrobe or View.Emotes
            or View.Games))
        {
            ShowPetPage(core);
        }

        // An owner whose creature was grown before this script existed, or before its last revision, gets
        // it once, from the home page, the next time the app resolves.
        if (!TourSeen(core) && !_tour.Active && !_pendingTour)
        {
            ShowPetPage(core);
            _pendingTour = true;
        }
    }

    /// <summary>The tour needs the pet page to have drawn once, so its rings have something to point at:
    /// it is started on the frame after the resolver asked for it.</summary>
    private bool _pendingTour;
    private int _pendingTourFrames;
    private const int PendingTourWaitFrames = 180;

    /// <summary>The settings page asked for a replay; started on the next draw, which has the context.</summary>
    private bool _tourRequested;

    private static bool GiftsUnscratched(AetherlingDto core)
    {
        if (core.Cards is null)
        {
            return true;
        }
        var seen = 0;
        foreach (var card in core.Cards)
        {
            if (card.Slot > 2)
            {
                continue;
            }
            seen += 1;
            if (card.RevealedAtUtc is null)
            {
                return true;
            }
        }
        return seen < 3;
    }

    private void Refresh()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await _host.RefreshAsync().ConfigureAwait(false) is { } core)
                {
                    Interlocked.Exchange(ref _refreshed, core);
                }
            }
            finally
            {
                _refreshing = false;
            }
        });
    }

    private void OnBought()
    {
        if (_host.Snapshot is { } bought)
        {
            _storage.Set(BornNoticeSeenKey, CoreStamp(bought));
            _badgeFor = null;
        }
        _view = View.Core;
        _core.OnShow(_host.Snapshot);
        _core.BeginArrival();
    }

    /// <summary>The one-time notice over the crystal for the owners the 2.7 change put back in front of
    /// one. In-phone and in-page, in a layer of its own so it sits above the crystal's own draw; its scrim
    /// is submitted last so the button above it stays live.</summary>
    private void DrawBornNotice(OsAppContext ctx)
    {
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastNoticeFrame), 0f, 0.1f);
        _lastNoticeFrame = now;
        _noticeIn = ctx.ReduceMotion ? 1f : MathF.Min(1f, _noticeIn + (dt * 3.2f));
        var ease = 1f - MathF.Pow(1f - _noticeIn, 3f);

        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##aetherlingBornNotice", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, 0.94f * ease));

        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ease))
        {
            ImGui.SetCursorPos(new Vector2(0f, size.Y * 0.14f));
            OnboardingUi.DrawHero(CoreIconId, FontAwesomeIcon.Gem, ctx.Localize("os.aetherling_born_title"), null);
            OnboardingUi.DrawCenteredParagraph(ctx.Localize("os.aetherling_born_body"), size.X - Px(48f),
                Look.Whisper);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + size.Y - Px(54f)));
            if (OnboardingUi.DrawPrimaryButton(ctx.Localize("os.aetherling_born_cta"), true)
                && _host.Snapshot is { } core)
            {
                _storage.Set(BornNoticeSeenKey, CoreStamp(core));
                _badgeFor = null;
                _noticeIn = 0f;
                ctx.Shell.DismissByTag(BornNoticeTag);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##aetherlingBornScrim", size);
    }

    /// <summary>The noises chip, top right of every page that has a corner to give: the creature's chitter
    /// and the wheel's jingle, the one level the settings page calls "How loud". The music (the growth loop
    /// and the minigames' tracks) is deliberately not under it; the games carry their own chip for that and
    /// the settings page keeps the switch. Same place and dress as the games' chip, so it never seems to
    /// move.</summary>
    private void DrawMute(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var side = Px(32f);
        var tl = new Vector2(origin.X + size.X - side - Px(12f), origin.Y + Px(10f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingMute", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(ctx.Localize(_host.SoundsMuted ? "os.aetherling_unmute" : "os.aetherling_mute"));
        }
        if (pressed)
        {
            _host.SoundsMuted = !_host.SoundsMuted;
            SaveSoundSettings();
        }

        var centre = tl + new Vector2(side * 0.5f, side * 0.5f);
        dl.AddCircleFilled(centre, side * 0.5f, Look.U32(new Vector4(0f, 0f, 0f, hovered ? 0.5f : 0.32f)), 24);
        IconDraw.AddCentered(dl, _host.SoundsMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeDown,
            Px(11f), centre, Look.U32(Look.CrystalPale, 0.9f));

        DrawSoundVolume(dl, tl, new Vector2(side, side));
    }

    /// <summary>The level under the chip, the same number the settings page's slider moves. One chirp when
    /// the hand lets go, because a volume nobody hears is a number.</summary>
    private void DrawSoundVolume(ImDrawListPtr dl, Vector2 chipTl, Vector2 chipSize)
    {
        var muted = _host.SoundsMuted;
        var volume = _host.SoundVolume;
        if (VolumeBar.Draw("aetherlingSound", dl, chipTl, chipSize, ref muted, ref volume,
            Look.U32(Look.Crystal, 0.85f), Look.U32(Look.CrystalPale, 0.18f),
            Look.U32(Look.CrystalPale, 0.95f), UiScale.S))
        {
            _host.SoundVolume = volume;
            _host.SoundsMuted = muted;
            SaveSoundSettings();
            _chirpOnRelease = !muted;
        }
        if (_chirpOnRelease && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _chirpOnRelease = false;
            _host.PlayChirp();
        }
    }
}
