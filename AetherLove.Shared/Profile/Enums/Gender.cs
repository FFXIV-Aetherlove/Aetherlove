using System;

namespace AetherLove.Shared.Profile.Enums;

/// <summary>None reserved for NPC fakes; Other selectable for non-Male/Female identity; clients omit icon for None/Other.</summary>
[Flags]
public enum Gender : short
{
    None = 0,
    Male = 1,
    Female = 2,
    Other = 4,
}
