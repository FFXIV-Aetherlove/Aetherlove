using System;

namespace AetherLove.Services;

/// <summary>Hand-off slot for a together-party invite on its way into a chat. The chat stages it as an
/// ATTACHMENT rather than sending it: the user writes the invitation, and the party token is appended to
/// their words on send, which is what makes the message render as a join card at the other end.</summary>
public sealed class PartyInviteShareContext
{
    /// <summary>The party a picked chat should attach, with the code that authorises the join.</summary>
    public (Guid PartyId, string Code)? PendingParty { get; set; }
}
