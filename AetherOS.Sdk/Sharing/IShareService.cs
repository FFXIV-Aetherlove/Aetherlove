using System.Collections.Generic;

namespace AetherOS.Sdk;

/// <summary>The share source API: offer a <see cref="ShareItem"/> to whichever apps accept its type. The shell
/// discovers targets (apps whose <see cref="IAetherApp.AcceptedShareTypes"/> contains the type), shows the OS
/// share sheet, and delivers to the one the user picks. A source never names a target.</summary>
public interface IShareService
{
    /// <summary>Opens the OS share sheet for this item. Does nothing when no app accepts the type.</summary>
    void Offer(ShareItem item, string? title = null);

    /// <summary>The apps that currently accept <paramref name="type"/> (available, and not the source app).</summary>
    IReadOnlyList<IAetherApp> TargetsFor(string type);

    /// <summary>Whether any app accepts <paramref name="type"/>; a source gates its Share affordance on this.</summary>
    bool CanShare(string type) => TargetsFor(type).Count > 0;
}
