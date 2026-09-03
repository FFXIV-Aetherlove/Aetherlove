using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Screens;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling;

/// <summary>The Aetherling: an Aethercore to attune, and a creature to raise once it hatches. One thing to
/// buy on the way in, and every page after that belongs to whatever came out.</summary>
public sealed class AetherlingApp : IAetherApp
{
    internal const string AppId = "aetherling";

    private const string CoreIconId = "lumi";
    private const string HatchedIconId = "unknown2";

    private const string MutedKey = "bgmMuted";
    private const string BgmVolumeKey = "bgmVolume";
    private const string SoundsMutedKey = "soundsMuted";
    private const string SoundVolumeKey = "soundVolume";
    private const string IntroSeenKey = "petIntroFor";
    private const string WheelSeenKey = "wheelSeenFor";

    /// <summary>Whether the forms explainer has been shown. Not stamped against a hatch like the wheel's:
    /// what a form does is the same lesson for every creature this account ever raises.</summary>
    private const string FormsIntroKey = "formsIntroSeen";
    private const string FloatingKey = "floatingShow";
    private const string FloatingLockKey = "floatingLocked";
    private const string FloatingSizeKey = "floatingSize";
    private const string FloatingXKey = "floatingX";
    private const string FloatingYKey = "floatingY";
    private const string LearnsEmotesKey = "learnsEmotes";
    private const string WorldGlyphsKey = "worldGlyphs";

    private static readonly Vector4 TileTopColor = new(0.10f, 0.14f, 0.26f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.04f, 0.05f, 0.10f, 1f);

    private enum View { Adopt, Core, Pet, PetIntro, PetAbout, PetSettings, Wardrobe, Emotes, AdultOnboarding, Games }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IAetherlingHost _host;
    private readonly IAppStorage _storage;
    private readonly PetRuntime _runtime = new();
    private readonly PetLiveliness _liveliness;
    private readonly AdoptScreen _adopt;
    private readonly CoreScreen _core;
    private readonly PetScreen _pet;
    private readonly PetIntroScreen _petIntro;
    private readonly PetAboutScreen _petAbout;
    private readonly PetSettingsScreen _petSettings;
    private readonly WardrobeScreen _wardrobe;
    private readonly AdultOnboardingScreen _adultOnboarding;
    private readonly GamesScreen _games;
    private readonly FloatingPet _floating;

    private View _view = View.Adopt;
    private bool _refreshing;
    private AetherlingDto? _refreshed;

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
        _storage = caps.Storage(AppId);
        _adopt = new AdoptScreen(host);
        _core = new CoreScreen(host, host.StartBgm);
        _pet = new PetScreen(host, _runtime);
        _petIntro = new PetIntroScreen(_runtime, FinishPetIntro);
        _petAbout = new PetAboutScreen(host, _runtime);
        _petSettings = new PetSettingsScreen(host);
        _wardrobe = new WardrobeScreen(host, _runtime);
        _adultOnboarding = new AdultOnboardingScreen(host, _runtime);
        _games = new GamesScreen(host, _runtime, scores, _storage);
        _floating = new FloatingPet(host, _runtime);
        _host.BgmMuted = _storage.Get<bool?>(MutedKey) ?? false;
        _host.BgmVolume = _storage.Get<float?>(BgmVolumeKey) ?? 1f;
        _host.SoundsMuted = _storage.Get<bool?>(SoundsMutedKey) ?? false;
        _host.SoundVolume = _storage.Get<float?>(SoundVolumeKey) ?? IAetherlingHost.DefaultSoundVolume;
        _petSettings.SoundsChanged += SaveSoundSettings;
        SeedOwnedReactions();

        _pet.IntroRequested += OpenPetIntro;
        _pet.WheelFirstOpened += () => _storage.Set(WheelSeenKey, IntroStamp());
        _pet.WardrobeRequested += () =>
        {
            _wardrobe.OnShow(_host.Snapshot);
            _view = View.Wardrobe;
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
        _pet.AdultingFinished += () =>
        {
            _adultOnboarding.OnShow(_host.Snapshot);
            _view = View.AdultOnboarding;
        };
        // Straight into the wardrobe: they have just been handed three things to wear, and the page
        // that puts them on is the only sensible next screen. Anything but a grown pet reaching here
        // is the welcome bailing out, and that belongs back on its own page.
        _adultOnboarding.Finished += () =>
        {
            if (_host.Snapshot is { Adult: not null } grown)
            {
                _wardrobe.OnShow(grown);
                _view = View.Wardrobe;
                return;
            }
            _view = View.Pet;
            _pet.OnShow(_host.Snapshot, justBorn: false);
        };
        _liveliness = new PetLiveliness(host, _runtime);
        _runtime.OnTick = _liveliness.Tick;
        _liveliness.LearnsEmotes = _storage.Get<bool?>(LearnsEmotesKey) ?? true;
        _floating.WorldGlyphs = _storage.Get<bool?>(WorldGlyphsKey) ?? true;
        _petSettings.LearnsEmotes = _liveliness.LearnsEmotes;
        _petSettings.WorldGlyphs = _floating.WorldGlyphs;
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
        _host.PetRenderer = new PetRendererService(_host, _runtime);
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

    /// <summary>The core until this account has hatched, then the creature it became.</summary>
    public ImTextureID? TileImage =>
        AppIcons.Tile(_host.Snapshot is { HatchedAtUtc: not null } ? HatchedIconId : CoreIconId);

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

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
                if (_host.Snapshot is { HatchedAtUtc: not null })
                {
                    _view = View.PetAbout;
                }
                break;
            case OsIntents.AetherlingFeed:
                if (_host.Snapshot is { HatchedAtUtc: not null })
                {
                    _view = View.Pet;
                    _pet.OnShow(_host.Snapshot, justBorn: false);
                    _pet.OpenFeeding();
                }
                break;
            case OsIntents.AetherlingRename:
                if (_host.Snapshot is { HatchedAtUtc: not null })
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
        // While it is growing up or being welcomed, the same creature must not also be standing out on the
        // game screen. Cleared on the way out of the app, which is the only path that stops drawing here.
        _floating.Hidden = _view == View.AdultOnboarding || _pet.CeremonyRunning;

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
                _core.Apply(core, animate: true);
            }
            else if (_view == View.Pet)
            {
                _pet.Apply(core);
            }
            else if (_view == View.AdultOnboarding)
            {
                _adultOnboarding.Apply(core);
            }
            else if (_view == View.Adopt)
            {
                ResolveView(core);
            }
        }
        if (_core.TryTakeBirthDone())
        {
            _view = View.Pet;
            _pet.IntroSeen = _storage.Get<string>(IntroSeenKey) == IntroStamp();
            _pet.WheelSeen = _storage.Get<string>(WheelSeenKey) == IntroStamp();
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
            case View.PetIntro:
                _petIntro.Draw(ctx, PetName);
                break;
            case View.PetAbout:
                if (_host.Snapshot is { } about)
                {
                    _petAbout.Draw(ctx, about);
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
                _wardrobe.Draw(ctx, () =>
                {
                    _view = View.Pet;
                    _pet.OnShow(_host.Snapshot, justBorn: false);
                });
                break;
            case View.AdultOnboarding:
                _adultOnboarding.Draw(ctx);
                break;
            case View.Games:
                _games.Draw(ctx);
                break;
        }

        DrawNav(ctx);

        // Anything that navigated off the games takes their music with it, intents included. A no-op when
        // there was none, which is every frame of the rest of the app.
        if (_view != View.Games)
        {
            _games.OnHide();
        }

        SyncBgm();

        // The games carry their own chip, because a run holds ImGui's active id and would kill this one;
        // the moments (the introduction, growing up, a ceremony) have no corner to spare.
        if (MuteVisible)
        {
            DrawMute(ctx);
        }
    }

    private bool _chirpOnRelease;

    private bool MuteVisible => _view switch
    {
        View.Games or View.PetIntro or View.AdultOnboarding => false,
        View.Pet => !_pet.CeremonyRunning && !_pet.HoldingPage,
        _ => true,
    };

    /// <summary>The app's only navigation. Drawn over whichever page is up, and left out of the pages that
    /// are a moment rather than a place (the birth, the introduction, growing up) and of a live minigame,
    /// which holds ImGui's active id and would leave every entry here structurally dead.</summary>
    private void DrawNav(OsAppContext ctx)
    {
        if (_host.Snapshot is not { HatchedAtUtc: not null } core || !NavVisible)
        {
            return;
        }

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var picked = PetNavBar.Draw(ctx, ImGui.GetWindowDrawList(),
            new Vector2(origin.X + (size.X * 0.5f), origin.Y + size.Y - PetNavBar.Reserved),
            CurrentNav, core.Adult is not null, core.Emotes is not null);
        if (picked == PetNavAction.None)
        {
            return;
        }

        // Leaving the wardrobe with an unsaved look is how a device dresses half a pet: the page batches
        // writes, so every exit through here has to push them first.
        if (_view is View.Wardrobe or View.Emotes)
        {
            _wardrobe.Flush();
        }

        switch (picked)
        {
            case PetNavAction.Home:
                _pet.OnShow(_host.Snapshot, justBorn: false);
                _view = View.Pet;
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
            case PetNavAction.Help:
                OpenPetIntro();
                break;
        }
    }

    /// <summary>The pages the bar belongs on. The games hub is one of them; a run and its leaderboard are
    /// not, because a run owns the keyboard and the leaderboard sits over a run that is paused behind it.</summary>
    private bool NavVisible => _view switch
    {
        View.Pet => !_pet.CeremonyRunning && !_pet.HoldingPage,
        View.PetAbout or View.PetSettings or View.Wardrobe or View.Emotes => true,
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
        View.PetSettings => PetNavAction.Settings,
        _ => PetNavAction.None,
    };

    /// <summary>What should be playing, decided from the state rather than fired at each transition. The
    /// loop used to be started and stopped from half a dozen places in the view resolver, which was already
    /// one path short: leaving the minigames stopped their track and nothing brought the pet's back.
    ///
    /// <para>The crystal's music carries on past the hatch and climbs a step per growth form, and stops for
    /// good at the adult, whose page is quiet on purpose. It also stops through an evolution, so the higher
    /// tempo arrives with the new body rather than sliding under the takeover.</para></summary>
    private void SyncBgm()
    {
        // The games own the track while they are up, and they reconcile it the same way.
        if (_view == View.Games)
        {
            return;
        }
        if (_host.Snapshot is not { } core || _pet.CeremonyRunning || _view == View.AdultOnboarding)
        {
            _host.StopBgm();
            return;
        }
        if (core.HatchedAtUtc is null)
        {
            _host.StartBgm(CoreScreen.SpeedFor((AetherlingStage)core.CoreStage));
            return;
        }
        if (CoreScreen.GrowthSpeedFor(core) is { } growing)
        {
            _host.StartBgm(growing);
            return;
        }
        _host.StopBgm();
    }

    private string PetName => _host.Snapshot?.PetName ?? AetherlingLimits.DefaultName;

    private void OpenPetIntro()
    {
        _view = View.PetIntro;
        _petIntro.OnShow();
    }

    /// <summary>The end of the explanation, carrying the one question it asked. The answer is the floating
    /// pet's on switch, so saying yes puts it out there immediately, at the size it was asked at.</summary>
    private void FinishPetIntro(bool wantsFloating, int sizeIndex)
    {
        _storage.Set(IntroSeenKey, IntroStamp());
        _pet.IntroSeen = true;
        _floating.Enabled = wantsFloating;
        _floating.SizeIndex = sizeIndex;
        _petSettings.FloatingEnabled = wantsFloating;
        _petSettings.FloatingSize = sizeIndex;
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
        _view = View.Pet;
        _pet.OnShow(_host.Snapshot, justBorn: false);
    }

    /// <summary>The intro is remembered against the hatch it explained, so a creature that was reset and
    /// hatched again gets its introduction again rather than a page the player has never seen explained.</summary>
    private string IntroStamp() =>
        _host.Snapshot?.HatchedAtUtc?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ?? string.Empty;

    private void LoadFloatingSettings()
    {
        _floating.Enabled = _storage.Get<bool?>(FloatingKey) ?? false;
        _floating.Locked = _storage.Get<bool?>(FloatingLockKey) ?? false;
        _floating.SizeIndex = _storage.Get<int?>(FloatingSizeKey) ?? FloatingPet.DefaultSizeIndex;
        _petSettings.FloatingEnabled = _floating.Enabled;
        _petSettings.FloatingLocked = _floating.Locked;
        _petSettings.FloatingSize = _floating.SizeIndex;
        _petIntro.SizeIndex = _floating.SizeIndex;
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
        _petIntro.SizeIndex = _petSettings.FloatingSize;
        _storage.Set(FloatingKey, (bool?)_floating.Enabled);
        _storage.Set(FloatingLockKey, (bool?)_floating.Locked);
        _storage.Set(FloatingSizeKey, (int?)_floating.SizeIndex);
        _liveliness.LearnsEmotes = _petSettings.LearnsEmotes;
        _floating.WorldGlyphs = _petSettings.WorldGlyphs;
        _storage.Set(LearnsEmotesKey, (bool?)_petSettings.LearnsEmotes);
        _storage.Set(WorldGlyphsKey, (bool?)_petSettings.WorldGlyphs);
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

    private void ResolveView(AetherlingDto? core)
    {
        if (core is { HatchedAtUtc: not null })
        {
            _pet.IntroSeen = _storage.Get<string>(IntroSeenKey) == IntroStamp();
            _pet.WheelSeen = _storage.Get<string>(WheelSeenKey) == IntroStamp();

            // An adult that never finished its welcome resumes it: the sections restart, the
            // revealed cards stay revealed, which is the whole idempotency story.
            if (core is { Adult: not null, OnboardingDoneAtUtc: null })
            {
                if (_view != View.AdultOnboarding)
                {
                    _adultOnboarding.OnShow(core);
                    _view = View.AdultOnboarding;
                }
                return;
            }

            if (_view is not (View.Pet or View.PetIntro or View.PetAbout or View.PetSettings or View.Wardrobe
                or View.Emotes or View.Games))
            {
                _view = View.Pet;
                _pet.OnShow(core, justBorn: false);
            }
            return;
        }

        if (core is not null)
        {
            if (_view != View.Core)
            {
                _view = View.Core;
                _core.OnShow(core);
            }
            return;
        }

        if (_view != View.Adopt)
        {
            _view = View.Adopt;
            _adopt.OnShow();
        }
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
        _view = View.Core;
        _core.OnShow(_host.Snapshot);
        _core.BeginArrival();
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
