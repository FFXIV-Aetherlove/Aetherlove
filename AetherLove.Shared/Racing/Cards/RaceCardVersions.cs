namespace AetherLove.Shared.Racing.Cards;

/// <summary>The identity strings a carded race is resolved and replayed under. Only <see cref="SupportVersion"/> is
/// stored per race: a change to the adapter, the catalogue bytes or the mechanics hash bumps it, so the one string
/// on the row covers all of them. A stored race whose version is neither empty nor this one is not replayable by
/// this build.</summary>
public static class RaceCardVersions
{
    public const int SchemaVersion = 1;

    public const string AdapterVersion = "support-authoring-v1";

    public const string SupportVersion = "aetherlove-cards-v1";

    public const string CatalogueVersion = "core-set-design-v4";

    public const string MechanicsSha256 = RaceCardCatalogue.MechanicsSha256;

    /// <summary>Whether this build can replay a race stored under <paramref name="supportVersion"/>: empty means
    /// card-free and steps the engine directly; otherwise the versions must be equal.</summary>
    public static bool CanReplay(string? supportVersion)
    {
        return string.IsNullOrEmpty(supportVersion) || supportVersion == SupportVersion;
    }
}
