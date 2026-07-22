using System;
using System.Numerics;
using System.Text.Json;

namespace AetherOS.Sdk;

/// <summary>The canonical intent-type vocabulary plus payload builders and readers. Apps ignore unknown intent
/// types by design, so a typo'd literal is a silent no-op; senders and receivers both go through these constants
/// and helpers instead. Guid payloads use the key "id", file payloads the key "path", and a deep link that
/// should return to its sender carries the sender's app id under "returnApp" (the target's back affordance
/// then calls <c>IOsShell.OpenApp</c> on it instead of its own parent view).</summary>
public static class OsIntents
{
    // AetherLove surfaces.
    public const string OpenDeck = "open.deck";
    public const string OpenMessages = "open.messages";
    public const string OpenSettings = "open.settings";
    /// <summary>With an id: open the chat with that peer. Without: return to the already-selected chat.</summary>
    public const string OpenChat = "open.chat";
    public const string OpenProfile = "open.profile";

    // Places / Hangouts / Settings / News deep links.
    public const string OpenVenue = "open.venue";
    public const string OpenMyVenues = "open.myvenues";
    public const string OpenManage = "open.manage";
    public const string OpenSupporter = "open.supporter";

    /// <summary>Open the Settings app straight to the wallpaper and home-screen page.</summary>
    public const string OpenWallpaper = "open.wallpaper";
    public const string OpenEntry = "open.entry";
    public const string OpenPreview = "open.preview";

    /// <summary>Open the Clock app straight to its Timers tab (a ringing-alarm chat link uses this).</summary>
    public const string OpenClockTimers = "open.clock_timers";

    // The messenger's photo-attach round trip (a target-initiated pull, not sheet-based sharing).
    public const string PickPhoto = "pick.photo";
    public const string PhotoPicked = "photo.picked";

    /// <summary>Open the messenger's add-contact flow prefilled with a friend code (payload key "code").</summary>
    public const string MessengerAdd = "msgr.add";

    // The camera round trip: an app requests a framed shot, the camera app replies with the saved photo.
    public const string CameraCapture = "camera.capture";
    public const string CameraCaptured = "camera.captured";

    /// <summary>Add an event to the calendar app (a tapped shared-event card's "add to calendar").</summary>
    public const string CalendarAdd = "calendar.add";

    public static OsIntent Create(string type) => new() { Type = type };

    public static OsIntent Create(string type, Guid id) => new()
    {
        Type = type,
        PayloadJson = $"{{\"id\":\"{id:D}\"}}",
    };

    public static OsIntent CreatePath(string type, string path) => new()
    {
        Type = type,
        PayloadJson = JsonSerializer.Serialize(new PathPayload(path)),
    };

    public static OsIntent CreateCode(string type, string code) => new()
    {
        Type = type,
        PayloadJson = JsonSerializer.Serialize(new CodePayload(code)),
    };

    /// <summary>A deep link whose target should offer a way back to the sending app.</summary>
    public static OsIntent CreateReturn(string type, string returnAppId) => new()
    {
        Type = type,
        PayloadJson = JsonSerializer.Serialize(new ReturnPayload(returnAppId)),
    };

    /// <summary>A deep link to a specific id whose target should offer a way back to the sending app.</summary>
    public static OsIntent CreateReturn(string type, Guid id, string returnAppId) => new()
    {
        Type = type,
        PayloadJson = JsonSerializer.Serialize(new IdReturnPayload(id, returnAppId)),
    };

    /// <summary>Asks the camera app for a framed shot. <paramref name="aspect"/> is cropHeight/cropWidth
    /// (matching <see cref="CameraRequest"/>); the reply arrives at <paramref name="returnAppId"/> as a
    /// <see cref="CameraCaptured"/> intent.</summary>
    public static OsIntent CreateCameraCapture(string returnAppId, float aspect, int minCropWidth) => new()
    {
        Type = CameraCapture,
        PayloadJson = JsonSerializer.Serialize(new CameraCapturePayload(returnAppId, aspect, minCropWidth)),
    };

    /// <summary>The camera app's reply: the saved photo path plus the crop rect the user framed (x, y, w, h
    /// in image pixels).</summary>
    public static OsIntent CreateCameraShot(string path, Vector4 crop) => new()
    {
        Type = CameraCaptured,
        PayloadJson = JsonSerializer.Serialize(new CameraShotPayload(path, crop.X, crop.Y, crop.Z, crop.W)),
    };

    public static OsIntent CreateCalendarAdd(string title, string note, long startUnixSeconds) => new()
    {
        Type = CalendarAdd,
        PayloadJson = JsonSerializer.Serialize(new CalendarAddPayload(title, note, startUnixSeconds)),
    };

    public static bool TryGetCalendarAdd(OsIntent intent, out string title, out string note, out long startUnixSeconds)
    {
        title = "";
        note = "";
        startUnixSeconds = 0;
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            var payload = JsonSerializer.Deserialize<CalendarAddPayload>(intent.PayloadJson);
            if (payload is null || string.IsNullOrEmpty(payload.title))
            {
                return false;
            }
            title = payload.title;
            note = payload.note ?? "";
            startUnixSeconds = payload.start;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetId(OsIntent intent, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("id", out var el)
                && el.ValueKind == JsonValueKind.String
                && Guid.TryParse(el.GetString(), out id);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetPath(OsIntent intent, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("path", out var el)
                && el.ValueKind == JsonValueKind.String
                && el.GetString() is { Length: > 0 } value)
            {
                path = value;
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetCode(OsIntent intent, out string code)
    {
        code = "";
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var el)
                && el.ValueKind == JsonValueKind.String
                && el.GetString() is { Length: > 0 } value)
            {
                code = value;
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetReturnApp(OsIntent intent, out string returnAppId)
    {
        returnAppId = "";
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("returnApp", out var el)
                && el.ValueKind == JsonValueKind.String
                && el.GetString() is { Length: > 0 } value)
            {
                returnAppId = value;
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetCameraCapture(OsIntent intent, out string returnAppId, out float aspect, out int minCropWidth)
    {
        returnAppId = "";
        aspect = 1f;
        minCropWidth = 128;
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            var payload = JsonSerializer.Deserialize<CameraCapturePayload>(intent.PayloadJson);
            if (payload is null || string.IsNullOrEmpty(payload.returnApp) || payload.aspect <= 0f)
            {
                return false;
            }
            returnAppId = payload.returnApp;
            aspect = payload.aspect;
            minCropWidth = Math.Max(1, payload.minWidth);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetCameraShot(OsIntent intent, out string path, out Vector4 crop)
    {
        path = "";
        crop = Vector4.Zero;
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            var payload = JsonSerializer.Deserialize<CameraShotPayload>(intent.PayloadJson);
            if (payload is null || string.IsNullOrEmpty(payload.path))
            {
                return false;
            }
            path = payload.path;
            crop = new Vector4(payload.cropX, payload.cropY, payload.cropW, payload.cropH);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record PathPayload(string path);

    private sealed record CodePayload(string code);

    private sealed record ReturnPayload(string returnApp);

    private sealed record IdReturnPayload(Guid id, string returnApp);

    private sealed record CameraCapturePayload(string returnApp, float aspect, int minWidth);

    private sealed record CameraShotPayload(string path, float cropX, float cropY, float cropW, float cropH);

    private sealed record CalendarAddPayload(string title, string note, long start);
}
