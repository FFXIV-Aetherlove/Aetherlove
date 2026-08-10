using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Doom;

/// <summary>The arcade's Doom cabinet: the real thing, running from the bundled shareware IWAD, rendered
/// into the phone screen.
///
/// The first launch asks the only question that matters and takes "of course not" for an answer, then boots
/// E1M1 anyway. After that it behaves like every other cabinet in the Arcade folder.</summary>
public sealed class DoomApp : IAetherApp
{
    /// <summary>The bundled IWAD. Shareware Doom, renamed for the joke.</summary>
    public const string WadFileName = "willitplay.wad";

    private const string IntroSeenKey = "intro_seen";
    private const string RunSecondsForReward = "run_seconds";
    private const string MutedKey = "muted";

    /// <summary>A run has to be a real go at it before it earns sparks; opening and closing the app does not.</summary>
    private const double RewardedRunSeconds = 60.0;

    /// <summary>TEMPORARY: shows the "can it run Doom" card on every launch rather than only the first, so the
    /// intro can be reviewed without clearing app storage. Set to false to restore the real first-run behaviour.</summary>
    private const bool AlwaysShowIntro = true;

    private static readonly Vector4 TileTopColor = new(0.58f, 0.11f, 0.09f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.16f, 0.03f, 0.03f, 1f);

    private enum View
    {
        Intro,
        Splash,
        Playing,
        Unavailable,
    }

    private readonly Func<string> name;
    private readonly IAppStorage storage;
    private readonly IKeyboardInput keys;
    private readonly AetherLove.Os.IArcadeRewards rewards;

    private DoomRuntime? runtime;
    private View view = View.Intro;
    private string? failure;
    private bool stateLoaded;
    private double lastFrameTime;
    private double runSeconds;
    private bool paused;
    private bool muted;

    public DoomApp(Func<string> name, IAppCapabilities capabilities, AetherLove.Os.IArcadeRewards rewards)
    {
        this.name = name;
        this.storage = capabilities.Storage("doom");
        this.keys = capabilities.Keyboard;
        this.rewards = rewards;
    }

    public string Id => "doom";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.SkullCrossbones;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    /// <summary>The cabinet needs nothing but the WAD on disk, so it works signed out and offline.</summary>
    public bool RequiresConnection => false;

    /// <summary>Only while a run is live: drags on the view aim the marine, so the phone must not slide about
    /// underneath them. The menus stay draggable like any other screen.</summary>
    public bool LocksWindowDrag => this.view == View.Playing;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings =>
        Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        this.lastFrameTime = ImGui.GetTime();
        EnsureStateLoaded();

        if (this.runtime is { } engine)
        {
            engine.Muted = this.muted;
        }

        // The app is a singleton, so the loaded-once state survives closing it. Re-arm the card on every
        // entry, except mid-run, where coming back from the home screen should resume where you left off.
        if (AlwaysShowIntro && this.view != View.Playing)
        {
            this.view = View.Intro;
        }
    }

    /// <summary>Leaving the phone freezes the world rather than letting an imp finish the job unattended.</summary>
    public void OnBackground()
    {
        if (this.view == View.Playing)
        {
            this.paused = true;
        }

        // Drawing stops with the app, so the tick that would pause the voices never runs; the synth keeps
        // rendering on the playback thread. Muted is a volatile flag the mixer checks itself, so setting it
        // from here cannot race the playback thread the way a cross-thread Pause can.
        if (this.runtime is { } engine)
        {
            engine.Muted = true;
        }
    }

    public void OnIntent(OsIntent intent)
    {
    }

    public void Draw(OsAppContext ctx)
    {
        EnsureStateLoaded();
        DoomChrome.BeginFrame();

        var now = ImGui.GetTime();
        var delta = Math.Clamp(now - this.lastFrameTime, 0.0, 0.5);
        this.lastFrameTime = now;

        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        dl.AddRectFilled(winPos, winPos + winSize, ImGui.GetColorU32(new Vector4(0.04f, 0.03f, 0.03f, 1f)));

        switch (this.view)
        {
            case View.Playing:
                DrawPlaying(ctx, delta, winPos, winSize);
                break;
            case View.Splash:
                DrawSplash(ctx, winPos, winSize);
                break;
            case View.Unavailable:
                DrawUnavailable(ctx, winPos, winSize);
                break;
            default:
                DrawIntro(ctx, winPos, winSize);
                break;
        }
    }

    private void EnsureStateLoaded()
    {
        if (this.stateLoaded)
        {
            return;
        }
        this.stateLoaded = true;
        this.view = !AlwaysShowIntro && this.storage.Get<bool>(IntroSeenKey) ? View.Splash : View.Intro;
        this.muted = this.storage.Get<bool>(MutedKey);
    }

    private static string MediaDirectory =>
        Path.Combine(Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? string.Empty,
            "Media", "other");

    private void StartRun(OsAppContext ctx)
    {
        this.storage.Set(IntroSeenKey, true);
        this.runtime?.Dispose();
        this.runtime = DoomRuntime.TryCreate(MediaDirectory, this.storage.Directory, this.keys, out this.failure);
        if (this.runtime == null)
        {
            this.view = View.Unavailable;
            return;
        }

        this.runtime.Muted = this.muted;
        this.runSeconds = 0.0;
        this.paused = false;
        this.lastFrameTime = ImGui.GetTime();
        this.view = View.Playing;
    }

    /// <summary>Tears the cabinet down and banks the run. The server decides whether it pays.</summary>
    private void EndRun()
    {
        if (this.runSeconds >= RewardedRunSeconds)
        {
            this.rewards.NoteGameFinished();
            this.storage.Set(RunSecondsForReward, (int)this.runSeconds);
        }

        this.runtime?.Dispose();
        this.runtime = null;
        this.view = View.Splash;
    }

    private void DrawIntro(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();

        var question = ctx.Localize("os.doom_intro_question");
        using (ctx.TitleFont?.Push())
        {
            var size = ImGui.CalcTextSize(question);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - size.X) * 0.5f, (winSize.Y * 0.5f) - ctx.Px(60f)),
                ImGui.GetColorU32(new Vector4(0.93f, 0.90f, 0.86f, 1f)), question);
        }

        var buttonW = winSize.X * 0.72f;
        var buttonH = ctx.Px(44f);
        if (DoomChrome.Button(ctx.Localize("os.doom_intro_button"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y * 0.5f), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun(ctx);
        }

        if (DoomChrome.Key(FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }
    }

    private void DrawSplash(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();

        var pixel = MathF.Max(2f, winSize.X * 0.74f / RetroLcd.WordColumns("DOOM"));
        var wordY = winSize.Y * 0.20f;
        RetroLcd.DrawWordCentered(dl, "DOOM", winPos + new Vector2(0f, wordY), winSize.X, pixel,
            ImGui.GetColorU32(new Vector4(0.78f, 0.13f, 0.10f, 1f)), RetroLcd.GlyphHeight);

        var subtitle = ctx.Localize("os.doom_subtitle");
        var subSize = ImGui.CalcTextSize(subtitle);
        dl.AddText(winPos + new Vector2((winSize.X - subSize.X) * 0.5f,
                wordY + (RetroLcd.GlyphHeight * pixel) + ctx.Px(16f)),
            ImGui.GetColorU32(new Vector4(0.72f, 0.68f, 0.64f, 1f)), subtitle);

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(40f);
        var firstY = winSize.Y * 0.58f;
        if (DoomChrome.Button(ctx.Localize("os.doom_play"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, firstY), new Vector2(buttonW, buttonH),
            ctx.Px(4f), filled: true))
        {
            StartRun(ctx);
        }

        if (DoomChrome.Key(FontAwesomeIcon.ArrowLeft,
            winPos + new Vector2(ctx.Px(12f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }

        if (DoomChrome.Key(this.muted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp,
            winPos + new Vector2(winSize.X - ctx.Px(42f), ctx.Px(12f)), ctx.Px(30f)))
        {
            ToggleMute(this.runtime);
        }

        var soundLabel = ctx.Localize(this.muted ? "os.doom_sound_off" : "os.doom_sound_on");
        var soundSize = ImGui.CalcTextSize(soundLabel);
        dl.AddText(winPos + new Vector2(winSize.X - ctx.Px(12f) - soundSize.X, ctx.Px(46f)),
            ImGui.GetColorU32(new Vector4(0.55f, 0.52f, 0.50f, 1f)), soundLabel);

        var hint = ctx.Localize("os.doom_controls");
        DrawWrapped(dl, hint, winPos + new Vector2(ctx.Px(22f), winSize.Y - ctx.Px(86f)), winSize.X - ctx.Px(44f),
            ImGui.GetColorU32(new Vector4(0.55f, 0.52f, 0.50f, 1f)));
    }

    private void DrawPlaying(OsAppContext ctx, double delta, Vector2 winPos, Vector2 winSize)
    {
        if (this.runtime is not { } engine)
        {
            this.view = View.Splash;
            return;
        }

        // Attention has left the phone, or the chat box wants the keys: either way stop the world.
        if (RetroLcd.WindowBlurred() || this.keys.GameTextFocused)
        {
            this.paused = true;
        }

        // Take the keyboard FIRST, before any of our own controls are submitted. Reading a key is what parks
        // the focus-holding field that makes Dalamud withhold input from the game, so deferring it until the
        // engine ticks leaves a window in which FFXIV also sees the keystroke: Space both fires the shotgun
        // and jumps the character. The field lands in the window's top-left corner, which is why the HUD row
        // below is right-aligned instead of sitting on top of it.
        if (!this.paused)
        {
            this.keys.RequestExclusive();
            this.keys.IsDown(AppKey.Space);
        }

        var hudH = ctx.Px(28f);
        var footerH = ctx.Px(22f);
        var padH = winSize.Y * 0.24f;
        var stageTop = winPos + new Vector2(0f, hudH);
        var stageSize = new Vector2(winSize.X, winSize.Y - hudH - padH - footerH);

        // Everything the player can click is submitted BEFORE the engine ticks. Polling the keyboard parks an
        // invisible focus-holding field at the window's top-left corner, and first-submitted wins clicks in a
        // window, so ticking first would let that field swallow the whole HUD row sitting in that corner.
        if (!DrawHud(ctx, engine, winPos, winSize, hudH))
        {
            return;
        }
        if (!this.paused)
        {
            DrawTouchPad(ctx, engine, winPos, winSize, padH, footerH);
            DrawLookSurface(engine, stageTop, stageSize);
        }

        var active = !this.paused && !engine.Finished;
        if (active)
        {
            this.runSeconds += delta;
        }
        engine.Tick(delta, active);

        var dl = ImGui.GetWindowDrawList();
        engine.Draw(dl, stageTop, stageSize);
        DrawFooter(ctx, winPos, winSize, footerH);

        if (this.paused)
        {
            DrawPausedOverlay(ctx, stageTop, stageSize);
        }

        if (engine.Finished)
        {
            EndRun();
        }
    }

    /// <summary>Drag anywhere on the view to turn. Doom has no free look, so only the horizontal component
    /// matters; vertical drag is ignored rather than faked. The drag latches on the press ORIGIN, so it keeps
    /// turning even once the cursor leaves the view.</summary>
    private static void DrawLookSurface(DoomRuntime engine, Vector2 stageTop, Vector2 stageSize)
    {
        if (!DoomChrome.Held(stageTop, stageSize))
        {
            if (DoomChrome.Hovered(stageTop, stageSize))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
            }
            return;
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        var dx = ImGui.GetIO().MouseDelta.X;
        if (dx != 0f)
        {
            engine.AddMouseTurn(dx);
        }
    }

    private static void DrawFooter(OsAppContext ctx, Vector2 winPos, Vector2 winSize, float footerH)
    {
        var dl = ImGui.GetWindowDrawList();
        var line = ctx.Localize("os.doom_footer");
        var size = ImGui.CalcTextSize(line);
        dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f,
                winSize.Y - footerH + ((footerH - size.Y) * 0.5f)),
            ImGui.GetColorU32(new Vector4(0.42f, 0.39f, 0.38f, 1f)), line);
    }

    /// <summary>False once the player has left the cabinet, so the caller stops drawing a run that is over.</summary>
    private bool DrawHud(OsAppContext ctx, DoomRuntime engine, Vector2 winPos, Vector2 winSize, float hudH)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = ctx.Px(10f);
        var key = MathF.Min(ctx.Px(24f), hudH - ctx.Px(4f));
        var gap = ctx.Px(6f);
        var keyY = winPos.Y + ((hudH - key) * 0.5f);

        // Right-aligned, and in reverse order, so nothing clickable overlaps the keyboard-capture field that
        // the host parks in the window's top-left corner.
        var x = winPos.X + winSize.X - padX - key;

        if (DoomChrome.Key(this.muted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp,
            new Vector2(x, keyY), key))
        {
            ToggleMute(engine);
        }
        x -= key + gap;

        if (!this.paused && DoomChrome.Key(FontAwesomeIcon.Pause, new Vector2(x, keyY), key))
        {
            this.paused = true;
        }
        x -= key + gap;

        if (DoomChrome.Key(FontAwesomeIcon.ArrowLeft, new Vector2(x, keyY), key))
        {
            EndRun();
            return false;
        }

        var label = string.Format(ctx.Localize("os.doom_kills"), engine.Stats.Kills);
        dl.AddText(new Vector2(winPos.X + padX + key, winPos.Y + ((hudH - ImGui.GetTextLineHeight()) * 0.5f)),
            ImGui.GetColorU32(new Vector4(0.78f, 0.72f, 0.68f, 1f)), label);
        return true;
    }

    private void ToggleMute(DoomRuntime? engine)
    {
        this.muted = !this.muted;
        this.storage.Set(MutedKey, this.muted);
        if (engine != null)
        {
            engine.Muted = this.muted;
        }
    }

    private void DrawPausedOverlay(OsAppContext ctx, Vector2 stageTop, Vector2 stageSize)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(stageTop, stageTop + stageSize, ImGui.GetColorU32(new Vector4(0.04f, 0.03f, 0.03f, 0.84f)));

        var label = ctx.Localize("os.doom_paused");
        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(stageTop + new Vector2((stageSize.X - labelSize.X) * 0.5f, (stageSize.Y * 0.5f) - ctx.Px(46f)),
            ImGui.GetColorU32(new Vector4(0.93f, 0.90f, 0.86f, 1f)), label);

        var buttonW = stageSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        if (DoomChrome.Button(ctx.Localize("os.doom_resume"),
            stageTop + new Vector2((stageSize.X - buttonW) * 0.5f, stageSize.Y * 0.5f),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: true))
        {
            this.lastFrameTime = ImGui.GetTime();
            this.paused = false;
        }

        if (DoomChrome.Button(ctx.Localize("os.doom_quit"),
            stageTop + new Vector2((stageSize.X - buttonW) * 0.5f, (stageSize.Y * 0.5f) + buttonH + ctx.Px(10f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            EndRun();
        }
    }

    /// <summary>A minimal pad for anyone playing with the mouse. The keyboard is the real control scheme;
    /// this exists so a player who never reads the hint can still shoot something.</summary>
    private void DrawTouchPad(OsAppContext ctx, DoomRuntime engine, Vector2 winPos, Vector2 winSize, float padH,
        float footerH)
    {
        ref var touch = ref engine.Touch;
        touch = default;

        var key = MathF.Min(padH * 0.38f, winSize.X * 0.15f);
        var baseY = winPos.Y + winSize.Y - footerH - padH + (padH * 0.12f);
        var leftX = winPos.X + (winSize.X * 0.06f);

        touch.Forward = DoomChrome.HeldKey("W", new Vector2(leftX + (key * 1.1f), baseY), key);
        touch.TurnLeft = DoomChrome.HeldKey("<", new Vector2(leftX, baseY + (key * 1.1f)), key);
        touch.Backward = DoomChrome.HeldKey("S",
            new Vector2(leftX + (key * 1.1f), baseY + (key * 1.1f)), key);
        touch.TurnRight = DoomChrome.HeldKey(">",
            new Vector2(leftX + (key * 2.2f), baseY + (key * 1.1f)), key);

        var rightX = winPos.X + winSize.X - (winSize.X * 0.06f) - key;
        touch.Fire = DoomChrome.HeldKey("FIRE",
            new Vector2(rightX - (key * 1.2f), baseY + (key * 0.55f)), key);
        touch.Use = DoomChrome.HeldKey("USE", new Vector2(rightX, baseY + (key * 0.55f)), key);
    }

    private void DrawUnavailable(OsAppContext ctx, Vector2 winPos, Vector2 winSize)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.doom_missing_title");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.24f),
                ImGui.GetColorU32(new Vector4(0.93f, 0.90f, 0.86f, 1f)), title);
        }

        var body = ctx.Localize(this.failure == "engine_failed" ? "os.doom_missing_engine" : "os.doom_missing_wad");
        DrawWrapped(dl, body, winPos + new Vector2(ctx.Px(24f), winSize.Y * 0.38f), winSize.X - ctx.Px(48f),
            ImGui.GetColorU32(new Vector4(0.72f, 0.68f, 0.64f, 1f)));

        var buttonW = winSize.X * 0.62f;
        var buttonH = ctx.Px(38f);
        if (DoomChrome.Button(ctx.Localize("os.doom_back"),
            winPos + new Vector2((winSize.X - buttonW) * 0.5f, winSize.Y - buttonH - ctx.Px(28f)),
            new Vector2(buttonW, buttonH), ctx.Px(4f), filled: false))
        {
            ctx.Shell.GoHomeToFolder(IOsShell.ArcadeFolderId);
        }
    }

    private static void DrawWrapped(ImDrawListPtr dl, string text, Vector2 topLeft, float width, uint color) =>
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), topLeft, color, text, width);
}
