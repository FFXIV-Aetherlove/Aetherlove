namespace AetherLove.Screens;

/// <summary>The ordered steps of the onboarding wizard. The numeric values double as the progress-dot
/// index in the header.</summary>
public enum OnboardingStep
{
    Welcome = 0,
    HowItWorks = 1,
    TermsOfService = 2,
    XIVAuth = 3,
    EncryptionSetup = 4,
    ProfileInfo = 5,
    AvatarUpload = 6,
    Photos = 7,
    OptionalInfo = 8,
    Filters = 9,
    Preferences = 10,
    Finished = 11,
}
