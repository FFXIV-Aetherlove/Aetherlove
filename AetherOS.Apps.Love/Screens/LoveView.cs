using System;
using System.Numerics;
using AetherOS.Sdk;

namespace AetherLove.Screens;

/// <summary>The dating app's internal views. Navigated purely inside the app; the OS shell only ever sees
/// <c>Screen.App</c>. Cross-app moves go through <see cref="IOsShell"/> on <see cref="LoveShell"/>.</summary>
public enum LoveView
{
    ProfilePicker,
    Onboarding,
    Deck,
    Match,
    ChatList,
    ChatCategory,
    Chat,
    Profile,
    Settings,
    MyProfile,
    Blocked,
    EncryptionVerification,
}

/// <summary>The app's internal router: which <see cref="LoveView"/> is showing plus a change flag, mirroring
/// the shell's <c>ScreenRouter</c> so the moved screens keep their <c>_router.Navigate(...)</c> shape. Locked
/// because navigation can fire off the UI thread.</summary>
public sealed class LoveRouter
{
    private readonly object _lock = new();
    private LoveView _current;
    private bool _navigationOccurred;

    public LoveRouter(LoveView initial)
    {
        _current = initial;
    }

    public LoveView Current
    {
        get { lock (_lock) { return _current; } }
    }

    public bool NavigationOccurred
    {
        get { lock (_lock) { return _navigationOccurred; } }
        set { lock (_lock) { _navigationOccurred = value; } }
    }

    public void Navigate(LoveView view)
    {
        lock (_lock)
        {
            _current = view;
            _navigationOccurred = true;
        }
    }
}

/// <summary>Frame-scoped holder for the OS shell handle. The app stamps <see cref="Shell"/> from
/// <c>OsAppContext.Shell</c> at the top of every Draw; screens that need cross-app navigation read it. It
/// also carries the camera round trip: selfies route through the camera app (landing in the camera roll),
/// and the reply intent delivers the shot back to whichever screen asked.</summary>
public sealed class LoveShell
{
    public IOsShell? Shell { get; set; }

    /// <summary>The host's door to the encryption recovery screen, stamped by the app at construction so
    /// any screen that finds a keyless profile can send the user there.</summary>
    public Action? OpenEncryptionRecovery { get; set; }

    private Action<string, Vector4>? _cameraReply;

    /// <summary>Sends a camera.capture intent for a shot framed at <paramref name="aspect"/>
    /// (cropHeight/cropWidth); <paramref name="onShot"/> fires with the saved path and crop rect once the
    /// camera app replies.</summary>
    public void RequestCamera(float aspect, int minCropWidth, Action<string, Vector4> onShot)
    {
        if (Shell is not { } shell)
        {
            return;
        }
        _cameraReply = onShot;
        shell.SendIntent("camera", OsIntents.CreateCameraCapture("aetherlove", aspect, minCropWidth));
    }

    public void DeliverCameraShot(string path, Vector4 crop)
    {
        var reply = _cameraReply;
        _cameraReply = null;
        reply?.Invoke(path, crop);
    }
}
