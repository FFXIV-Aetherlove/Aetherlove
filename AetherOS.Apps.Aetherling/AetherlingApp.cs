using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Screens;
using AetherOS.Apps.Aetherling.Ui;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling;

/// <summary>An app that will not say what it is. It has a question mark for an icon, a name of "???", three
/// screens of noise for an introduction, and one thing to buy.</summary>
public sealed class AetherlingApp : IAetherApp
{
    internal const string AppId = "aetherling";

    private const string MysteryIconId = "unknown";
    private const string HatchedIconId = "unknown2";

    private const string TourSeenKey = "tourSeen";
    private const string MutedKey = "bgmMuted";
    private const string SoundsMutedKey = "soundsMuted";
    private const string SoundVolumeKey = "soundVolume";
    private const string IntroSeenKey = "petIntroFor";
    private const string FloatingKey = "floatingShow";
    private const string FloatingLockKey = "floatingLocked";
    private const string FloatingSizeKey = "floatingSize";
    private const string FloatingXKey = "floatingX";
    private const string FloatingYKey = "floatingY";

    private static readonly Vector4 TileTopColor = new(0.10f, 0.14f, 0.26f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.04f, 0.05f, 0.10f, 1f);

    private enum View { Onboarding, Adopt, Core, Pet, PetIntro, PetAbout, PetSettings, Wardrobe, AdultOnboarding, Games }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IAetherlingHost _host;
    private readonly IAppStorage _storage;
    private readonly PetRuntime _runtime = new();
    private readonly OnboardingScreen _onboarding;
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
    private bool _tourSeen;
    private bool _tourSeenLoaded;
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
        _onboarding = new OnboardingScreen(FinishOnboarding);
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
        _host.SoundsMuted = _storage.Get<bool?>(SoundsMutedKey) ?? false;
        _host.SoundVolume = _storage.Get<float?>(SoundVolumeKey) ?? IAetherlingHost.DefaultSoundVolume;
        _petSettings.SoundsChanged += SaveSoundSettings;
        SeedOwnedReactions();

        _pet.IntroRequested += OpenPetIntro;
        _pet.AboutRequested += () =>
        {
            _petAbout.OnShow();
            _view = View.PetAbout;
        };
        _pet.SettingsRequested += () => _view = View.PetSettings;
        _pet.GamesRequested += () =>
        {
            _games.OnShow(_host.Snapshot);
            _view = View.Games;
        };
        _games.BackRequested += () =>
        {
            _games.OnHide();
            _pet.OnShow(_host.Snapshot, justBorn: false);
            _view = View.Pet;
        };
        _games.MuteChanged += muted => _storage.Set(MutedKey, (bool?)muted);
        _pet.WardrobeRequested += () =>
        {
            _wardrobe.OnShow(_host.Snapshot);
            _view = View.Wardrobe;
        };
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
        _petSettings.SettingsChanged += SaveFloatingSettings;
        _petSettings.RecentreRequested += _floating.Recentre;
        _floating.Moved += SaveFloatingPosition;
        _floating.HideRequested += () =>
        {
            _petSettings.FloatingEnabled = false;
            SaveFloatingSettings();
        };
        _floating.StatusRequested += _host.OpenOnPhone;
        LoadFloatingSettings();
        _host.Overlay = _floating;
    }

    public string Id => AppId;

    public string Name => _name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Question;

    /// <summary>The mystery until this account has hatched, then the creature. Both files are named for the
    /// theme rather than the app, so the shipped art gives nothing away before the birth either.</summary>
    public ImTextureID? TileImage =>
        AppIcons.Tile(_host.Snapshot is { HatchedAtUtc: not null } ? HatchedIconId : MysteryIconId);

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
            case OsIntents.AetherlingReplayBirth:
                _view = View.Core;
                _core.OnShow(_host.Snapshot);
                _core.ReplayBirth();
                break;
            case OsIntents.AetherlingStatus:
                if (_host.Snapshot is { HatchedAtUtc: not null })
                {
                    _view = View.PetAbout;
                }
                break;
            case OsIntents.AetherlingReset:
                ForgetEverything();
                break;
            case OsIntents.AetherlingFeed:
                if (_host.Snapshot is { HatchedAtUtc: not null })
                {
                    _view = View.Pet;
                    _pet.OnShow(_host.Snapshot, justBorn: false);
                    _pet.OpenFeeding();
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
            _pet.OnShow(_host.Snapshot, justBorn: true);
        }

        switch (_view)
        {
            case View.Onboarding:
                _onboarding.Draw(ctx);
                break;
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
                    _petAbout.Draw(ctx, about, () => _view = View.Pet);
                }
                break;
            case View.PetSettings:
                if (_host.Snapshot is { } settings)
                {
                    _petSettings.Draw(ctx, settings, () => _view = View.Pet);
                }
                break;
            case View.Wardrobe:
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

        // Anything that navigated off the games takes their music with it, intents included. A no-op when
        // there was none, which is every frame of the rest of the app.
        if (_view != View.Games)
        {
            _games.OnHide();
        }

        SyncBgm();

        // No mute button once it is out: there is no loop to silence, and the toggle would restart one.
        // The games carry their own, because a run holds ImGui's active id and would kill this one.
        if (_view is View.Onboarding or View.Adopt or View.Core)
        {
            DrawMute(ctx);
        }
    }

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

    /// <summary>Back to before any of it, for the staff reset. The stored settings go too: they were answers
    /// to questions about a creature that no longer exists, and the next one asks them again.</summary>
    private void ForgetEverything()
    {
        _floating.Enabled = false;
        _floating.Locked = false;
        _floating.Position = null;
        _floating.SizeIndex = FloatingPet.DefaultSizeIndex;
        _petSettings.FloatingEnabled = false;
        _petSettings.FloatingLocked = false;
        _petSettings.FloatingSize = FloatingPet.DefaultSizeIndex;
        _petIntro.SizeIndex = FloatingPet.DefaultSizeIndex;
        _pet.IntroSeen = false;
        _storage.Set(IntroSeenKey, string.Empty);
        _storage.Set(FloatingKey, (bool?)false);
        _storage.Set(FloatingLockKey, (bool?)false);
        _storage.Set(FloatingSizeKey, (int?)FloatingPet.DefaultSizeIndex);
        _host.SoundsMuted = false;
        _host.SoundVolume = IAetherlingHost.DefaultSoundVolume;
        SaveSoundSettings();

        _view = View.Adopt;
        _adopt.OnShow();
    }

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
        if (!_tourSeenLoaded)
        {
            _tourSeen = _storage.Get<bool?>(TourSeenKey) ?? false;
            _tourSeenLoaded = true;
        }

        if (!_tourSeen)
        {
            if (_view != View.Onboarding)
            {
                _view = View.Onboarding;
                _onboarding.OnShow();
            }
            return;
        }

        if (core is { HatchedAtUtc: not null })
        {
            _pet.IntroSeen = _storage.Get<string>(IntroSeenKey) == IntroStamp();

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
                or View.Games))
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

    private void FinishOnboarding()
    {
        _tourSeen = true;
        _storage.Set(TourSeenKey, (bool?)true);
        ResolveView(_host.Snapshot);
    }

    private void OnBought()
    {
        _view = View.Core;
        _core.OnShow(_host.Snapshot);
        _core.BeginArrival();
    }

    private void DrawMute(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var side = Px(26f);
        var tl = new Vector2(origin.X + size.X - side - Px(14f), origin.Y + Px(14f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingMute", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(ctx.Localize(_host.BgmMuted ? "os.aetherling_unmute" : "os.aetherling_mute"));
        }
        if (pressed)
        {
            var muted = !_host.BgmMuted;
            _host.BgmMuted = muted;
            _storage.Set(MutedKey, (bool?)muted);
            // Unmuting picks the loop back up on the next frame, at whatever tempo the state calls for.
            // Naming one here would have to name the ceremony's, which is the wrong one for a growing pet.
        }

        var centre = tl + new Vector2(side * 0.5f, side * 0.5f);
        var alpha = hovered ? 0.75f : 0.32f;
        dl.AddCircleFilled(centre, side * 0.5f, Look.U32(Look.Crystal with { W = hovered ? 0.14f : 0.06f }), 20);
        IconDraw.AddCentered(dl, _host.BgmMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeDown,
            Px(12f), centre, Look.U32(Look.CrystalPale, alpha));
    }
}
