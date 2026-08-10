using System;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherLove;
using AetherOS.Apps.Doom.Backend;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using ManagedDoom;
using ManagedDoom.Audio;

namespace AetherOS.Apps.Doom;

/// <summary>Owns one running Doom and drives it from the phone's draw loop.
///
/// The engine expects to own the process: upstream spins its own window, its own 35 Hz loop and its own
/// sleep. Here it is a guest. The game advances on an accumulator so it keeps true Doom time regardless of
/// the frame rate the game client happens to be running at, and rendering is decoupled from that so a
/// 144 Hz client interpolates rather than re-simulating.</summary>
internal sealed class DoomRuntime : IDisposable
{
    /// <summary>Doom's tic rate. Not configurable: the whole simulation is defined in terms of it.</summary>
    private const double TicSeconds = 1.0 / 35.0;

    /// <summary>A long stall (alt-tabbing, a zone load) must not be repaid as a burst of tics, or the player
    /// returns to find themselves dead somewhere else.</summary>
    private const double MaxCatchUpSeconds = 0.25;

    private readonly GameContent content;
    private readonly ImGuiDoomVideo video;
    private readonly DoomAudioOutput? audio;
    private readonly NAudioDoomSound? sound;
    private readonly MeltySynthDoomMusic? music;
    private readonly PhoneUserInput input;
    private readonly global::ManagedDoom.Doom doom;
    private readonly Config config;
    private readonly string configPath;

    private double accumulator;
    private bool audioRunning = true;

    private DoomRuntime(string wadPath, string? soundFontPath, string dataDirectory, IKeyboardInput keys)
    {
        ConfigUtilities.DataDirectoryOverride = dataDirectory;
        this.configPath = Path.Combine(dataDirectory, "managed-doom.cfg");

        this.config = File.Exists(this.configPath) ? new Config(this.configPath) : new Config();
        // 320x200 on purpose: it is the resolution the joke is about, it costs a quarter of the software
        // renderer's work, and the phone screen is nowhere near big enough to show the difference.
        this.config.video_highresolution = false;
        this.config.video_fpsscale = 1;
        this.config.game_alwaysrun = true;

        // Warp straight into E1M1. Without it the engine starts on the title screen, whose idle behaviour is
        // to play its built-in attract DEMOS: the game appears to play itself, which is exactly what it looks
        // like when the player expected "Of course not" to hand them a shotgun.
        var args = new CommandLineArgs(["-iwad", wadPath, "-warp", "1", "1", "-skill", "3"]);
        this.content = new GameContent(args);

        this.video = new ImGuiDoomVideo(this.config, this.content);
        this.input = new PhoneUserInput(this.config, keys);

        ISound engineSound = new NullSound();
        IMusic engineMusic = new NullMusic();
        try
        {
            this.audio = new DoomAudioOutput(9);
            this.sound = new NAudioDoomSound(this.config, this.content, this.audio);
            engineSound = this.sound;

            if (soundFontPath != null)
            {
                this.music = new MeltySynthDoomMusic(this.config, this.content, soundFontPath);
                this.audio.Music = this.music;
                engineMusic = this.music;
            }
        }
        catch (Exception ex)
        {
            // A missing or busy audio device must not cost the player the game.
            UiHost.Log.Warning(ex, "[Doom] Audio unavailable; running silent.");
        }

        this.doom = new global::ManagedDoom.Doom(args, this.config, this.content, this.video, engineSound,
            engineMusic, this.input);
    }

    public bool Finished { get; private set; }

    /// <summary>True once music is actually playing, so the intro card can say so honestly.</summary>
    public bool HasMusic => this.music != null;

    /// <summary>Silences the cabinet. Survives a restart via the app's own stored preference.</summary>
    public bool Muted
    {
        get => this.audio?.Muted ?? true;
        set
        {
            if (this.audio is { } device)
            {
                device.Muted = value;
            }
        }
    }

    /// <summary>Builds a runtime, or returns null with a reason when the cabinet cannot start.</summary>
    public static DoomRuntime? TryCreate(string mediaDirectory, string dataDirectory, IKeyboardInput keys,
        out string? failure)
    {
        var wad = Path.Combine(mediaDirectory, DoomApp.WadFileName);
        if (!File.Exists(wad))
        {
            failure = "wad_missing";
            return null;
        }

        try
        {
            Directory.CreateDirectory(dataDirectory);
            failure = null;
            return new DoomRuntime(wad, FindSoundFont(mediaDirectory), dataDirectory, keys);
        }
        catch (Exception ex)
        {
            UiHost.Log.Error(ex, "[Doom] Failed to start the engine.");
            failure = "engine_failed";
            return null;
        }
    }

    /// <summary>Any SoundFont dropped next to the WAD becomes the music voice. Absent, the game still runs;
    /// it just runs without a soundtrack.</summary>
    private static string? FindSoundFont(string mediaDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(mediaDirectory, "*.sf2").OrderBy(f => f).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Advances the simulation. <paramref name="active"/> is false while the surface is paused or
    /// unfocused, which stops the clock instead of letting the world run on unattended.</summary>
    public void Tick(double delta, bool active)
    {
        if (this.Finished)
        {
            return;
        }

        // Only on the transition. Pausing and resuming every frame takes nine voice locks per frame each way,
        // which contends with NAudio's callback thread and starves the output into repeating its last buffer.
        if (active != this.audioRunning)
        {
            this.audioRunning = active;
            if (active)
            {
                this.sound?.Resume();
            }
            else
            {
                this.sound?.Pause();
            }
        }

        if (!active)
        {
            this.input.ReleaseAll(this.doom);
            this.accumulator = 0.0;
            return;
        }

        this.input.PumpEvents(this.doom);

        this.accumulator = Math.Min(this.accumulator + delta, MaxCatchUpSeconds);
        while (this.accumulator >= TicSeconds)
        {
            this.accumulator -= TicSeconds;
            try
            {
                if (this.doom.Update() == UpdateResult.Completed)
                {
                    this.Finished = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Error(ex, "[Doom] Engine tic failed.");
                this.Finished = true;
                return;
            }
        }
    }

    /// <summary>Renders and draws the current frame into <paramref name="rect"/>, letterboxed to Doom's
    /// aspect so the view is never stretched.</summary>
    public void Draw(ImDrawListPtr dl, Vector2 topLeft, Vector2 available)
    {
        try
        {
            var frac = Fixed.FromDouble(Math.Clamp(this.accumulator / TicSeconds, 0.0, 1.0));
            this.video.Render(this.doom, frac);
        }
        catch (Exception ex)
        {
            UiHost.Log.Error(ex, "[Doom] Frame render failed.");
            this.Finished = true;
            return;
        }

        var aspect = this.video.AspectRatio;
        var size = new Vector2(available.X, available.X / aspect);
        if (size.Y > available.Y)
        {
            size = new Vector2(available.Y * aspect, available.Y);
        }
        var origin = topLeft + ((available - size) * 0.5f);
        this.video.Present(dl, origin, size);
    }

    /// <summary>Lets the on-screen pad contribute to the next tic command.</summary>
    public ref PhoneUserInput.TouchState Touch => ref this.input.Touch;

    /// <summary>Feeds a horizontal drag, in screen pixels, into the player's turn.</summary>
    public void AddMouseTurn(float deltaX) => this.input.AddMouseTurn(deltaX);

    /// <summary>The player's tally for the arcade leaderboard, or zeroes outside a live game.</summary>
    public (int Kills, int Items, int Secrets) Stats
    {
        get
        {
            try
            {
                var player = this.doom.Game?.World?.ConsolePlayer;
                return player == null
                    ? (0, 0, 0)
                    : (player.KillCount, player.ItemCount, player.SecretCount);
            }
            catch
            {
                return (0, 0, 0);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            this.config.Save(this.configPath);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Doom] Config save failed.");
        }

        this.music?.Dispose();
        this.sound?.Dispose();
        this.audio?.Dispose();
        this.video.Dispose();
        this.content.Dispose();
    }
}
