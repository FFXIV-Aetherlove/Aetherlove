using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Signal;
using AetherLove.Shared;
using AetherLove.Shared.Diagnostics;
using AetherLove.UI;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Standalone diagnostic window opened by "/aetherlove debug". English-only on purpose: it's a
/// support tool whose copyable text is read by whoever is helping.</summary>
public sealed class DebugWindow : Window
{
    private readonly AetherSignalService _signal;
    private readonly AetherHubContext _hub;

    private volatile bool _loading;
    private volatile string? _serverError;
    private volatile DebugInfoDto? _server;
    private DateTimeOffset _serverFetchedLocalUtc;

    private ISharedImmediateTexture? _jpegTex;
    private ISharedImmediateTexture? _webpTex;
    private bool _samplesStored;

    private DateTime _copiedAt = DateTime.MinValue;

    private NotificationSound _testSound;
    private string? _soundTestResult;
    private bool _soundTestOk;

    private static readonly Vector4 HeadingCol = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 OkCol = new(0.40f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 WarnCol = new(0.95f, 0.65f, 0.25f, 1f);
    private static readonly Vector4 ErrCol = new(0.95f, 0.45f, 0.45f, 1f);

    public DebugWindow(AetherSignalService signal, AetherHubContext hub)
        : base("AetherLove Debug##aetherloveDebug")
    {
        _signal = signal;
        _hub = hub;
        Size = new Vector2(580, 660);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 320),
            MaximumSize = new Vector2(4000, 4000),
        };
    }

    public override void OnOpen()
    {
        _testSound = Plugin.Configuration.NotificationSoundChoice;
        _soundTestResult = null;
        LoadServerInfo();
    }

    private void LoadServerInfo()
    {
        _loading = true;
        _serverError = null;
        _server = null;
        _jpegTex = null;
        _webpTex = null;
        _samplesStored = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetDebugInfoAsync().ConfigureAwait(false);
                _serverFetchedLocalUtc = DateTimeOffset.UtcNow;
                _server = dto;
            }
            catch (Exception ex)
            {
                _serverError = ex.Message;
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        if (ImGui.Button("Refresh"))
        {
            LoadServerInfo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy info for support"))
        {
            ImGui.SetClipboardText(BuildSupportText());
            _copiedAt = DateTime.UtcNow;
        }
        ImGui.SameLine();
        if (ImGui.Button("Get support (Discord)"))
        {
            OpenDiscord();
        }
        if ((DateTime.UtcNow - _copiedAt).TotalSeconds < 3)
        {
            ImGui.SameLine();
            ImGui.TextColored(OkCol, "Copied!");
        }

        if (_serverError is not null)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(ErrCol, $"Server diagnostics unavailable: {_serverError}");
            ImGui.TextColored(WarnCol, "The local info below is still valid; server fields need you signed in and connected.");
            ImGui.PopTextWrapPos();
        }
        else if (_loading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("loading server info...");
        }

        if (!ImGui.BeginTabBar("##aetherDebugTabs"))
        {
            return;
        }
        if (ImGui.BeginTabItem("Diagnostics"))
        {
            DrawDiagnosticsTab(scale);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Fonts"))
        {
            DrawFontsTab();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawDiagnosticsTab(float scale)
    {
        Section("Connection");
        Row("Status", _signal.State.ToString());
        Row("Transport", Server(_server?.Transport));

        Section("Account");
        Row("Account ID (partial)", Server(_server?.PartialAccountId));
        Row("IP address", Server(RedactIp(_server?.IpAddress)));

        Section("Versions");
        Row("Plugin", SystemInfo.PluginVersion());
        Row("Dalamud (assembly)", SystemInfo.DalamudVersion());
        Row("API protocol", ApiVersion.Current.ToString());
        Row(".NET runtime", SystemInfo.Runtime());

        Section("System");
        Row("OS", SystemInfo.Os());
        Row("Running under Wine", Dalamud.Utility.Util.IsWine() ? "Yes" : "No");
        Row("CPU", SystemInfo.Cpu());
        Row("Memory", SystemInfo.Memory());

        Section("Time");
        Row("Local time", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Row("Time zone", $"{TimeZoneInfo.Local.Id} (UTC{Offset(TimeZoneInfo.Local.BaseUtcOffset)})");
        Row("UTC time", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        Row("Server UTC", _server is null
            ? Server(null)
            : _server.ServerTimeUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        Row("Clock skew", Skew());

        DrawSoundSection();

        Section("Image rendering test");
        Row("WebP decode (auto-detected)", WebpProbeText());
        Row("Photos requested as", _signal.AcceptsWebp() ? "WebP" : "JPEG");
        var forceJpeg = Plugin.Configuration.ForceJpegImages;
        if (ImGui.Checkbox("Force JPEG photos (override auto-detect)", ref forceJpeg))
        {
            Plugin.Configuration.ForceJpegImages = forceJpeg;
            Plugin.Configuration.Save();
        }
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled("Takes effect on the next reconnect (reopen AetherLove). Both formats are sent below regardless of OS; compare which one renders.");
        ImGui.PopTextWrapPos();
        DrawSamples(scale);
    }

    /// <summary>Every pickable family rendered live with its resolved file state, so a family that silently
    /// falls back to the default is visible at a glance.</summary>
    private void DrawFontsTab()
    {
        Section("Active font");
        Row("Configured", Plugin.Configuration.Os.FontFamily);
        Row("Resolved", UiFonts.ActiveFamily.Id);
        Row("UI scale", UiScale.S.ToString("0.###"));
        Row("Handles ready", UiFonts.Ready ? "Yes" : "No");
        Row("Font directory", UiFonts.DiagnosticsFontDir);
        if (ImGui.Button("Rebuild fonts now"))
        {
            UiFonts.Rebuild();
        }

        Section("Families");
        foreach (var family in UiFonts.Families)
        {
            var path = UiFonts.DiagnosticsResolve(family);
            var fileNote = family.File is null
                ? "(built-in)"
                : path is null
                    ? $"FILE MISSING: {family.File}"
                    : $"{family.File} ({new FileInfo(path).Length / 1024} KB)";
            var active = family.Id == UiFonts.ActiveFamily.Id ? "  << ACTIVE" : "";
            var preview = UiFonts.Preview(family);
            ImGui.TextColored(path is null && family.File is not null ? ErrCol : WarnCol,
                $"{family.Id}  trim {family.Trim:0.##}  {fileNote}  previewReady={preview is { Available: true }}{active}");
            using (preview?.Push())
            {
                ImGui.TextUnformatted(FontSample);
            }
            ImGui.Spacing();
        }

        Section("Live phone handles");
        using (UiFonts.Body?.Push())
        {
            ImGui.TextUnformatted($"Body: {FontSample}");
        }
        using (UiFonts.H3?.Push())
        {
            ImGui.TextUnformatted("H3: The quick brown fox 0123456789");
        }
        using (UiFonts.H1?.Push())
        {
            ImGui.TextUnformatted("H1: Quick fox 042");
        }
    }

    private const string FontSample =
        "The quick brown fox jumps over the lazy dog 0123456789 Съешь ещё этих мягких булок";

    private void DrawSamples(float scale)
    {
        if (_server is not null && !_samplesStored)
        {
            var dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "DebugCache");
            _jpegTex = _server.SampleJpeg.Length > 0 ? AvatarDiskCache.Store(dir, "debug-jpeg", _server.SampleJpeg) : null;
            _webpTex = _server.SampleWebp.Length > 0 ? AvatarDiskCache.Store(dir, "debug-webp", _server.SampleWebp) : null;
            _samplesStored = true;
        }

        DrawSample("JPEG", _server?.SampleJpeg, _jpegTex, scale);
        DrawSample("WebP", _server?.SampleWebp, _webpTex, scale);
    }

    private void DrawSample(string label, byte[]? bytes, ISharedImmediateTexture? tex, float scale)
    {
        ImGui.Spacing();
        var wrap = tex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            ImGui.Image(wrap.Handle, new Vector2(72f, 72f) * scale);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted($"{label} sample ({(bytes is { Length: > 0 } b ? FormatName(b) : "?")})");
            ImGui.TextColored(OkCol, $"Decoded and rendered OK ({wrap.Width}x{wrap.Height}).");
            ImGui.EndGroup();
        }
        else if (_loading)
        {
            ImGui.TextDisabled($"{label} sample: loading...");
        }
        else if (_server is null || bytes is not { Length: > 0 })
        {
            ImGui.TextDisabled($"{label} sample: unavailable (not connected).");
        }
        else
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(ErrCol, $"{label} sample ({FormatName(bytes)}): failed to decode; the gray-block symptom for this format.");
            ImGui.PopTextWrapPos();
        }
    }

    private string RenderStatus(byte[]? bytes, ISharedImmediateTexture? tex)
    {
        var wrap = tex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            return $"Rendered OK ({wrap.Width}x{wrap.Height})";
        }
        if (_loading)
        {
            return "loading";
        }
        if (_server is null || bytes is not { Length: > 0 })
        {
            return "unavailable (not connected)";
        }
        return "FAILED to decode (gray-block symptom)";
    }

    private static string FormatName(byte[] bytes) => ImageFormat.ExtensionFor(bytes) switch
    {
        ".webp" => "WebP",
        ".jpg" => "JPEG",
        ".png" => "PNG",
        _ => "unknown",
    };

    private static string WebpProbeText() => Plugin.Configuration.WebpSupported switch
    {
        true => "Supported",
        false => "Not supported; served JPEG",
        null => "Not yet probed; defaulting to JPEG",
    };

    private string Server(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }
        return _loading ? "(loading...)" : "(unavailable)";
    }

    /// <summary>Masks the host part of the address: IPv4 keeps the first two octets, IPv6 the first two
    /// groups. The result still distinguishes networks for support without exposing the full address.</summary>
    private static string? RedactIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            return ip;
        }
        if (ip.Contains(':'))
        {
            var groups = ip.Split(':');
            return groups.Length <= 2 ? ip : $"{groups[0]}:{groups[1]}:****";
        }
        var octets = ip.Split('.');
        return octets.Length == 4 ? $"{octets[0]}.{octets[1]}.***.***" : ip;
    }

    /// <summary>Masks the account-name segment of a filesystem path (Windows Users\name, Unix/Wine
    /// home/name), keeping the first two characters so support can still tell paths apart.</summary>
    private static string RedactPath(string path)
    {
        return System.Text.RegularExpressions.Regex.Replace(path,
            @"(?i)([\\/](?:users|home)[\\/])([^\\/]+)",
            m => m.Groups[1].Value + MaskName(m.Groups[2].Value));

        static string MaskName(string name) =>
            name.Length <= 2 ? "***" : name[..2] + "***";
    }

    private string Skew()
    {
        var server = _server;
        if (server is null)
        {
            return Server(null);
        }
        var seconds = (_serverFetchedLocalUtc - server.ServerTimeUtc).TotalSeconds;
        var text = $"{seconds:+0.#;-0.#;0}s vs server (includes network round-trip)";
        return Math.Abs(seconds) > 30 ? text + "  <- your clock looks wrong" : text;
    }

    private static string Offset(TimeSpan o) =>
        (o < TimeSpan.Zero ? "-" : "+") + o.Duration().ToString("hh\\:mm");

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(HeadingCol, title);
        ImGui.Separator();
    }

    private static void Row(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(170f * ImGuiHelpers.GlobalScale);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(value);
        ImGui.PopTextWrapPos();
    }

    private static void RowState(string label, bool ok, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(170f * ImGuiHelpers.GlobalScale);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(ok ? OkCol : ErrCol, value);
        ImGui.PopTextWrapPos();
    }

    /// <summary>Every reason a notification sound would be silenced right now, in plain language; empty when a
    /// sound would actually play. Drives the verdict banner and the support text.</summary>
    private static List<string> SoundBlockers()
    {
        var cfg = Plugin.Configuration;
        var blockers = new List<string>();
        if (!cfg.EnableNotifications)
        {
            blockers.Add("Notifications are turned OFF (master switch in Settings > Notifications).");
        }
        if (!cfg.EnableNotificationSounds)
        {
            blockers.Add("\"Play a sound\" is turned OFF in Settings > Notifications.");
        }
        if (cfg.HideNotificationsDuringCombat && Plugin.Condition[ConditionFlag.InCombat])
        {
            blockers.Add("You are in combat and \"Mute in combat\" is on.");
        }
        if (Plugin.Configuration.OsSettings.NotificationVolume <= 0f)
        {
            blockers.Add("Notification volume is set to 0% in Settings > Audio settings.");
        }
        if (!File.Exists(NotificationSoundPlayer.ResolvePath(cfg.NotificationSoundChoice)))
        {
            blockers.Add("The selected sound file is missing (reinstall the plugin).");
        }
        return blockers;
    }

    private void DrawSoundSection()
    {
        Section("Notification sound");
        var cfg = Plugin.Configuration;

        var blockers = SoundBlockers();
        if (blockers.Count == 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(OkCol, "Notification sounds should play. Use Test play below to confirm you can hear them.");
            ImGui.PopTextWrapPos();
        }
        else
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(ErrCol, "Notification sounds will NOT play right now:");
            foreach (var reason in blockers)
            {
                ImGui.TextColored(ErrCol, "   - " + reason);
            }
            ImGui.PopTextWrapPos();
        }
        ImGui.Spacing();

        RowState("Notifications on", cfg.EnableNotifications,
            cfg.EnableNotifications ? "Yes" : "No (master switch off in Settings > Notifications)");
        RowState("Play a sound", cfg.EnableNotificationSounds,
            cfg.EnableNotificationSounds ? "Yes" : "No -- turn on \"Play a sound\" in Settings > Notifications");
        Row("Selected sound", cfg.NotificationSoundChoice.DisplayName());
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        Row("Mute in combat", cfg.HideNotificationsDuringCombat
            ? (inCombat ? "On -- and you are IN COMBAT now, so sounds are suppressed" : "On (not in combat)")
            : "Off");

        Row("Output device", NotificationSoundPlayer.DescribeOutput());
        Row("Volume", $"{Plugin.Configuration.OsSettings.NotificationVolume:P0}");
        Row("Sound folder", RedactPath(NotificationSoundPlayer.SoundDirectory));
        var selPath = NotificationSoundPlayer.ResolvePath(cfg.NotificationSoundChoice);
        var exists = File.Exists(selPath);
        RowState("Selected file present", exists, exists ? "Yes" : "MISSING (reinstall the plugin)");
        if (exists)
        {
            Row("Selected file format", DescribeWav(selPath));
        }

        if (Dalamud.Utility.Util.IsWine())
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(WarnCol, "Running under Wine/Proton: the Windows sound backend (waveOut) these chimes use can stay silent depending on your audio setup, even when the game's own sound works.");
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Test sound");
        ImGui.SameLine(170f * ImGuiHelpers.GlobalScale);
        ImGui.SetNextItemWidth(190f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##dbgTestSound", _testSound.DisplayName()))
        {
            foreach (var s in Enum.GetValues<NotificationSound>())
            {
                if (ImGui.Selectable(s.DisplayName(), s == _testSound))
                {
                    _testSound = s;
                }
            }
            ImGui.EndCombo();
        }

        if (ImGui.Button("Test play"))
        {
            SetSoundTestResult(NotificationSoundPlayer.TryPlay(_testSound)
                ?? $"Playing on \"{NotificationSoundPlayer.DescribeOutput()}\". If you hear nothing, pick a different output device in Settings > Audio settings.");
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop##sound"))
        {
            NotificationSoundPlayer.Stop();
        }

        if (_soundTestResult is not null)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(_soundTestOk ? OkCol : WarnCol, _soundTestResult);
            ImGui.PopTextWrapPos();
        }
    }

    private void SetSoundTestResult(string result)
    {
        _soundTestResult = result;
        _soundTestOk = result.StartsWith("Playing", StringComparison.Ordinal);
    }

    /// <summary>Reads the WAV header's fmt chunk so support can spot a non-PCM file (which <c>SoundPlayer</c>,
    /// the winmm backend, cannot play) or a truncated/wrong file.</summary>
    private static string DescribeWav(string path)
    {
        try
        {
            var length = new FileInfo(path).Length;
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (fs.Length < 12 || new string(br.ReadChars(4)) != "RIFF")
            {
                return $"{length} bytes (not a RIFF/WAV file)";
            }
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE")
            {
                return $"{length} bytes (not a WAVE file)";
            }
            while (fs.Position + 8 <= fs.Length)
            {
                var id = new string(br.ReadChars(4));
                var size = br.ReadUInt32();
                if (id == "fmt ")
                {
                    var fmt = br.ReadUInt16();
                    var channels = br.ReadUInt16();
                    var rate = br.ReadUInt32();
                    br.ReadUInt32();
                    br.ReadUInt16();
                    var bits = br.ReadUInt16();
                    var fmtName = fmt switch
                    {
                        1 => "PCM",
                        2 => "ADPCM",
                        3 => "IEEE float",
                        6 => "A-law",
                        7 => "mu-law",
                        0xFFFE => "extensible",
                        _ => $"format {fmt}",
                    };
                    var chName = channels switch
                    {
                        1 => "mono",
                        2 => "stereo",
                        _ => $"{channels}ch",
                    };
                    var warn = fmt == 1 ? "" : "  <- not PCM; SoundPlayer may fail to play this";
                    return $"{fmtName}, {rate / 1000f:0.#} kHz, {bits}-bit, {chName}, {length} bytes{warn}";
                }
                fs.Position += size + (size & 1);
            }
            return $"{length} bytes (no fmt chunk)";
        }
        catch (Exception ex)
        {
            return $"unreadable: {ex.Message}";
        }
    }

    private string BuildSupportText()
    {
        var server = _server;
        var sb = new StringBuilder();
        sb.AppendLine("=== AetherLove debug info ===");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        sb.AppendLine($"Connection: {_signal.State}");
        sb.AppendLine($"Transport: {Server(server?.Transport)}");
        sb.AppendLine($"Account (partial): {Server(server?.PartialAccountId)}");
        sb.AppendLine($"IP: {Server(RedactIp(server?.IpAddress))}");
        sb.AppendLine();
        sb.AppendLine($"Plugin: {SystemInfo.PluginVersion()}");
        sb.AppendLine($"Dalamud (assembly): {SystemInfo.DalamudVersion()}");
        sb.AppendLine($"API protocol: {ApiVersion.Current}");
        sb.AppendLine($".NET runtime: {SystemInfo.Runtime()}");
        sb.AppendLine($"Font: {UiFonts.ActiveFamily.Id}");
        sb.AppendLine();
        sb.AppendLine($"OS: {SystemInfo.Os()}");
        sb.AppendLine($"Wine: {(Dalamud.Utility.Util.IsWine() ? "Yes" : "No")}");
        sb.AppendLine($"CPU: {SystemInfo.Cpu()}");
        sb.AppendLine($"Memory: {SystemInfo.Memory()}");
        sb.AppendLine();
        sb.AppendLine($"Local time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Time zone: {TimeZoneInfo.Local.Id} (UTC{Offset(TimeZoneInfo.Local.BaseUtcOffset)})");
        sb.AppendLine($"UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        if (server is not null)
        {
            sb.AppendLine($"Server UTC: {server.ServerTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Clock skew: {Skew()}");
        }

        sb.AppendLine();
        sb.AppendLine($"WebP decode (auto-detected): {WebpProbeText()}");
        sb.AppendLine($"Force JPEG override: {(Plugin.Configuration.ForceJpegImages ? "On" : "Off")}");
        sb.AppendLine($"Photos requested as: {(_signal.AcceptsWebp() ? "WebP" : "JPEG")}");
        sb.AppendLine($"JPEG sample render: {RenderStatus(server?.SampleJpeg, _jpegTex)}");
        sb.AppendLine($"WebP sample render: {RenderStatus(server?.SampleWebp, _webpTex)}");

        var cfg = Plugin.Configuration;
        var soundPath = NotificationSoundPlayer.ResolvePath(cfg.NotificationSoundChoice);
        sb.AppendLine();
        sb.AppendLine("--- Notification sound ---");
        var soundBlockers = SoundBlockers();
        sb.AppendLine(soundBlockers.Count == 0
            ? "Will play now: YES"
            : "Will play now: NO -- " + string.Join(" ", soundBlockers));
        sb.AppendLine($"Notifications on: {(cfg.EnableNotifications ? "Yes" : "No")}");
        sb.AppendLine($"Play a sound: {(cfg.EnableNotificationSounds ? "Yes" : "No")}");
        sb.AppendLine($"Selected sound: {cfg.NotificationSoundChoice.DisplayName()}");
        sb.AppendLine($"Mute in combat: {(cfg.HideNotificationsDuringCombat ? "On" : "Off")} (in combat now: {(Plugin.Condition[ConditionFlag.InCombat] ? "Yes" : "No")})");
        sb.AppendLine($"Output device: {NotificationSoundPlayer.DescribeOutput()}");
        sb.AppendLine($"Volume: {Plugin.Configuration.OsSettings.NotificationVolume:P0}");
        sb.AppendLine($"Sound file: {RedactPath(soundPath)}");
        sb.AppendLine($"Sound file present: {(File.Exists(soundPath) ? "Yes" : "MISSING")}");
        if (File.Exists(soundPath))
        {
            sb.AppendLine($"Sound file format: {DescribeWav(soundPath)}");
        }

        if (_serverError is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Server fetch error: {_serverError}");
        }
        return sb.ToString();
    }
}
