using System;

namespace AetherLove.Config;

/// <summary>A user-created chat category (client-side only). Display order is the list position in
/// <see cref="Configuration.ChatCategories"/>; membership lives in <see cref="Configuration.ChatCategoryMembers"/>.</summary>
public sealed class ChatCategoryConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Avatar fill colour, packed 0xAABBGGRR.</summary>
    public uint Color { get; set; }
}
