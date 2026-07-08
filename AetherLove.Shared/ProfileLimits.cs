namespace AetherLove.Shared;

/// <summary>Shared profile-field limits enforced on both the client (UI gating) and the server (authoritative).</summary>
public static class ProfileLimits
{
    /// <summary>Minimum visible characters for a display name. The client disables submit below it and the
    /// server rejects anything shorter, so a display name is always at least this long.</summary>
    public const int DisplayNameMinLength = 3;

    /// <summary>Maximum raw characters for an RP character's name/title (matches the display-name column).</summary>
    public const int CharacterNameMaxLength = 50;
}
