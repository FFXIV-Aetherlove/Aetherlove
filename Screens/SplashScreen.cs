using System;
using System.IO;
using System.Media;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Splash screen with a lub-dub heartbeat animation. Tap to skip.</summary>
public sealed class SplashScreen : IDisposable
{
    private const int HeartbeatCount = 2;

    private static readonly float[] BeatTimes = { 0.6f, 2.6f };

    private const float DubOffset = 0.13f;
    private const float DubAmplitude = 0.65f;

    private const float LogoNaturalW = 300f;
    private const float LogoNaturalH = 403f;

    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private ISharedImmediateTexture? _logoTexture;
    private AppTheme _loadedTheme;
    private float _elapsed;

    private SoundPlayer? _soundPlayer;
    private volatile bool _playbackComplete;
    private bool _audioEnabled;

    public SplashScreen(ScreenRouter router, SessionBootstrapper bootstrap)
    {
        _router = router;
        _bootstrap = bootstrap;
    }

    private static string LogoFileName => ThemeService.CurrentTheme switch
    {
        AppTheme.AllaganPassion => "logo_allagan.png",
        AppTheme.VanillaSunrise => "logo_yellow.png",
        _ => "logo_purple.png",
    };


    public void OnShow()
    {
        _elapsed = 0f;
        _playbackComplete = false;
        _audioEnabled = AccessibilityService.SoundEffectsEnabled
                            && !Plugin.Configuration.DisableStartupHeartbeatSound;

        _ = _bootstrap.RunAsync();

        var currentTheme = ThemeService.CurrentTheme;
        if (_logoTexture == null || _loadedTheme != currentTheme)
        {
            _loadedTheme = currentTheme;
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
            var logoPath = Path.Combine(dir, "Media", LogoFileName);
            _logoTexture = File.Exists(logoPath)
                ? Plugin.TextureProvider.GetFromFile(logoPath)
                : null;

            if (_logoTexture == null)
            {
                Plugin.Log.Warning($"[SplashScreen] Logo not found: {logoPath}");
            }
        }

        PlayAudio();
    }

    private void PlayAudio()
    {
        StopAudio();

        if (!_audioEnabled)
        {
            return;
        }

        var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
        var audioPath = Path.Combine(dir, "Media", "heartbeat.wav");

        if (!File.Exists(audioPath))
        {
            Plugin.Log.Warning("[SplashScreen] heartbeat.wav not found, skipping audio.");
            _playbackComplete = true;
            return;
        }

        try
        {
            _soundPlayer = new SoundPlayer(audioPath);
            _soundPlayer.Load();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[SplashScreen] Failed to create/load SoundPlayer.");
            _playbackComplete = true;
            return;
        }

        var player = _soundPlayer;
        _ = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < HeartbeatCount; i++)
                {
                    player.PlaySync();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[SplashScreen] Playback task threw an exception.");
            }
            finally
            {
                _playbackComplete = true;
            }
        });
    }

    private void StopAudio()
    {
        try { _soundPlayer?.Stop(); } catch { /* ignore */ }
        _soundPlayer?.Dispose();
        _soundPlayer = null;
    }

    private void Advance()
    {
        StopAudio();

        if (_bootstrap.LastResult == SessionBootstrapResult.OutdatedClient)
        {
            _router.Navigate(Screen.Outdated);
            return;
        }

        if (_bootstrap.LastResult == SessionBootstrapResult.Banned)
        {
            _router.Navigate(Screen.Banned);
            return;
        }

        if (_bootstrap.HasUnseenWarnings &&
            (_bootstrap.LastResult == SessionBootstrapResult.SignedInActive ||
             _bootstrap.LastResult == SessionBootstrapResult.SignedInOnboarding))
        {
            _router.Navigate(Screen.WarningsAcknowledge);
            return;
        }

        if (_bootstrap.NeedsPassphraseUnlock)
        {
            _router.Navigate(Screen.PassphraseUnlock);
            return;
        }

        var target = _bootstrap.LastResult switch
        {
            SessionBootstrapResult.SignedInActive => Screen.Deck,
            SessionBootstrapResult.SignedInOnboarding => Screen.Onboarding,
            _ => Screen.Onboarding,
        };
        _router.Navigate(target);
    }

    public void Dispose() => StopAudio();

    // Asymmetric envelope: fast Gaussian rise, slower exponential decay.
    private static float AsymPeak(float dt)
    {
        if (dt < 0f)
        {
            return MathF.Exp(-80f * dt * dt);
        }
        else
        {
            return MathF.Exp(-6f * dt);
        }
    }

    private float HeartbeatPulse()
    {
        var pulse = 0f;
        foreach (var beatTime in BeatTimes)
        {
            var lub = AsymPeak(_elapsed - beatTime);
            var dub = AsymPeak(_elapsed - beatTime - DubOffset) * DubAmplitude;
            pulse = MathF.Max(pulse, MathF.Max(lub, dub));
        }
        return pulse;
    }


    public void Draw()
    {
        _elapsed += ImGui.GetIO().DeltaTime;

        var avail = ImGui.GetContentRegionAvail();

        var beat = AccessibilityService.ReduceMotion ? 0f : HeartbeatPulse();

        var tex = _logoTexture?.GetWrapOrDefault();
        if (tex != null)
        {
            var fitScale = Math.Min(1f, Math.Min(avail.X * 0.95f / LogoNaturalW,
                                                 avail.Y * 0.72f / LogoNaturalH));
            var baseW = LogoNaturalW * fitScale;
            var baseH = LogoNaturalH * fitScale;

            var swell = 1.0f + 0.12f * beat;
            var imgW = baseW * swell;
            var imgH = baseH * swell;

            var imgX = (avail.X - imgW) * 0.5f;
            var imgY = (avail.Y - imgH) * 0.5f - avail.Y * 0.04f;

            var alpha = AccessibilityService.ReduceMotion ? 0.90f : 0.60f + 0.40f * beat;

            ImGui.SetCursorPos(new Vector2(imgX, imgY));
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
            ImGui.Image(tex.Handle, new Vector2(imgW, imgH));
            ImGui.PopStyleVar();
        }
        else
        {
            const string Title = "AetherLove";
            var tsz = ImGui.CalcTextSize(Title);
            var alpha = 0.60f + 0.40f * beat;
            ImGui.SetCursorPos(new Vector2((avail.X - tsz.X) * 0.5f, (avail.Y - tsz.Y) * 0.5f));
            ImGui.TextColored(new Vector4(alpha, alpha * 0.7f, alpha * 0.85f, alpha), Title);
        }

        var fallbackDue = !_audioEnabled && _elapsed >= BeatTimes[^1] + 0.5f;
        var heartbeatDone = _playbackComplete || fallbackDue;
        var bootstrapDone = _bootstrap.LastResult != SessionBootstrapResult.Pending;

        // 8s cap so a hung server doesn't lock the splash forever.
        var bootstrapWaitTimedOut = _elapsed >= 8f;
        if (heartbeatDone && (bootstrapDone || bootstrapWaitTimedOut))
        {
            Advance();
            return;
        }

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("##splashTap", avail);
        // Only allow skip once bootstrap has resolved.
        if (ImGui.IsItemClicked() && bootstrapDone)
        {
            Advance();
        }
    }
}
