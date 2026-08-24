using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling;
using AetherOS.Apps.Groove;
using AetherOS.Sdk;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace AetherLove.Os;

/// <summary>The Aetherling app's host: hub passthroughs, the looping ceremony track, and the game-BGM duck
/// that goes with it.</summary>
public sealed class AetherlingHostService : IAetherlingHost, IDisposable
{
    private const string BgmFile = "crystallic.ogg";
    private const string CrackFile = "crack.ogg";
    private const string GameBgmFolder = "bgm";
    private const string VoiceFolder = "sfx";
    private const string ChirpPrefix = "aetherling_chirp_";
    private const string ResponsePrefix = "aetherling_response_";

    /// <summary>A floor under the gap between two of the creature's noises. <see cref="OneShotSound.Busy"/>
    /// is the real limiter; this only covers the case where a clip fails to load, which is never busy and
    /// would otherwise re-fire on every click of a burst.</summary>
    private const double MinVoiceGapSeconds = 0.25;

    /// <summary>Where the voice starts before anyone touches the slider: a companion, not an
    /// announcement, so its noises sit well under the game.</summary>
    private const float VoiceLevel = 0.2f;

    /// <summary>One step per growth form, so the voice audibly settles as the creature grows up. The
    /// adult sits at 1.0 because the clips were recorded for it.</summary>
    private const float HatchlingPitch = 1.32f;
    private const float Hatchling2Pitch = 1.20f;
    private const float Hatchling3Pitch = 1.09f;

    private readonly AetherHubContext _hub;
    private readonly SessionBootstrapper _bootstrap;
    private readonly IAppCapabilities _capabilities;
    private readonly Services.Together.TogetherStateService _party;
    private readonly Config.Configuration _config;
    private readonly Services.Sparks.SparkActivityReporter _sparkReporter;
    private readonly BgmPlayer _bgm = new();
    private readonly OneShotSound _sfx = new();
    private readonly OneShotSound _voice = new();
    private readonly ShuffleBag _chirps = new(ChirpPrefix);
    private readonly ShuffleBag _responses = new(ResponsePrefix);

    /// <summary>How often the game's sound settings and the window's focus are re-read. Fast enough that
    /// alt-tabbing feels immediate, slow enough that it is free.</summary>
    private const double AudioPollSeconds = 0.25;

    private AetherlingDto? _snapshot;
    private bool _snapshotSeeded;
    private bool _mutedGameBgm;
    private bool _muted;
    private bool _silenced;
    private double _audioAccum;
    private string? _track;
    private double _lastVoiceAt = double.NegativeInfinity;
    private string _job = "";
    private ushort _emoteHeld;
    private uint _emoteRow;
    private long _emoteSeq;

    public AetherlingHostService(AetherHubContext hub, SessionBootstrapper bootstrap, IAppCapabilities capabilities,
        Services.Sparks.SparkActivityReporter sparkReporter, Services.Together.TogetherStateService party,
        Config.Configuration config)
    {
        _hub = hub;
        _bootstrap = bootstrap;
        _capabilities = capabilities;
        _sparkReporter = sparkReporter;
        _party = party;
        _config = config;
        BatteryService.HoldEmpty = () => GameSessionActive;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.Framework.Update += OnTick;
    }

    public void NoteGameFinished() => _sparkReporter.NoteAetherlingGameFinished();

    /// <summary>The party's pets, minus your own row: the creature standing out there is already yours, and
    /// a second copy of it in the huddle reads as a bug. Members who never hatched or turned sharing off
    /// carry no pet on their row, so they drop out here without a second rule.</summary>
    public IReadOnlyList<AetherOS.Apps.Aetherling.AetherlingPartyPet> PartyPets
    {
        get
        {
            if (!_config.OsSettings.PartyPetsShown)
            {
                return [];
            }
            var members = _party.Members;
            if (members.Count == 0)
            {
                return [];
            }
            var own = _party.OwnAccountId;
            var pets = new List<AetherOS.Apps.Aetherling.AetherlingPartyPet>(members.Count);
            foreach (var member in members)
            {
                if (member.Pet is not { } pet || member.AccountId == own)
                {
                    continue;
                }
                pets.Add(new AetherOS.Apps.Aetherling.AetherlingPartyPet(
                    member.AccountId, pet.Stage, pet.Palette, pet.Accessories, pet.Name));
            }
            return pets;
        }
    }

    public int PartyPetSize => _config.OsSettings.PartyPetSize;

    public bool GameSessionActive { get; set; }

    public IAetherlingInteractLab? InteractLab { get; set; }

    public AetherlingDto? Snapshot
    {
        get
        {
            // The login snapshot is the first answer, so a returning owner sees their core without waiting
            // for a round trip. It latches only once there IS a connection to read: the home screen asks for
            // the tile's name every frame from the moment the phone opens, which is well before the hub is
            // up, and latching on that first empty read left the tile saying "???" until the app was opened.
            if (!_snapshotSeeded && _bootstrap.LastConnection is { } connection)
            {
                _snapshotSeeded = true;
                _snapshot = connection.Aetherling;
            }
            return _snapshot;
        }
    }

    /// <summary>The name to show wherever the app is listed, or null while nothing has hatched. Reads the
    /// login snapshot, so the home tile is right at boot without anyone opening the app.</summary>
    public string? PetName => Snapshot is { HatchedAtUtc: not null, PetName: { Length: > 0 } name } ? name : null;

    private static string MediaRoot =>
        Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Media");

    public string AssetRoot => Path.Combine(MediaRoot, "unknown");

    public string SoundRoot => Path.Combine(MediaRoot, VoiceFolder);

    /// <summary>Forgets the core entirely, for the staff reset. Seeded stays true, or the next read falls
    /// back to the login snapshot and resurrects the thing that was just deleted.</summary>
    public void ClearSnapshot()
    {
        _snapshotSeeded = true;
        _snapshot = null;
        StopBgm();
    }

    public IAetherlingOverlay? Overlay { get; set; }

    public AetherOS.Sdk.IPetRenderer? PetRenderer { get; set; }

    public ITextureCache Textures => _capabilities.Textures;

    public bool ReduceMotion => AccessibilityService.ReduceMotion;

    /// <summary>Handed over by the plugin at startup: this service is constructed before the windows are
    /// wired, so it cannot take one in its constructor.</summary>
    public Action? PhoneOpener { get; set; }

    public void OpenOnPhone() => PhoneOpener?.Invoke();

    public async Task<AetherlingDto?> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            _snapshotSeeded = true;
            _snapshot = await _hub.GetAetherlingAsync(ct).ConfigureAwait(false);
            return _snapshot;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherling] Core fetch failed.");
            return null;
        }
    }

    /// <summary>Sending half of party pets. Lives here rather than in the app because the shell's party
    /// surfaces own the switch: the pet's own pages never mention the party.</summary>
    public async Task<AetherlingDto> SetPartySharingAsync(bool shares, CancellationToken ct = default)
    {
        var dto = await _hub.SetAetherlingPartySharingAsync(shares, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> PurchaseAsync(CancellationToken ct = default)
    {
        var dto = await _hub.PurchaseAethercoreAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> ChargeAsync(CancellationToken ct = default)
    {
        var dto = await _hub.ChargeAethercoreAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> HatchAsync(CancellationToken ct = default)
    {
        var dto = await _hub.HatchAethercoreAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> NameAsync(string name, CancellationToken ct = default)
    {
        var dto = await _hub.NameAetherlingAsync(name, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> RenameAsync(string name, CancellationToken ct = default)
    {
        var dto = await _hub.RenameAetherlingAsync(name, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<long?> GetSparkBalanceAsync(CancellationToken ct = default)
    {
        try
        {
            return (await _hub.GetSparkWalletAsync(ct).ConfigureAwait(false)).Balance;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherling] Balance fetch failed.");
            return null;
        }
    }

    public async Task<AetherlingDto> FeedAsync(short element, CancellationToken ct = default)
    {
        // The job rides every feed; the server only reads it on the one that grows the pet up.
        var dto = await _hub.FeedAetherlingAsync(element, CurrentJobAbbreviation, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> SetLookAsync(
        AetherLove.Shared.Aetherling.AetherlingLookDto look, CancellationToken ct = default)
    {
        var dto = await _hub.SetAetherlingLookAsync(look, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> RevealScratchAsync(short slot, CancellationToken ct = default)
    {
        var dto = await _hub.RevealAetherlingCardAsync(slot, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherLove.Shared.Aetherling.AetherlingWheelDto?> GetWheelAsync(CancellationToken ct = default)
    {
        try
        {
            return await _hub.GetAetherlingWheelAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherling] Wheel fetch failed.");
            return null;
        }
    }

    public async Task<AetherLove.Shared.Aetherling.AetherlingWheelDto> SpinWheelAsync(CancellationToken ct = default)
    {
        var wheel = await _hub.SpinAetherlingWheelAsync(ct).ConfigureAwait(false);
        if (_snapshot is { } core)
        {
            _snapshot = core with { Wheel = new AetherLove.Shared.Aetherling.AetherlingWheelStateDto(true, wheel.NextSpinAtUtc) };
        }
        return wheel;
    }

    public Task<AetherLove.Shared.Aetherling.AetherlingWheelDto> RevealWheelAsync(CancellationToken ct = default) =>
        _hub.RevealAetherlingWheelAsync(ct);

    public async Task<AetherlingDto> CompleteOnboardingAsync(CancellationToken ct = default)
    {
        var dto = await _hub.CompleteAetherlingOnboardingAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherLove.Shared.Store.StoreInventoryItemDto[]?> GetOwnedItemsAsync(
        CancellationToken ct = default)
    {
        try
        {
            return await _hub.GetStoreInventoryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherling] Inventory fetch failed.");
            return null;
        }
    }

    /// <summary>What the player is playing, sampled on the framework tick. It cannot be read on demand:
    /// the one caller that matters is a hub round trip, and the object table answers null off the
    /// framework thread, which is how a warrior was handed an armorer's hammer. Empty covers the title
    /// screen, loading and anything else that has no truthful answer.</summary>
    public string CurrentJobAbbreviation => _job;

    public uint LastEmoteRowId => _emoteRow;

    private Dictionary<string, uint>? _emoteRowsByCommand;

    /// <summary>Resolves a text command to its Emote sheet row, from the game's own data: every command
    /// form a row publishes (command, short, alias, short alias) points back at it, so "/blowkiss" and
    /// "/kiss" answer the same row and nothing is transcribed by hand.</summary>
    public uint EmoteRowForCommand(string command)
    {
        if (_emoteRowsByCommand is null)
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var emote in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>())
                {
                    if (emote.TextCommand.ValueNullable is not { } tc)
                    {
                        continue;
                    }
                    foreach (var form in new[] { tc.Command, tc.ShortCommand, tc.Alias, tc.ShortAlias })
                    {
                        var text = form.ExtractText();
                        if (text.Length > 1 && text[0] == '/')
                        {
                            map.TryAdd(text, emote.RowId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Aetherling] Could not read the Emote sheet; watching is off this session.");
            }
            _emoteRowsByCommand = map;
        }
        return _emoteRowsByCommand.TryGetValue(command, out var row) ? row : 0u;
    }

    public long LastEmoteSequence => _emoteSeq;

    public unsafe byte CurrentWeatherId
    {
        get
        {
            var env = FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager.Instance();
            return env == null ? (byte)0 : env->ActiveWeather;
        }
    }

    public uint TerritoryId => Plugin.ClientState.TerritoryType;

    public async Task<AetherlingDto?> ReportEmoteSightingAsync(string emoteKey, CancellationToken ct = default)
    {
        try
        {
            var dto = await _hub.ReportAetherlingEmoteSightingAsync(emoteKey, ct).ConfigureAwait(false);
            _snapshotSeeded = true;
            _snapshot = dto;
            return dto;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug("[Aetherling] emote sighting report failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Samples the player's own emote state. The controller holds the row id for the emote's
    /// duration and reads 0 between them, so a repeat only re-fires after the previous run ended, which
    /// is also what makes a macro burst a single sighting.</summary>
    private unsafe void SampleEmote()
    {
        ushort id = 0;
        if (Plugin.ObjectTable.LocalPlayer is { } player)
        {
            var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
            if (chara != null)
            {
                id = chara->EmoteController.EmoteId;
            }
        }
        if (id != 0 && id != _emoteHeld)
        {
            _emoteRow = id;
            _emoteSeq++;
        }
        _emoteHeld = id;
    }

    /// <summary>The English sheet on purpose: the abbreviation is a lookup key rather than a label.</summary>
    private static string ReadJob()
    {
        try
        {
            var id = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0u;
            if (id == 0)
            {
                return "";
            }
            return Plugin.DataManager
                .GetExcelSheet<Lumina.Excel.Sheets.ClassJob>(Dalamud.Game.ClientLanguage.English)
                .GetRowOrDefault(id)?.Abbreviation.ExtractText().ToLowerInvariant() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public string DescribeError(Exception ex) => HubErrorText.Localize(ex);

    public bool BgmMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            ApplyAudioState();
        }
    }

    public void StartBgm(float speed) => PlayTrack(Path.Combine(AssetRoot, BgmFile), speed);

    public void StartGameBgm(string fileName, float speed = 1f) => PlayTrack(Path.Combine(MediaRoot, GameBgmFolder, fileName), speed);

    /// <summary>Re-rates the track that is already up, and restarts from the top for any other one: the
    /// ceremony and each minigame are different pieces of music, so "already playing" is only an answer
    /// when it is the same file.</summary>
    private void PlayTrack(string path, float speed)
    {
        if (_muted)
        {
            return;
        }
        if (_bgm.IsPlaying && string.Equals(_track, path, StringComparison.OrdinalIgnoreCase))
        {
            _bgm.SetSpeed(speed);
        }
        else
        {
            _track = path;
            _bgm.Play(path, speed);
        }
        _silenced = !ShouldBeAudible();
        ApplyAudioState();
    }

    /// <summary>Re-reads the played job, the game's sound settings and the window's focus. Cheap enough to
    /// run on a tick, and it has to be a tick: nothing raises an event when the player pulls a slider or
    /// alt-tabs away, and the object table only answers on this thread.</summary>
    private void OnTick(IFramework framework)
    {
        _audioAccum += framework.UpdateDelta.TotalSeconds;
        if (_audioAccum < AudioPollSeconds)
        {
            return;
        }
        _audioAccum = 0;
        _job = ReadJob();
        SampleEmote();

        if (!_bgm.IsPlaying)
        {
            return;
        }

        // The player flipping BGM back on mid-duck takes the mute over: release the claim, so the checks
        // below read their real answer again and the app never writes over their choice on the way out.
        if (_mutedGameBgm
            && GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var bgmNow) && !bgmNow)
        {
            _mutedGameBgm = false;
        }

        var silenced = !ShouldBeAudible();
        if (silenced == _silenced)
        {
            return;
        }
        _silenced = silenced;
        ApplyAudioState();
    }

    /// <summary>The loop is the phone's own music, so it answers to the game's sound options: master or BGM
    /// muted or at zero silences it, and it goes quiet with the game when the window loses focus unless the
    /// player asked for sound while inactive.</summary>
    private bool ShouldBeAudible()
    {
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSoundDisable, out var allOff) && allOff)
        {
            return false;
        }
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndMaster, out var masterMuted) && masterMuted)
        {
            return false;
        }
        if (GameVolume.TryGet(SystemConfigOption.SoundMaster, out var master) && master <= 0f)
        {
            return false;
        }

        // Our own duck sets IsSndBgm, so while the mute is ours the player's real answer is "not muted".
        if (!_mutedGameBgm
            && GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var bgmMuted) && bgmMuted)
        {
            return false;
        }
        if (GameVolume.TryGet(SystemConfigOption.SoundBgm, out var bgm) && bgm <= 0f)
        {
            return false;
        }

        if (WindowFocus.GameHasFocus())
        {
            return true;
        }
        return GameVolume.TryGetMuted(SystemConfigOption.IsSoundAlways, out var always) && always
            && GameVolume.TryGetMuted(SystemConfigOption.IsSoundBgmAlways, out var bgmAlways) && bgmAlways;
    }

    /// <summary>Silence is a ramp, never a stop: the track keeps running so coming back to the window picks it
    /// up where it would have been rather than restarting it. The game gets its own music back meanwhile,
    /// because there is nothing left to duck it for.</summary>
    private void ApplyAudioState()
    {
        var quiet = _muted || _silenced;
        _bgm.SetMuted(quiet);
        if (quiet || !_bgm.IsPlaying)
        {
            RestoreGameBgm();
            return;
        }
        DuckGameBgm();
    }

    public void StopBgm()
    {
        _track = null;
        _bgm.Stop();
        RestoreGameBgm();
    }

    /// <summary>The shell giving way. Rides the mute toggle with the loop, because a player who silenced the
    /// ceremony does not want the one loud moment of it either.</summary>
    public void PlayCrack()
    {
        if (_muted)
        {
            return;
        }
        _sfx.Play(Path.Combine(AssetRoot, CrackFile));
    }

    public bool SoundsMuted { get; set; }

    private float _voiceLevel = VoiceLevel;

    public float SoundVolume
    {
        get => _voiceLevel;
        set => _voiceLevel = Math.Clamp(value, 0f, 1f);
    }

    public void PlayChirp() => PlayVoice(_chirps);

    public void PlayResponse() => PlayVoice(_responses);

    /// <summary>One noise at a time, whatever the player does to the creature. The burst case is the whole
    /// point: ten pokes in two seconds are one sound, not ten voices stacked on the same output.</summary>
    private void PlayVoice(ShuffleBag bag)
    {
        if (SoundsMuted || _voice.Busy)
        {
            return;
        }
        var now = Environment.TickCount64 / 1000.0;
        if (now - _lastVoiceAt < MinVoiceGapSeconds)
        {
            return;
        }
        if (!_capabilities.Audio.EffectsAudible)
        {
            return;
        }
        if (bag.Next(Path.Combine(MediaRoot, VoiceFolder)) is not { } path)
        {
            return;
        }
        _lastVoiceAt = now;
        _voice.Play(path, _voiceLevel, VoicePitch());
    }

    /// <summary>The creature's voice drops as it grows: highest as a newborn, and the clips as recorded
    /// once it is an adult. The clips ARE the adult voice, so every young form is a shift up from them and
    /// nothing is ever pitched down, which would sound like a different animal rather than a younger one.
    /// Read off the same growth counters the worn form comes from, so the voice and the body cannot
    /// disagree about how old it is.</summary>
    private float VoicePitch()
    {
        if (Snapshot is not { } core || core.Adult is not null)
        {
            return 1f;
        }
        var perStage = Math.Max((short)1, core.Growth?.FeedsPerStage ?? 3);
        var fed = core.Growth?.GrowthFed ?? 0;
        if (fed >= perStage * 2)
        {
            return Hatchling3Pitch;
        }
        return fed >= perStage ? Hatchling2Pitch : HatchlingPitch;
    }

    /// <summary>Deals every clip once before any repeat, reshuffling when the bag runs dry and never
    /// starting a fresh round on the one just heard. The set is whatever files are actually on disk, read
    /// once: naming a count here would go stale the moment somebody adds a clip.</summary>
    private sealed class ShuffleBag(string prefix)
    {
        private readonly Random _rng = new();
        private readonly List<string> _bag = [];
        private string[]? _files;
        private string? _last;

        public string? Next(string folder)
        {
            if (_files is null)
            {
                try
                {
                    _files = Directory.Exists(folder)
                        ? Directory.GetFiles(folder, $"{prefix}*.ogg")
                        : [];
                    Array.Sort(_files, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Debug(ex, "[Aetherling] Could not list {Prefix} clips.", prefix);
                    _files = [];
                }
            }
            if (_files.Length == 0)
            {
                return null;
            }
            if (_bag.Count == 0)
            {
                _bag.AddRange(_files);
                for (var i = _bag.Count - 1; i > 0; i--)
                {
                    var j = _rng.Next(i + 1);
                    (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
                }
                if (_bag.Count > 1 && _bag[^1] == _last)
                {
                    (_bag[^1], _bag[0]) = (_bag[0], _bag[^1]);
                }
            }
            _last = _bag[^1];
            _bag.RemoveAt(_bag.Count - 1);
            return _last;
        }
    }

    /// <summary>Only ever claims a mute it made itself, so somebody who already plays with the music off is
    /// never unmuted behind their back when the app closes.</summary>
    private void DuckGameBgm()
    {
        if (_mutedGameBgm)
        {
            return;
        }
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var muted) && !muted)
        {
            GameVolume.SetMuted(SystemConfigOption.IsSndBgm, true);
            _mutedGameBgm = true;
        }
    }

    /// <summary>Gives the game's music back, keeping ownership of the mute until the flag reads unmuted:
    /// <see cref="GameVolume.SetMuted"/> swallows a failed write, and the flag is the only record that this
    /// mute is ours to undo.</summary>
    private void RestoreGameBgm()
    {
        if (!_mutedGameBgm)
        {
            return;
        }
        GameVolume.SetMuted(SystemConfigOption.IsSndBgm, false);
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var muted) && !muted)
        {
            _mutedGameBgm = false;
        }
    }

    private void OnLogout(int type, int code) => StopBgm();

    public void Dispose()
    {
        Plugin.Framework.Update -= OnTick;
        Plugin.ClientState.Logout -= OnLogout;
        _voice.Dispose();
        _sfx.Dispose();
        _bgm.Dispose();
        RestoreGameBgm();
    }
}
