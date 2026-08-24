namespace AetherLove.Shared.Together;

/// <summary>Wire-level bounds shared by the server and every client surface.</summary>
public static class TogetherLimits
{
    public const int PartyCodeLength = 6;

    /// <summary>The party's own ceiling. Activities (an Echo room, a wayfinder hunt) may impose smaller
    /// bounds of their own; the party never grows past this. Four keeps the pet huddle and the widget
    /// roster readable; the server can raise it per environment via <c>Together:MaxMembers</c>.</summary>
    public const int MaxMembers = 4;

    /// <summary>Emoji-aware length cap for one chat line (<c>EmojiText.EffectiveLength</c>).</summary>
    public const int ChatMaxLength = 200;

    /// <summary>How many recent lines the server replays into a snapshot. The ring is in-memory only:
    /// it dies with the party (and with a server restart), nothing is ever written to disk.</summary>
    public const int ChatReplayLines = 50;
}
