using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering.LineArt;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The moment a diet earns a flourish: a single scratch ticket handed over on the pet's own
/// page, right where the crystal that earned it landed. The prize is granted when it is scratched and
/// not before, exactly like the grown-up's three cards, so an unclaimed ticket is an unowned reaction
/// and the pet page keeps a chip up until it is claimed.</summary>
internal sealed class ReactionTicketOverlay(IAetherlingHost host, PetRuntime pet)
{
    /// <summary>Earned-reaction cards live at this plus the element, matching the server's own base.</summary>
    public const short SlotBase = 10;

    /// <summary>And the two earned-shell milestones, one slot range each, same shape.</summary>
    public const short ShellSlotBase = 20;

    public const short ShellSlot2Base = 30;

    private readonly LineCanvas _preview = new();
    private ScratchCard? _card;
    private AetherlingDto? _core;
    private short _slot = -1;
    private bool _busy;
    private float _in;
    private string? _error;
    private float _errorLeft;
    private AetherlingDto? _pendingRevealed;
    private string? _pendingError;

    public bool Visible => _slot >= 0;

    /// <summary>Raised with the reply that granted the prize, so the page can adopt it and re-read what
    /// the account owns; the reaction is live from that moment.</summary>
    public event Action<AetherlingDto>? Revealed;

    /// <summary>Raised with the shell just won when the owner takes the button up on it. A flourish
    /// needs nowhere to go (it is live the moment it is granted), but a form is a thing to put ON,
    /// so its ticket ends in the wardrobe with the form already worn.</summary>
    public event Action<string>? WearRequested;

    /// <summary>The element a ticket's slot stands for, whichever of the three ranges it sits in, or
    /// None when the slot is not an earned ticket at all.</summary>
    public static AetherlingElement ElementFor(short slot) => slot switch
    {
        > SlotBase and <= SlotBase + (short)AetherlingElement.Water => (AetherlingElement)(slot - SlotBase),
        > ShellSlotBase and <= ShellSlotBase + (short)AetherlingElement.Water => (AetherlingElement)(slot - ShellSlotBase),
        > ShellSlot2Base and <= ShellSlot2Base + (short)AetherlingElement.Water => (AetherlingElement)(slot - ShellSlot2Base),
        _ => AetherlingElement.None,
    };

    /// <summary>True for a ticket whose prize is a shell rather than a flourish: the copy and the
    /// revealed face both branch on it.</summary>
    public static bool IsShellSlot(short slot) => slot > ShellSlotBase;

    public void Open(AetherlingDto core, short slot)
    {
        _core = core;
        _slot = slot;
        _card = new ScratchCard(slot);
        _busy = false;
        _in = 0f;
        _error = null;
        _errorLeft = 0f;
    }

    public void Adopt(AetherlingDto core) => _core = core;

    public void Close()
    {
        _slot = -1;
        _card = null;
        _core = null;
    }

    public void Draw(OsAppContext ctx, Vector2 origin, Vector2 size, float dt)
    {
        if (_card is null || _core is null)
        {
            return;
        }
        DrainPending();

        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##aetherlingTicket", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        _in = ctx.ReduceMotion ? 1f : MathF.Min(1f, _in + (dt * 3.4f));
        var ease = Look.EaseOut(_in);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 0.86f }, ease));

        var element = Elements.Find((short)ElementFor(_slot));
        var accent = element?.Accent ?? Look.Crystal;
        var name = _core.PetName ?? AetherlingLimits.DefaultName;
        var dto = CardFor(_core, _slot);
        var revealed = dto?.RevealedAtUtc is not null;

        var panelW = MathF.Min(size.X - Px(28f), Px(320f));
        var panelH = Px(268f);
        var panelTl = new Vector2(
            origin.X + ((size.X - panelW) * 0.5f),
            origin.Y + ((size.Y - panelH) * 0.5f) + ((1f - ease) * Px(16f)));
        var panelBr = panelTl + new Vector2(panelW, panelH);
        dl.AddRectFilled(panelTl, panelBr, Look.U32(new Vector4(0.07f, 0.06f, 0.11f, 0.98f), ease), Px(18f));
        dl.AddRect(panelTl, panelBr, Look.U32(accent, 0.45f * ease), Px(18f), ImDrawFlags.RoundCornersAll, Px(1.2f));
        Look.Halo(dl, new Vector2(panelTl.X + (panelW * 0.5f), panelTl.Y + Px(30f)), panelW * 0.9f, accent,
            0.10f * ease, 5);

        var centreX = panelTl.X + (panelW * 0.5f);
        var y = panelTl.Y + Px(16f);
        using (ctx.TitleFont?.Push())
        {
            Look.Centred(dl, ctx.Localize("os.aetherling_ticket_title"), centreX, y, Look.U32(Look.CrystalPale, ease));
            y += ImGui.GetTextLineHeight() + Px(4f);
        }
        var body = string.Format(
            ctx.Localize(IsShellSlot(_slot) ? "os.aetherling_ticket_body_shell" : "os.aetherling_ticket_body"),
            name,
            element is { } def ? ctx.Localize(Elements.NameKey(def)) : string.Empty);
        y += Look.CentredWrapped(dl, body, centreX, y, panelW - Px(28f), Look.U32(Look.Body, 0.9f * ease), 0.88f)
            * Look.LineStep(0.88f);

        var cardW = panelW - Px(32f);
        var cardH = Px(96f);
        var cardTl = new Vector2(panelTl.X + Px(16f), y + Px(10f));
        _card.Draw(ctx, dl, cardTl, new Vector2(cardW, cardH), revealed, _busy,
            (faceTl, faceSize) => DrawFace(ctx, dl, faceTl, faceSize, dto, accent));

        if (_card.WantsReveal && !_busy && !revealed)
        {
            _card.MarkRevealRequested();
            Reveal();
        }

        var buttonTl = new Vector2(panelTl.X + Px(16f), panelBr.Y - Px(48f));
        var buttonSize = new Vector2(cardW, Px(34f));
        if (revealed && DrawDone(ctx, dl, buttonTl, buttonSize, accent, ease))
        {
            var won = IsShellSlot(_slot) && (dto?.PrizeRefs ?? []).Length > 0 ? dto!.PrizeRefs![0] : null;

            // Closed before the hand-off, so the wardrobe opens on a clear page rather than under
            // a ticket that is still fading.
            Close();
            if (won is not null)
            {
                WearRequested?.Invoke(won);
            }
            return;
        }

        _errorLeft = MathF.Max(0f, _errorLeft - dt);
        if (_error is { } error && _errorLeft > 0f)
        {
            Look.Centred(dl, error, centreX, panelBr.Y - Px(14f), Look.U32(Look.Spark, 0.9f), 0.82f);
        }

        // The scrim last, so the card and the button take their own clicks first, and hit-tested by hand
        // so a press outside the panel is simply swallowed rather than closing an unclaimed prize.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##aetherlingTicketScrim", size);
    }

    private bool DrawDone(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, Vector4 accent, float ease)
    {
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingTicketDone", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var br = tl + size;
        dl.AddRectFilled(tl, br, Look.U32(accent, (hovered ? 0.85f : 0.7f) * ease), size.Y * 0.5f);
        var label = IsShellSlot(_slot) ? "os.aetherling_ticket_wear" : "os.aetherling_ticket_done";
        Look.Centred(dl, ctx.Localize(label), tl.X + (size.X * 0.5f),
            tl.Y + ((size.Y - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.Void, 0.95f * ease));
        return pressed;
    }

    /// <summary>The face: the kind of thing before it is scratched, its own name after. A won form draws
    /// its own portrait, the same little outline the wardrobe rows show; a flourish has no shape to draw,
    /// so an icon in the element's colour stands in for it.</summary>
    private void DrawFace(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, AetherlingScratchCardDto? dto, Vector4 accent)
    {
        var centreX = tl.X + (size.X * 0.5f);
        Look.Centred(dl,
            ctx.Localize(IsShellSlot(_slot) ? "os.aetherling_ticket_kind_shell" : "os.aetherling_ticket_kind"),
            centreX, tl.Y + Px(10f), Look.U32(Look.Whisper, 0.7f), 0.8f);
        if (dto?.RevealedAtUtc is null)
        {
            return;
        }

        var itemRef = (dto.PrizeRefs ?? []).Length > 0 ? dto.PrizeRefs![0] : string.Empty;
        var art = size.Y - Px(34f);
        var artTl = new Vector2(tl.X + Px(16f), tl.Y + Px(28f));
        var artCentre = artTl + new Vector2(art * 0.5f, art * 0.5f);
        var drawn = IsShellSlot(_slot) && ShellPreview.Draw(dl, _preview, itemRef, artTl, art);
        if (!drawn)
        {
            IconDraw.AddCentered(dl, IsShellSlot(_slot) ? FontAwesomeIcon.Shapes : FontAwesomeIcon.Star,
                art * 0.5f, artCentre, Look.U32(accent, 0.95f));
        }

        var label = IsShellSlot(_slot)
            ? Ui.ShellCatalog.Find(itemRef)?.Name ?? itemRef
            : ReactionDef.Find(itemRef)?.Name ?? itemRef;
        var labelX = tl.X + Px(24f) + art;
        Look.LeftWrapped(dl, label, labelX, tl.Y + (size.Y * 0.5f) - Px(6f),
            tl.X + size.X - Px(14f) - labelX, Look.U32(Look.CrystalPale), 1.0f);
    }

    private static AetherlingScratchCardDto? CardFor(AetherlingDto core, short slot)
    {
        foreach (var card in core.Cards ?? [])
        {
            if (card.Slot == slot)
            {
                return card;
            }
        }
        return null;
    }

    private void Reveal()
    {
        _busy = true;
        _error = null;
        var slot = _slot;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.RevealScratchAsync(slot).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingRevealed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    private void DrainPending()
    {
        if (Interlocked.Exchange(ref _pendingRevealed, null) is { } dto)
        {
            _busy = false;
            _core = dto;
            _card?.Celebrate();
            pet.Celebrate();
            Revealed?.Invoke(dto);
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } error)
        {
            _busy = false;
            _error = error;
            _errorLeft = 4f;
        }
    }
}
