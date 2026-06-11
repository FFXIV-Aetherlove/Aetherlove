// Makes the phone-scale helper available as a bare Px(...) across the whole plugin, so every screen
// scales design pixels the same way without redeclaring it. See AetherLove.UI.UiScale.
global using static AetherLove.UI.UiScale;

// The shared profile-form helpers and field choices are used bare (HelpTooltip, MaskToBools,
// DrawSectionHeading, RaceValues, …) by both the onboarding and "My profile" screens.
global using static AetherLove.UI.SharedUiHelpers;
global using static AetherLove.UI.ProfileFields;
