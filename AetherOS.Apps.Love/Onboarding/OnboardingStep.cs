namespace AetherLove.Screens;

/// <summary>The ordered steps of the AetherLove dating-profile onboarding. Numeric values are progress-bar indices; save points are Photos, Extras, and Filters.</summary>
public enum OnboardingStep
{
    Welcome = 0,
    Name = 1,
    Bio = 2,
    Character = 3,
    Languages = 4,
    Interests = 5,
    LookingFor = 6,
    ImageRules = 7,
    Avatar = 8,
    Photos = 9,
    Extras = 10,
    Filters = 11,
    Finished = 12,
}
