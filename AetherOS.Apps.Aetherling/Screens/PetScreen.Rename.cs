using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The paid rename: a pencil beside the name on the home page, the box it opens, and the cheer
/// when the new name lands. The item is spent server-side, so the client never decides whether a rename
/// is allowed; it only asks the account what it owns so the tap opens the right card.</summary>
internal sealed partial class PetScreen
{
    private const float CheerSeconds = 3.4f;

    private sealed record RenameCheck(bool Owned, bool Reached);

    private bool _renameOpen;
    private bool _renameOfferOpen;
    private bool _renameChecking;
    private RenameCheck? _pendingRenameCheck;
    private AetherlingDto? _pendingRenamed;

    private float _cheerLeft;
    private string _cheerName = string.Empty;
    private int _cheerChirps;

    /// <summary>Whether an overlay of this feature's owns the page, so the rest of it stops submitting.</summary>
    private bool RenameOverlayOpen => _renameOpen || _renameOfferOpen;

    /// <summary>Opens the box from outside the screen: the store's "use it now" hand-off. Ownership is
    /// re-read on the way in, because the purchase that sent us here is newer than anything cached.</summary>
    public void OpenRename()
    {
        if (_core is not { HatchedAtUtc: not null })
        {
            return;
        }
        AskRename();
    }

    /// <summary>The pencil next to the name. Drawn before the header text is finished with, so it sits
    /// where the name ends rather than at a measured guess.</summary>
    private void DrawRenamePill(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float lineH)
    {
        var size = new Vector2(Px(30f), Px(24f));
        var at = new Vector2(tl.X, tl.Y + ((lineH - size.Y) * 0.5f));

        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton("##aetherlingRenamePill", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(ctx.Localize("os.aetherling_rename_pill_tip"));
        }
        dl.AddRectFilled(at, at + size,
            Look.U32(Look.Crystal with { W = hovered ? 0.24f : 0.12f }), size.Y * 0.5f);
        if (_renameChecking)
        {
            LoadingSpinner.Draw(at + (size * 0.5f), Px(6f), Px(2f), Look.U32(Look.CrystalPale));
            return;
        }
        var icon = IconDraw.Measure(FontAwesomeIcon.Pen, Px(11f));
        IconDraw.Add(dl, FontAwesomeIcon.Pen, Px(11f),
            at + ((size - icon) * 0.5f), Look.U32(Look.CrystalPale, hovered ? 1f : 0.85f));
        if (pressed)
        {
            AskRename();
        }
    }

    /// <summary>Asks the account what it owns, right now. The cached inventory is not trusted here: the
    /// player may have bought one seconds ago in another app.</summary>
    private void AskRename()
    {
        if (_renameChecking || RenameOverlayOpen || _namingOpen)
        {
            return;
        }
        _renameChecking = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            var items = await host.GetOwnedItemsAsync().ConfigureAwait(false);
            var owned = items is not null && items.Any(i =>
                i.ItemKind == StoreItemKind.AetherlingConsumable
                && string.Equals(i.ItemRef, AetherlingLimits.NameChangeRef, StringComparison.OrdinalIgnoreCase)
                && i.Quantity > 0);
            if (items is not null)
            {
                Interlocked.Exchange(ref _pendingInventory, items);
            }
            Interlocked.Exchange(ref _pendingRenameCheck, new RenameCheck(owned, items is not null));
        });
    }

    /// <summary>Takes what the ownership check and the rename round trip left. Called from Draw: both
    /// open overlays and one of them starts a dance, all of which belong to the draw thread.</summary>
    private void DrainRename()
    {
        if (Interlocked.Exchange(ref _pendingRenameCheck, null) is { } check)
        {
            _renameChecking = false;
            if (!check.Reached)
            {
                _error = null;
                ShowToast(Loc.T("os.aetherling_rename_offline"));
            }
            else if (check.Owned)
            {
                _renameOpen = true;
                _nameFocusPending = true;
                _nameBuffer = _core?.PetName ?? AetherlingLimits.DefaultName;
            }
            else
            {
                _renameOfferOpen = true;
            }
        }

        if (Interlocked.Exchange(ref _pendingRenamed, null) is { } renamed)
        {
            _busy = false;
            _renameOpen = false;
            AdoptCore(renamed);
            RefreshInventory();
            StartCheer(renamed.PetName ?? AetherlingLimits.DefaultName);
        }
    }

    /// <summary>The fuss: a forced dance (the choreography is a performance, not a reward, so it plays
    /// whether or not this pet has learned the emote), a sparkle burst, a delighted glyph, and a run of
    /// chirps paced across the dance rather than fired at once.</summary>
    private void StartCheer(string name)
    {
        _cheerName = name;
        _cheerLeft = CheerSeconds;
        _cheerChirps = 0;
        pet.Celebrate();
        pet.AuditionGlyph("burst");
        if (EmoteChoreographies.Find("dance") is { } dance)
        {
            pet.PlayEmote(dance, 1f, force: true);
        }
        host.PlayChirp();
    }

    /// <summary>The cheer's own layer: the new name rising over the stage while the creature dances under
    /// it. Drawn on the page's list rather than the foreground, so a phone shade over the top covers it.</summary>
    private void DrawCheer(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float dt)
    {
        if (_cheerLeft <= 0f)
        {
            return;
        }
        _cheerLeft = MathF.Max(0f, _cheerLeft - dt);
        var played = CheerSeconds - _cheerLeft;

        var chirpsDue = played switch
        {
            >= 1.5f => 3,
            >= 0.95f => 2,
            >= 0.45f => 1,
            _ => 0,
        };
        while (_cheerChirps < chirpsDue)
        {
            _cheerChirps++;
            host.PlayChirp();
        }

        var t = Math.Clamp(played / CheerSeconds, 0f, 1f);
        var alpha = t < 0.12f ? t / 0.12f : 1f - MathF.Max(0f, (t - 0.65f) / 0.35f);
        alpha = Math.Clamp(alpha, 0f, 1f);
        var centreX = origin.X + (size.X * 0.5f);
        var y = origin.Y + (size.Y * 0.30f) - (ctx.ReduceMotion ? 0f : Px(26f) * t);

        Look.Centred(dl, _cheerName, centreX, y, Look.U32(Look.CrystalPale, alpha), 1.6f);
        Look.Centred(dl, ctx.Localize("os.aetherling_rename_cheer"), centreX, y + Px(30f),
            Look.U32(Look.Spark, alpha * 0.95f), 0.95f);

        if (ctx.ReduceMotion)
        {
            return;
        }
        for (var i = 0; i < 7; i++)
        {
            var phase = (played * 1.6f) + (i * 0.9f);
            var spin = phase % 2f;
            var r = Px(40f) + (Px(48f) * spin * 0.5f);
            var a = (i * MathF.Tau / 7f) + (played * 0.8f);
            var at = new Vector2(centreX + (MathF.Cos(a) * r * 1.5f), y + Px(14f) + (MathF.Sin(a) * r));
            dl.AddCircleFilled(at, Px(2.4f) * (1f - (spin * 0.5f)),
                Look.U32(i % 2 == 0 ? Look.Spark : Look.Crystal, alpha * (1f - (spin * 0.5f))));
        }
    }

    /// <summary>The rename box. Same shape as the free naming card, different words: this one is spending
    /// something, and it says so.</summary>
    private void DrawRenameCard(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, 0.72f));

        var pad = Px(18f);
        var cardW = size.X - (pad * 2f);
        var cardH = Px(196f);
        var tl = new Vector2(origin.X + pad, origin.Y + ((size.Y - cardH) * 0.42f));
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.10f, 0.09f, 0.16f, 0.97f)), Px(16f));
        dl.AddRect(tl, br, Look.U32(Look.Crystal, 0.35f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        var y = tl.Y + Px(16f);
        Look.Centred(dl, ctx.Localize("os.aetherling_rename_title"), tl.X + (cardW * 0.5f), y,
            Look.U32(Look.CrystalPale), 1.15f);
        y += Px(30f);
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_rename_body"), tl.X + (cardW * 0.5f), y,
            cardW - Px(28f), Look.U32(Look.Whisper, 0.9f), 0.9f);
        y += Px(40f);

        ImGui.SetCursorScreenPos(new Vector2(tl.X + Px(14f), y));
        ImGui.SetNextItemWidth(cardW - Px(28f));
        if (_nameFocusPending)
        {
            _nameFocusPending = false;
            ImGui.SetKeyboardFocusHere();
        }
        var submitted = ImGui.InputText("##aetherlingRename", ref _nameBuffer,
            AetherlingLimits.NameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        y += Px(34f);

        if (_error is { Length: > 0 })
        {
            Look.CentredWrapped(dl, _error, tl.X + (cardW * 0.5f), y, cardW - Px(28f),
                Look.U32(new Vector4(0.95f, 0.5f, 0.5f, 1f)), 0.85f);
        }

        var buttonY = br.Y - Px(46f);
        var half = (cardW - Px(38f)) * 0.5f;
        var confirm = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(14f), buttonY), half,
            ctx.Localize("os.aetherling_rename_confirm"), primary: true);
        var cancel = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(24f) + half, buttonY), half,
            ctx.Localize("os.aetherling_rename_cancel"), primary: false);

        if ((confirm || submitted) && !_busy && Changed())
        {
            SubmitRename();
        }
        if (cancel && !_busy)
        {
            _renameOpen = false;
            _error = null;
        }
    }

    private bool Changed() =>
        _nameBuffer.Trim().Length > 0
        && !string.Equals(_nameBuffer.Trim(), _core?.PetName ?? AetherlingLimits.DefaultName, StringComparison.Ordinal);

    /// <summary>The card for anyone who has no Name change yet: what it is, and the one tap to the shelf
    /// that sells it.</summary>
    private void DrawRenameOffer(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, 0.72f));

        var pad = Px(18f);
        var cardW = size.X - (pad * 2f);
        var cardH = Px(178f);
        var tl = new Vector2(origin.X + pad, origin.Y + ((size.Y - cardH) * 0.42f));
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.10f, 0.09f, 0.16f, 0.97f)), Px(16f));
        dl.AddRect(tl, br, Look.U32(Look.Crystal, 0.35f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        var y = tl.Y + Px(16f);
        Look.Centred(dl, ctx.Localize("os.aetherling_rename_need_title"), tl.X + (cardW * 0.5f), y,
            Look.U32(Look.CrystalPale), 1.15f);
        y += Px(32f);
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_rename_need_body"), tl.X + (cardW * 0.5f), y,
            cardW - Px(28f), Look.U32(Look.Whisper, 0.9f), 0.9f);

        var buttonY = br.Y - Px(46f);
        var half = (cardW - Px(38f)) * 0.5f;
        var toStore = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(14f), buttonY), half,
            ctx.Localize("os.aetherling_rename_need_store"), primary: true);
        var notNow = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(24f) + half, buttonY), half,
            ctx.Localize("os.aetherling_rename_need_later"), primary: false);

        if (toStore)
        {
            _renameOfferOpen = false;
            ctx.Shell.SendIntent("store", OsIntents.CreateStoreProduct(
                (short)StoreItemKind.AetherlingConsumable, AetherlingLimits.NameChangeRef));
        }
        if (notNow)
        {
            _renameOfferOpen = false;
        }
    }

    private void SubmitRename()
    {
        var name = _nameBuffer;
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.RenameAsync(name).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingRenamed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }
}
