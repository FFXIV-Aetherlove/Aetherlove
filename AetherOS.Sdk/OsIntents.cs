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
    public const string OpenLevemete = "open.levemete";
    public const string OpenMyVenues = "open.myvenues";
    public const string OpenManage = "open.manage";
    public const string OpenSupporter = "open.supporter";

    /// <summary>Open the Settings app straight to the wallpaper and home-screen page.</summary>
    public const string OpenWallpaper = "open.wallpaper";
    public const string OpenEntry = "open.entry";
    public const string OpenPreview = "open.preview";

    /// <summary>Open the Clock app straight to its Timers tab (a ringing-alarm chat link uses this).</summary>
    public const string OpenClockTimers = "open.clock_timers";

    /// <summary>Open the Market app on a specific item (payload key "itemId", optional "returnApp").</summary>
    public const string OpenMarketItem = "open.market_item";

    // The messenger's photo-attach round trip (a target-initiated pull, not sheet-based sharing).
    public const string PickPhoto = "pick.photo";
    public const string PhotoPicked = "photo.picked";

    /// <summary>Open the messenger's add-contact flow prefilled with a friend code (payload key "code").</summary>
    public const string MessengerAdd = "msgr.add";

    /// <summary>Replays the ceremony's last act without touching anything the server knows. For tuning it,
    /// which otherwise means spending two hundred sparks and waiting out four gates per look.</summary>
    public const string AetherlingReplayBirth = "aetherling.replay_birth";

    /// <summary>Open the pet's status page. Fired by the floating creature's own menu, which is outside the
    /// phone and so has no other way in.</summary>
    public const string AetherlingStatus = "aetherling.status";

    /// <summary>The staff reset landed: forget everything local about the creature and go back to the start.
    /// The app cannot notice on its own, because the thing it would ask about no longer exists.</summary>
    public const string AetherlingReset = "aetherling.reset";

    /// <summary>Open the pet with its basket out, ready to feed. Fired by the store the moment crystals are
    /// bought, so the thing you just bought is one tap from the mouth it was bought for.</summary>
    public const string AetherlingFeed = "aetherling.feed";

    /// <summary>Join an Echo watch room from a tapped share card (payload keys "id" and "code", plus the
    /// usual optional "returnApp"). Both are carried because the room id addresses the room and the code
    /// authorises the join, exactly as the share token pairs them.</summary>
    public const string EchoJoin = "echo.join";

    // The camera round trip: an app requests a framed shot, the camera app replies with the saved photo.
    public const string CameraCapture = "camera.capture";
    public const string CameraCaptured = "camera.captured";

    /// <summary>Add an event to the calendar app (a tapped shared-event card's "add to calendar").</summary>
    public const string CalendarAdd = "calendar.add";

    /// <summary>Deep-link into the Store (payload key "path", e.g. "crystals/fire": a category key,
    /// optionally followed by a search seed). The Aetherling crystal-shop chip targets this.</summary>
    public const string StoreOpen = "store.open";

    /// <summary>Open the Wallet. Sent with a "returnApp" payload so the wallet offers a one-click way back
    /// to whatever sent the user there.</summary>
    public const string WalletOpen = "wallet.open";

    /// <summary>Open the Wallet straight on its ways-to-earn page (the store's "you need more sparks" path).</summary>
    public const string WalletEarn = "wallet.earn";

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

    public static OsIntent CreateMarketItem(uint itemId, string? returnAppId = null) => new()
    {
        Type = OpenMarketItem,
        PayloadJson = JsonSerializer.Serialize(new MarketItemPayload(itemId, returnAppId)),
    };

    /// <summary>An Echo room deep link: the id addresses the room, the code authorises the join.</summary>
    public static OsIntent CreateRoomJoin(string type, Guid id, string code, string? returnAppId = null) => new()
    {
        Type = type,
        PayloadJson = JsonSerializer.Serialize(new RoomJoinPayload(id, code, returnAppId)),
    };

    public static bool TryGetRoomJoin(OsIntent intent, out Guid id, out string code)
    {
        id = Guid.Empty;
        code = "";
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("id", out var idEl)
                || !doc.RootElement.TryGetProperty("code", out var codeEl)
                || !Guid.TryParse(idEl.GetString(), out var parsed)
                || codeEl.GetString() is not { Length: > 0 } parsedCode)
            {
                return false;
            }
            id = parsed;
            code = parsedCode;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetMarketItem(OsIntent intent, out uint itemId)
    {
        itemId = 0;
        if (string.IsNullOrEmpty(intent.PayloadJson))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("itemId", out var el)
                && el.ValueKind == JsonValueKind.Number
                && el.TryGetUInt32(out var value)
                && value > 0)
            {
                itemId = value;
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static OsIntent CreateCalendarAdd(string title, string note, long startUnixSeconds,
        int? remindMinutes = null) => new()
    {
        Type = CalendarAdd,
        PayloadJson = JsonSerializer.Serialize(new CalendarAddPayload(title, note, startUnixSeconds, remindMinutes)),
    };

    public static bool TryGetCalendarAdd(OsIntent intent, out string title, out string note, out long startUnixSeconds)
        => TryGetCalendarAdd(intent, out title, out note, out startUnixSeconds, out _);

    public static bool TryGetCalendarAdd(OsIntent intent, out string title, out string note, out long startUnixSeconds,
        out int? remindMinutes)
    {
        title = "";
        note = "";
        startUnixSeconds = 0;
        remindMinutes = null;
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
            remindMinutes = payload.remind;
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

    private sealed record MarketItemPayload(uint itemId, string? returnApp);

    private sealed record RoomJoinPayload(Guid id, string code, string? returnApp);

    private sealed record CameraCapturePayload(string returnApp, float aspect, int minWidth);

    private sealed record CameraShotPayload(string path, float cropX, float cropY, float cropW, float cropH);

    private sealed record CalendarAddPayload(string title, string note, long start, int? remind = null);
}
