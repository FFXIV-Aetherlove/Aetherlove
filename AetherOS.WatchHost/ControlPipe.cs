using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherOS.WatchHost;

/// <summary>The inbound message tags. Unknown tags are ignored, which makes a typo a silent no-op, so the
/// dispatch site names them from here instead of typing literals.</summary>
internal static class ControlCommands
{
    public const string Load = "load";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Seek = "seek";
    public const string Volume = "volume";
    public const string Size = "size";
    public const string Captions = "captions";
    public const string Shutdown = "shutdown";
}

/// <summary>One inbound command. Fields the command's tag does not carry stay null, so a caller can tell an
/// absent value from a zero one.</summary>
internal sealed record ControlCommand(
    string Type,
    string? VideoId = null,
    double? Start = null,
    long? Epoch = null,
    double? Seconds = null,
    double? Volume = null,
    int? Width = null,
    int? Height = null,
    bool? On = null);

internal sealed record HostHello(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("hostVersion")] string HostVersion)
{
    [JsonPropertyName("t")]
    [JsonPropertyOrder(-1)]
    public string T => "hello";
}

internal sealed record HostReady
{
    [JsonPropertyName("t")]
    public string T => "ready";
}

internal sealed record HostLog([property: JsonPropertyName("msg")] string Msg)
{
    [JsonPropertyName("t")]
    [JsonPropertyOrder(-1)]
    public string T => "log";
}

/// <summary>The control channel to the plugin. The plugin is the pipe server and this host is the client, so
/// the plugin owns the channel's lifetime and a dead plugin surfaces here as a read fault.
///
/// The framing is UTF-8 JSON Lines: exactly one object per line, terminated by \n, in both directions.</summary>
internal sealed class ControlPipe : IDisposable
{
    private const int ProtocolVersion = 1;

    private readonly string pipeName;
    private readonly object writeGate = new();

    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private StreamReader? reader;
    private volatile bool closing;

    /// <summary>Raised on the read thread for every well-formed inbound line.</summary>
    public event Action<ControlCommand>? CommandReceived;

    /// <summary>Raised once when the channel dies: the plugin closed it, or a read or write threw.</summary>
    public event Action<string>? Faulted;

    public ControlPipe(string pipeName)
    {
        this.pipeName = pipeName;
    }

    /// <summary>Connects and announces the host. Reading does not start here: the caller wires its command
    /// handlers first, so a command cannot arrive before there is anything to run it.</summary>
    public void Connect(int timeoutMs, string hostVersion)
    {
        this.pipe = new NamedPipeClientStream(".", this.pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        this.pipe.Connect(timeoutMs);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        this.writer = new StreamWriter(this.pipe, utf8)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        this.reader = new StreamReader(this.pipe, utf8);

        Send(new HostHello(ProtocolVersion, hostVersion));
    }

    public void Start()
    {
        var pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "watch-host-control",
        };
        pump.Start();
    }

    public void SendReady() => Send(new HostReady());

    public void SendState(PlayerState state) => Send(state);

    public void SendLog(string message) => Send(new HostLog(message));

    private void Pump()
    {
        try
        {
            while (true)
            {
                var line = this.reader?.ReadLine();
                if (line is null)
                {
                    Fault("Control pipe closed by the plugin.");
                    return;
                }
                if (line.Length == 0)
                {
                    continue;
                }
                if (TryParse(line, out var command))
                {
                    CommandReceived?.Invoke(command);
                }
            }
        }
        catch (Exception ex)
        {
            Fault($"Control pipe read failed: {ex.Message}");
        }
    }

    private static bool TryParse(string line, out ControlCommand command)
    {
        command = new ControlCommand(string.Empty);
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (JsonRead.Text(root, "t") is not { Length: > 0 } type)
            {
                return false;
            }

            command = new ControlCommand(
                type,
                JsonRead.Text(root, "videoId"),
                JsonRead.Number(root, "start"),
                JsonRead.Integer(root, "epoch"),
                JsonRead.Number(root, "sec"),
                JsonRead.Number(root, "v"),
                JsonRead.Int32(root, "w"),
                JsonRead.Int32(root, "h"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Send<T>(T message)
    {
        var line = JsonSerializer.Serialize(message);
        lock (this.writeGate)
        {
            if (this.writer is not { } target)
            {
                return;
            }
            try
            {
                target.WriteLine(line);
                target.Flush();
            }
            catch (Exception ex)
            {
                Fault($"Control pipe write failed: {ex.Message}");
            }
        }
    }

    private void Fault(string reason)
    {
        if (this.closing)
        {
            return;
        }
        this.closing = true;
        Faulted?.Invoke(reason);
    }

    public void Dispose()
    {
        this.closing = true;
        lock (this.writeGate)
        {
            try
            {
                this.writer?.Dispose();
            }
            catch (Exception)
            {
            }
            this.writer = null;
        }
        try
        {
            this.reader?.Dispose();
            this.pipe?.Dispose();
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>Kind-checked reads off a <see cref="JsonElement"/>. Both parsers here talk to code we do not
/// own (the plugin's writer and the watch page), so a missing or wrongly typed field has to read as absent
/// rather than throw.</summary>
internal static class JsonRead
{
    public static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var parsed)
            ? parsed
            : null;

    public static long? Integer(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    public static int? Int32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}
