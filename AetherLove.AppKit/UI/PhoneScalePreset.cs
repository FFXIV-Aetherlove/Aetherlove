namespace AetherLove.UI;

/// <summary>User-selectable phone size preset. The numbers are what lands in the saved config, so a new size is
/// APPENDED whatever order it reads in on screen; renumbering resizes every existing player's phone.</summary>
public enum PhoneScalePreset
{
    Small = 0,
    Medium = 1,
    Large = 2,
    XL = 3,
    XXL = 4,
    XS = 5,
}
