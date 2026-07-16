using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Signal;
using AetherLove.Shared;
using AetherLove.Shared.Diagnostics;
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
    private readonly AetherLoveHubClient _hub;

    private volatile bool _loading;
    private volatile string? _serverError;
    private volatile DebugInfoDto? _server;
    private DateTimeOffset _serverFetchedLocalUtc;

    private ISharedImmediateTexture? _jpegTex;
    private ISharedImmediateTexture? _webpTex;
    private bool _samplesStored;

    private DateTime _copiedAt = DateTime.MinValue;

    private static readonly Vector4 HeadingCol = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 OkCol = new(0.40f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 WarnCol = new(0.95f, 0.65f, 0.25f, 1f);
    private static readonly Vector4 ErrCol = new(0.95f, 0.45f, 0.45f, 1f);

    public DebugWindow(AetherSignalService signal, AetherLoveHubClient hub)
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

    public override void OnOpen() => LoadServerInfo();

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

        Section("Connection");
        Row("Status", _signal.State.ToString());
        Row("Transport", Server(_server?.Transport));

        Section("Account");
        Row("Account ID (partial)", Server(_server?.PartialAccountId));
        Row("IP address", Server(_server?.IpAddress));

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
        ImGui.TextDisabled("Takes effect on the next reconnect (reopen AetherLove). Both formats are sent below regardless of OS — compare which one renders.");
        ImGui.PopTextWrapPos();
        DrawSamples(scale);
    }

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
            ImGui.TextColored(ErrCol, $"{label} sample ({FormatName(bytes)}): failed to decode — the gray-block symptom for this format.");
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
        false => "Not supported — served JPEG",
        null => "Not yet probed — defaulting to JPEG",
    };

    private string Server(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }
        return _loading ? "(loading...)" : "(unavailable)";
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
        sb.AppendLine($"IP: {Server(server?.IpAddress)}");
        sb.AppendLine();
        sb.AppendLine($"Plugin: {SystemInfo.PluginVersion()}");
        sb.AppendLine($"Dalamud (assembly): {SystemInfo.DalamudVersion()}");
        sb.AppendLine($"API protocol: {ApiVersion.Current}");
        sb.AppendLine($".NET runtime: {SystemInfo.Runtime()}");
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

        if (_serverError is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Server fetch error: {_serverError}");
        }
        return sb.ToString();
    }
}
