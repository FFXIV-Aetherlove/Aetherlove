using System;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Racer;

/// <summary>Lumi Racer: the pet's racing career as its own app. The server resolves every race; this
/// app plays them back, keeps the crystal card, and hosts the party flow.</summary>
public sealed class RacerApp : IAetherApp
{
    private enum View
    {
        Home,
        Race,
        Stats,
        Stamps,
        DifficultyHelp,
        Waiting,
        Intro,
        Selection,
        Cup,
        Bonus,
        Packs,
        Inspect,
        Hand,
    }

    /// <summary>The onboarding version this player has finished. Bump <see cref="IntroVersion"/> when the
    /// pages change enough that every player should read them again; the old <c>racer.introSeen</c> flag is
    /// no longer read.</summary>
    private const string IntroVersionKey = "racer.introVersion";

    private const int IntroVersion = 2;
    private const string MutedKey = "muted";
    private const string VolumeKey = "volume";

    private readonly Func<string> _name;
    private readonly IRacerHost _host;
    private readonly IAppCapabilities _caps;
    private readonly AetherOS.Sdk.IAppStorage _storage;
    private readonly Screens.HomeScreen _home;
    private readonly Screens.RaceScreen _race;
    private readonly Screens.StatsScreen _stats;
    private readonly Screens.StampsScreen _stamps;
    private readonly Screens.DifficultyHelpScreen _difficultyHelp;
    private readonly Screens.WaitingRoomScreen _waiting;
    private readonly Screens.RaceOnboardingScreen _intro;
    private readonly Screens.CupScreen _cup;

    private readonly Screens.GrandstandFrame _frame;
    private readonly Screens.PacksScreen _packs;
    private readonly Screens.Cards.AlbumScreen _album;
    private readonly Screens.Cards.CardInspectScreen _inspect;
    private readonly Screens.Cards.RaceHandScreen _raceHand;

    private View _view = View.Home;

    private readonly Screens.RaceSelectionScreen _selection;
    private bool _muted;
    private float _volume = 1f;
    private const float CupFadeSeconds = 2f;
    private Guid? _openedPartyRace;
    private AetherLove.Shared.Racing.LumiRaceDto? _pendingParty;
    private AetherLove.Shared.Racing.LumiRaceRewardDto? _pendingPartyReward;

    public RacerApp(Func<string> name, IRacerHost host, IAppCapabilities caps)
    {
        _name = name;
        _host = host;
        _caps = caps;
        Screens.RacerFonts.Preload();
        _storage = caps.Storage("racer");
        _muted = _storage.Get<bool?>(MutedKey) == true;
        _volume = _storage.Get<float?>(VolumeKey) ?? 1f;
        _host.SetBgmVolume(_volume);
        _race = new Screens.RaceScreen(host, BackToHome, OpenStamps, () => _muted, ToggleMute, () => _volume, SetVolume);
        _cup = new Screens.CupScreen(host, caps, OpenCupRace, BackToHome, OpenCupHand);
        _home = new Screens.HomeScreen(host, caps, OpenRace, OpenSelection, OpenCup, OpenPacks, OpenStamps,
            OpenWaiting);
        _frame = new Screens.GrandstandFrame(host, BackToHome, OpenBonus, OpenStats, ReplayIntro, () => _muted, ToggleMute, () => _volume, SetVolume);
        _packs = new Screens.PacksScreen(host, () => _home.OnShow());
        _album = new Screens.Cards.AlbumScreen(host, _storage, OpenInspect);
        _inspect = new Screens.Cards.CardInspectScreen(host, BackFromInspect);
        _raceHand = new Screens.Cards.RaceHandScreen(host, _storage, OpenRace, EnterCup);
        _selection = new Screens.RaceSelectionScreen(host, OpenSoloHand, BackToHome, OpenDifficultyHelp);
        _stats = new Screens.StatsScreen(host);
        _stamps = new Screens.StampsScreen(host, OpenPacks);
        _difficultyHelp = new Screens.DifficultyHelpScreen(host, OpenSelection);
        _waiting = new Screens.WaitingRoomScreen(host, caps, BackToHome);
        _intro = new Screens.RaceOnboardingScreen(host, FinishIntro);
    }

    public string Id => "racer";

    public string Name => _name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.FlagCheckered;

    public Vector4 TileTop => new(0.31f, 0.22f, 0.56f, 1f);

    public Vector4 TileBottom => new(0.55f, 0.42f, 0.90f, 1f);

    public int Badge => _home.PendingPackCount;

    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>>? Strings =>
        Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        if ((_storage.Get<int?>(IntroVersionKey) ?? 0) < IntroVersion)
        {
            _intro.Show();
            _view = View.Intro;
        }
        _home.OnShow();
        if (_view == View.Cup)
        {
            _cup.OnShow();
        }
        if (!_muted && _view is not (View.Race or View.Cup))
        {
            _host.StartMenuBgm();
        }
    }

    public void OnBackground()
    {
        _race.OnHidden();
        _host.StopBgm();
    }

    public void Draw(OsAppContext ctx)
    {
        // The grandstand and the racers draw from two packs; until both have downloaded there is only the wait.
        foreach (var pack in new[] { AetherLove.Shared.Assets.AssetPacks.Racer, AetherLove.Shared.Assets.AssetPacks.Aetherling })
        {
            if (!_caps.Assets.IsReady(pack))
            {
                DrawAssetsPendingCard(pack, _caps.Assets.Progress(pack), ctx.ReduceMotion);
                return;
            }
        }

        // A party race the host began arrives by push, and the pushed run carries no viewer slot, so
        // the stage opens from a per-viewer fetch. Watched here rather than on a screen: it must fire
        // whichever one is up.
        if (_host.PartyRun is { Status: (short)AetherLove.Shared.Racing.LumiRacePartyRunStatus.Active, Race: { } run }
            && _openedPartyRace != run.RaceId)
        {
            _openedPartyRace = run.RaceId;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var mine = await _host.RefreshPartyRunAsync().ConfigureAwait(false);
                    if (mine is { Race: { } race })
                    {
                        _pendingParty = race;
                        _pendingPartyReward = mine.Reward;
                    }
                }
                catch (Exception)
                {
                }
            });
        }
        if (_pendingParty is { } pending)
        {
            _pendingParty = null;
            // Never restart a race that is already on the stage: a late duplicate would rewind the
            // presentation to the parade and fire the start bang over whatever the player is watching.
            if (_view != View.Race)
            {
                OpenRace(new AetherLove.Shared.Racing.LumiRaceStartResultDto(
                    pending,
                    _pendingPartyReward
                        ?? new AetherLove.Shared.Racing.LumiRaceRewardDto(0, 0, false, 0, false)));
            }
            _pendingPartyReward = null;
        }

        switch (_view)
        {
            case View.Bonus:
                _frame.Draw(ctx, 1, "os.racer_bonus_cards", () => _album.Draw(ctx), contentPage: true,
                    backgroundName: "album-elemental-circuit-bg", contentInk: Screens.GrandstandFrame.Ink,
                    contentInset: 26f);
                break;
            case View.Inspect:
                _frame.Draw(ctx, 1, null, () => _inspect.Draw(ctx), true, contentPage: true);
                break;
            case View.Hand:
                _frame.Draw(ctx, -1, "os.racer_hand_title", () => _raceHand.Draw(ctx, BackFromHand), true, contentPage: true,
                    overlay: () => _raceHand.DrawOverlays(ctx), overlayOpen: _raceHand.PickerOpen);
                break;
            case View.Packs:
                _frame.Draw(ctx, -1, _packs.IsOpening ? null : "os.racer_packs_title", () => _packs.Draw(ctx), contentPage: true);
                break;
            case View.Cup:
                _cup.Drain();
                if (_cup.ShowingPodium)
                {
                    _cup.Draw(ctx);
                }
                else
                {
                    _frame.Draw(ctx, -1, "os.racer_cup_title", () => _cup.Draw(ctx, framed: true), true, contentPage: true);
                }

                break;
            case View.Selection:
                _frame.Draw(ctx, -1, "os.racer_pick_title", () => _selection.Draw(ctx), contentPage: true);
                break;
            case View.Race:
                _race.Draw(ctx);
                break;
            case View.Stats:
                _frame.Draw(ctx, 2, null, () => _stats.Draw(ctx), true, contentPage: true);
                break;
            case View.Stamps:
                _frame.Draw(ctx, -1, "os.racer_stamp_card", () => _stamps.Draw(ctx), contentPage: true);
                break;
            case View.DifficultyHelp:
                _frame.Draw(ctx, -1, "os.racer_diff_help_title", () => _difficultyHelp.Draw(ctx), true, contentPage: true);
                break;
            case View.Waiting:
                _frame.Draw(ctx, -1, "os.racer_waiting_title", () => _waiting.Draw(ctx), contentPage: true);
                break;
            case View.Intro:
                // The gate is said before the pages about racing rather than after them: a player whose
                // creature cannot race yet learns that first.
                _frame.Draw(ctx, -1, null, () => _intro.Draw(ctx), true, contentPage: true,
                    overlay: () => _home.DrawGateOverlay(ctx), overlayOpen: _home.GateOpen);
                break;
            default:
                _frame.Draw(ctx, 0, null, () => _home.Draw(ctx), overlay: () => _home.DrawOverlays(ctx), overlayOpen: _home.OverlayOpen);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    private void OpenRace(AetherLove.Shared.Racing.LumiRaceStartResultDto result)
    {
        _race.Begin(result);
        _view = View.Race;
    }

    private void OpenStats()
    {
        _stats.OnShow();
        _view = View.Stats;
    }

    /// <summary>The Album, reloaded from the server every time the tab is chosen.</summary>
    private void OpenBonus()
    {
        _album.OnShow();
        _view = View.Bonus;
    }

    /// <summary>A card up close, from the Album.</summary>
    private void OpenInspect(AetherLove.Shared.Racing.Cards.RaceCard card, Screens.Cards.OwnedCard owned)
    {
        _inspect.Show(card, owned);
        _view = View.Inspect;
    }

    /// <summary>Back from a card to the Album as it was left: no reload, so its scroll and filters survive.</summary>
    private void BackFromInspect() => _view = View.Bonus;

    /// <summary>A chosen offer goes to the race hand screen, which saves the hand and starts the race.</summary>
    private void OpenSoloHand(AetherLove.Shared.Racing.LumiRaceOfferDto offer)
    {
        _raceHand.ShowSolo(offer);
        _view = View.Hand;
    }

    private void OpenCupHand()
    {
        _raceHand.ShowCup();
        _view = View.Hand;
    }

    private void EnterCup()
    {
        _view = View.Cup;
        _cup.Enter();
    }

    /// <summary>Back from the hand to the page that opened it, as it was left.</summary>
    private void BackFromHand() => _view = _raceHand.ForCup ? View.Cup : View.Selection;

    private void OpenPacks()
    {
        _packs.OnShow();
        _view = View.Packs;
    }

    private void OpenCup()
    {
        _cup.OnShow();
        _view = View.Cup;
        _host.FadeOutBgm(CupFadeSeconds);
    }

    private void OpenCupRace(AetherLove.Shared.Racing.LumiRaceStartResultDto result)
    {
        _race.Begin(result, () =>
        {
            _cup.FinishRace();
            _view = View.Cup;
        });
        _view = View.Race;
    }

    private void OpenWaiting()
    {
        _waiting.OnShow();
        _view = View.Waiting;
    }

    private void FinishIntro()
    {
        _storage.Set(IntroVersionKey, (int?)IntroVersion);
        BackToHome();
    }

    /// <summary>The home page's way back into the onboarding, for a player who wants the tour again.</summary>
    private void ReplayIntro()
    {
        _intro.Show();
        _view = View.Intro;
    }

    private void OpenStamps()
    {
        _stamps.OnShow();
        _view = View.Stamps;
    }

    /// <summary>Opens the page that explains what the three grades change.</summary>
    private void OpenSelection()
    {
        _selection.OnShow();
        _view = View.Selection;
    }

    private void OpenDifficultyHelp()
    {
        _difficultyHelp.OnShow();
        _view = View.DifficultyHelp;
    }

    private void BackToHome()
    {
        _view = View.Home;
        _home.OnShow();
        if (!_muted)
        {
            _host.StartMenuBgm();
        }
        else
        {
            _host.StopBgm();
        }
    }

    /// <summary>The level under the same chip, kept apart from the mute so silencing the race and
    /// then bringing it back returns to the volume the player chose.</summary>
    private void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        _storage.Set(VolumeKey, (float?)_volume);
        _host.SetBgmVolume(_volume);
    }

    /// <summary>One switch for every screen. Muting stops whatever plays; unmuting restarts the track
    /// the current screen owns.</summary>
    private void ToggleMute()
    {
        _muted = !_muted;
        _storage.Set(MutedKey, (bool?)_muted);
        if (_muted)
        {
            _host.StopBgm();
        }
        else if (_view == View.Race)
        {
            _race.ResumeBgm();
        }
        else if (_view != View.Cup)
        {
            _host.StartMenuBgm();
        }
    }
}
