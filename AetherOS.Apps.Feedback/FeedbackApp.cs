using System;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Feedback;

/// <summary>The feedback desk: the sole home for the user feedback form, a self-contained surface app.</summary>
public sealed class FeedbackApp : IAetherApp
{
    private readonly Func<string> _name;
    private readonly FeedbackScreen _screen;

    public FeedbackApp(Func<string> name, IFeedbackHost host)
    {
        _name = name;
        _screen = new FeedbackScreen(host);
    }

    public string Id => "feedback";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.CommentDots;
    public Vector4 TileTop => new(0.42f, 0.66f, 0.95f, 1f);
    public Vector4 TileBottom => new(0.20f, 0.34f, 0.72f, 1f);
    public int Badge => 0;
    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public void Open()
    {
    }

    public void OnForeground() => _screen.OnForeground();

    public void Draw(OsAppContext ctx) => _screen.Draw(ctx);

    public void OnIntent(OsIntent intent)
    {
    }
}
