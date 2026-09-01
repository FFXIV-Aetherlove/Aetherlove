namespace AetherLove.Shared.EchoVidya;

/// <summary>Where a queued entry plays from. APPEND-ONLY: values are stored in the database and travel on
/// the wire, so they are never renumbered and a retired source keeps its number forever.</summary>
public enum EchoMediaSource : short
{
    YouTube = 0,
    Twitch = 1,
}
