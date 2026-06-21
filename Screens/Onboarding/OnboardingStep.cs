namespace AetherLove.Screens;

/// <summary>The ordered steps of the onboarding wizard. The numeric values double as the progress-dot
/// index in the header.</summary>
public enum OnboardingStep
{
    Welcome = 0,
    Preferences = 1,
    HowItWorks = 2,
    TermsOfService = 3,
    XIVAuth = 4,
    EncryptionSetup = 5,
    ProfileInfo = 6,
    AvatarUpload = 7,
    Photos = 8,
    OptionalInfo = 9,
    Filters = 10,
    Finished = 11,
}
