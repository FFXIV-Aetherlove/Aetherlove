namespace AetherLove.Config;

/// <summary>What AetherLove does to its windows when the player enters combat.</summary>
public enum CombatBehavior
{
    /// <summary>Both the phone and the bubble disappear; they reappear when combat ends.</summary>
    Hide = 0,
    /// <summary>If the phone is open, it switches to the mini bubble when combat starts.</summary>
    Minimize = 1,
    /// <summary>Nothing happens — windows stay wherever they are.</summary>
    LeaveOpen = 2,
}
