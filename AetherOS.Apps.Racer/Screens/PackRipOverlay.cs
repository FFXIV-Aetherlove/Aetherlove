using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Racing.Cards;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

internal sealed class PackRipOverlay(IRacerHost host, LumiRacePackDto pack, Action backToMain, Vector2? startAt = null, Vector2? startSize = null, string? artName = null, bool compact = false)
{
    // Every pack variant shares the same tear seam.
    private const float CrimpV = 0.215f;

    /// <summary>An item card's width over its height; a racing card face sits inside the same box.</summary>
    private const float PrizeCardAspect = 0.72f;

    /// <summary>How far a turning card has opened before its face is drawn.</summary>
    private const float FaceReadableShare = 0.6f;

    private const float RacingFaceShare = 0.94f;

    private const uint PrizePaper = 0xFFF2EDE2;
    private const uint CardShadow = 0x59000000u;
    private const uint PackShadow = 0x4C000000u;
    private const uint SleeveFallback = 0xFF6B4C9Au;
    private const uint CutGuideInk = 0x8CFFFFFFu;
    private const uint CutDoneInk = 0xE6FFFFFFu;
    private const uint ScissorsFill = 0xC81B1628u;
    private const uint TurnedBackFill = 0xFF7C5CDB;
    private const uint TurnedBackEdge = 0xFFB4AAF0;

    private static readonly Vector4 DefaultAccent = new(0.79f, 0.58f, 0.17f, 1f);
    private static readonly Vector4 CutHalo = new(1f, 0.95f, 0.7f, 1f);

    private sealed record Prize(string Name, Vector4 Accent, ISharedImmediateTexture? Art, StoreItemKind Kind);

    private readonly Prize?[] _prizes = new Prize?[2];
    private readonly Cards.CardFaceRenderer _faces = new();
    private readonly ConcurrentQueue<(int Slot, Prize Value)> _resolved = new();
    private bool _asked;
    private float _age;
    private float _tear;
    private bool _torn;
    private Task<LumiRacePackDto>? _revealTask;
    private LumiRacePackDto? _revealed;
    private string? _revealError;
    private float _flip;

    public bool Closed { get; private set; }

    public void Draw(OsAppContext ctx)
    {
        while (_resolved.TryDequeue(out var landed))
        {
            _prizes[landed.Slot] = landed.Value;
        }

        if (_revealed is null && _revealTask is { IsCompletedSuccessfully: true } revealed)
        {
            _revealed = revealed.Result;
        }

        if (!_asked)
        {
            _asked = true;
            Resolve(ctx.Capabilities.Storage("racer").Directory);
        }

        if (_revealTask is { IsFaulted: true } failed && _revealError is null)
        {
            _revealError = host.DescribeError(failed.Exception!.GetBaseException());
        }
        if (_revealTask is { IsCanceled: true } && _revealError is null)
        {
            _revealError = host.DescribeError(new TaskCanceledException());
        }

        var origin = ImGui.GetWindowPos();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##packOverlay", ImGui.GetWindowSize(), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        _age += ImGui.GetIO().DeltaTime;
        var dl = ImGui.GetWindowDrawList();
        var size = ImGui.GetWindowSize();
        GrandstandFrame.Panel(ctx, host, "navy", origin, size);
        GrandstandFrame.Sparkles(ctx, origin + new Vector2(Px(10)), size - new Vector2(Px(20)), 34);

        var (stageTopLeft, stageSize) = RacerCard.Stage(origin, size);
        if (compact || startAt.HasValue)
        {
            var height = MathF.Max(Px(60), size.Y - Px(112));
            stageSize = new Vector2(MathF.Min(size.X - Px(44), height * .74f), height);
            stageSize.Y = stageSize.X / .74f;
            stageTopLeft = origin + new Vector2((size.X - stageSize.X) / 2, Px(14));
            if (!ctx.ReduceMotion && _age < .35f && startSize.HasValue && startAt.HasValue)
            {
                var t = Math.Clamp(_age / .35f, 0, 1);
                t = 1 - MathF.Pow(1 - t, 3);
                stageTopLeft = Vector2.Lerp(startAt.Value, stageTopLeft, t);
                stageSize = Vector2.Lerp(startSize.Value, stageSize, t);
            }
        }
        if (!_torn)
        {
            DrawFoil(ctx, dl, stageTopLeft, stageSize);
        }
        else
        {
            DrawCards(ctx, dl, stageTopLeft, stageSize);
        }

        DrawLeave(ctx, dl, origin, size);
    }

    private void DrawFoil(OsAppContext ctx, ImDrawListPtr dl, Vector2 packPos, Vector2 packSize)
    {
        var tearY = packPos.Y + (packSize.Y * CrimpV);
        var art = ctx.Capabilities.Textures.Get(Path.Combine(host.PetAssetRoot, "racer", (artName ?? PackArtwork.For(pack.PackId)) + ".png"));

        dl.AddRectFilled(packPos + new Vector2(Px(6), packSize.Y - Px(2)),
            packPos + new Vector2(packSize.X - Px(6), packSize.Y + Px(10)), PackShadow, Px(10));

        if (art is { } tex)
        {
            dl.AddImage(tex, new Vector2(packPos.X, tearY), packPos + packSize,
                new Vector2(0f, CrimpV), Vector2.One, 0xFFFFFFFF);

            // The strip left of the cut lifts and tilts away; the rest stays crimped on the sleeve.
            if (_tear < 1f)
            {
                dl.AddImage(tex, new Vector2(packPos.X + (packSize.X * _tear), packPos.Y),
                    new Vector2(packPos.X + packSize.X, tearY),
                    new Vector2(_tear, 0f), new Vector2(1f, CrimpV), 0xFFFFFFFF);
            }
            if (_tear > 0f)
            {
                var lift = Px(14) * _tear;
                var tilt = Px(20) * _tear;
                dl.AddImageQuad(tex,
                    new Vector2(packPos.X, packPos.Y - lift - tilt),
                    new Vector2(packPos.X + (packSize.X * _tear), packPos.Y - lift),
                    new Vector2(packPos.X + (packSize.X * _tear), tearY - lift),
                    new Vector2(packPos.X, tearY - lift - tilt),
                    new Vector2(0f, 0f), new Vector2(_tear, 0f),
                    new Vector2(_tear, CrimpV), new Vector2(0f, CrimpV),
                    0xFFFFFFFF);
            }
        }
        else
        {
            dl.AddRectFilled(packPos, packPos + packSize, SleeveFallback, Px(8));
        }

        DrawCutGuide(ctx, dl, packPos, packSize, tearY);

        ImGui.SetCursorScreenPos(new Vector2(packPos.X, tearY - Px(22)));
        ImGui.InvisibleButton("##tearStrip", new Vector2(packSize.X, Px(46)));
        HandOnHover();
        if (ImGui.IsItemActive() && (startAt is null || ctx.ReduceMotion || _age >= .35f))
        {
            var progress = (ImGui.GetMousePos().X - packPos.X) / packSize.X;
            _tear = MathF.Max(_tear, Math.Clamp(progress, 0f, 1f));
        }
        if (_tear >= 0.95f)
        {
            _torn = true;
            SendReveal();
        }
    }

    /// <summary>The dashed line the sleeve is cut along, and the scissors that walk it. The scissors show
    /// the gesture rather than describing it, and they sit ON the cut once the drag starts.</summary>
    private void DrawCutGuide(OsAppContext ctx, ImDrawListPtr dl, Vector2 packPos, Vector2 packSize, float tearY)
    {
        var left = packPos.X;
        var right = packPos.X + packSize.X;
        var dash = Px(9f);

        for (var x = left; x < right; x += dash * 2f)
        {
            var to = MathF.Min(x + dash, right);
            if (x >= left + (packSize.X * _tear))
            {
                dl.AddLine(new Vector2(x, tearY), new Vector2(to, tearY), CutGuideInk, Px(1.8f));
            }
        }

        if (_tear > 0f)
        {
            var cut = left + (packSize.X * _tear);
            dl.AddLine(new Vector2(left, tearY), new Vector2(cut, tearY), CutDoneInk, Px(2.5f));
            RacerChrome.Halo(dl, new Vector2(cut, tearY), Px(22), CutHalo, 0.55f, 3);
        }

        // Before the first drag the scissors slide the line on a loop, so the gesture is shown.
        var walk = _tear > 0f
            ? _tear
            : (ctx.ReduceMotion ? 0.5f : ((_age * 0.55f) % 1.45f) / 1f);
        if (walk <= 1f)
        {
            var at = new Vector2(left + (packSize.X * walk), tearY);
            dl.AddCircleFilled(at, Px(15f), ScissorsFill, 20);
            dl.AddCircle(at, Px(15f), CutGuideInk, 20, Px(1.4f));
            AetherLove.UI.IconDraw.AddCentered(dl, FontAwesomeIcon.Cut, Px(15f), at, 0xFFFFFFFF);
        }

        var hint = ctx.Localize("os.racer_pack_tear");
        GrandstandFrame.WrappedLabel(ctx, hint,
            new Vector2(ImGui.GetWindowPos().X + Px(14), packPos.Y + packSize.Y + Px(8)),
            new Vector2(ImGui.GetWindowSize().X - Px(28), Px(34)), GrandstandFrame.Cream, RacerTextSize.Caption);
    }

    private enum RipItem
    {
        Prize,
        Card,
    }

    /// <summary>The cards the reveal reply dealt, or the pack's own while the reply is on its way.</summary>
    private LumiRacePackCardDto[] DealtCards() => _revealed?.Cards ?? pack.Cards ?? [];

    /// <summary>The pack's contents in reveal order: the first item prize, the second when the pack has one,
    /// then every racing card. Empty for a pack dealt before cards (size 0), which keeps the older two-prize
    /// reveal.</summary>
    private (RipItem Kind, int Index)[] Items()
    {
        var size = _revealed?.Size ?? pack.Size;
        if (size <= 0)
        {
            return [];
        }

        var items = new List<(RipItem Kind, int Index)> { (RipItem.Prize, 0) };
        if (pack.PrizeKind2 != 0 || pack.PrizeRef2.Length > 0)
        {
            items.Add((RipItem.Prize, 1));
        }

        var cards = DealtCards();
        for (var i = 0; i < cards.Length; i++)
        {
            items.Add((RipItem.Card, i));
        }

        return items.ToArray();
    }

    private void DrawCards(OsAppContext ctx, ImDrawListPtr dl, Vector2 stagePos, Vector2 stageSize)
    {
        var items = Items();
        var count = Math.Max(2, items.Length);
        _flip = MathF.Min(1f, _flip + (ImGui.GetIO().DeltaTime * (ctx.ReduceMotion ? 10f : 2.2f) * (2f / count)));

        GrandstandFrame.Label(ctx, ctx.Localize("os.racer_pack_title"),
            new Vector2(ImGui.GetWindowPos().X + Px(20), stagePos.Y + Px(10)),
            new Vector2(ImGui.GetWindowSize().X - Px(40), Px(32)), GrandstandFrame.Cream, RacerTextSize.Heading);

        if (items.Length == 0)
        {
            DrawTwoPrizes(ctx, dl, stagePos, stageSize);
            return;
        }

        var columns = items.Length <= 3 ? items.Length : 2;
        var rows = (items.Length + columns - 1) / columns;
        var labelH = Px(46);
        var top = stagePos.Y + Px(46);
        var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - Px(62);
        var gap = Px(10);
        var widthLimit = (stageSize.X - (gap * (columns - 1))) / columns;
        var heightLimit = ((bottom - top) - (rows * labelH) - (gap * (rows - 1))) / rows * PrizeCardAspect;
        var cardW = MathF.Max(Px(40), MathF.Min(widthLimit, heightLimit));
        var cardSize = new Vector2(cardW, cardW / PrizeCardAspect);
        var rowPitch = cardSize.Y + labelH + gap;
        var packReady = Cards.CardArt.PackReady(ctx);
        var dealt = DealtCards();
        for (var i = 0; i < items.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var inRow = Math.Min(columns, items.Length - (row * columns));
            var rowWidth = (inRow * cardSize.X) + ((inRow - 1) * gap);
            var centreX = stagePos.X + ((stageSize.X - rowWidth) * 0.5f) + (column * (cardSize.X + gap)) + (cardSize.X * 0.5f);
            var cardTop = top + (row * rowPitch);
            var reveal = Math.Clamp((_flip * items.Length) - i, 0f, 1f);
            var (kind, index) = items[i];
            if (kind == RipItem.Card)
            {
                DrawRacingCard(ctx, dl, dealt[index], i, reveal, centreX, cardTop, cardSize, packReady);
            }
            else
            {
                DrawPrizeCard(ctx, dl, index, reveal, centreX, cardTop, cardSize, ctx.Localize(PrizeLabelKey(index)));
            }
        }
    }

    /// <summary>What an item prize is, read from its kind: the server falls back to another pool or a crystal
    /// when an accessory or a colour has run out, so the slot alone does not say.</summary>
    private string PrizeLabelKey(int slot)
    {
        var kind = (StoreItemKind)(slot == 0 ? pack.PrizeKind1 : pack.PrizeKind2);
        return kind switch
        {
            StoreItemKind.AetherlingAccessory or StoreItemKind.AetherlingArms => "os.racer_pack_accessory",
            StoreItemKind.AetherlingPalette => "os.racer_pack_colour",
            _ => "os.racer_pack_prize_store",
        };
    }

    /// <summary>The reveal a pack dealt before cards keeps: the accessory and the colour, side by side.</summary>
    private void DrawTwoPrizes(OsAppContext ctx, ImDrawListPtr dl, Vector2 stagePos, Vector2 stageSize)
    {
        var cardSize = new Vector2(stageSize.X * 0.42f, stageSize.X * 0.42f / PrizeCardAspect);
        var top = stagePos.Y + ((stageSize.Y - cardSize.Y) * 0.42f);
        for (var i = 0; i < 2; i++)
        {
            var reveal = Math.Clamp((_flip * 2f) - i, 0f, 1f);
            var centreX = stagePos.X + (stageSize.X * (i == 0 ? 0.27f : 0.73f));
            var label = ctx.Localize(i == 0 ? "os.racer_pack_prize_race" : "os.racer_pack_prize_store");
            DrawPrizeCard(ctx, dl, i, reveal, centreX, top, cardSize, label);
        }
    }

    /// <summary>One item card mid-turn: the back while it faces away, the paper face with the item's art and
    /// name once it has turned far enough to read.</summary>
    private void DrawPrizeCard(OsAppContext ctx, ImDrawListPtr dl, int slot, float reveal, float centreX, float top, Vector2 cardSize, string label)
    {
        var width = cardSize.X * MathF.Abs((reveal * 2f) - 1f);
        var a = new Vector2(centreX - (width * 0.5f), top);
        var b = new Vector2(centreX + (width * 0.5f), top + cardSize.Y);
        dl.AddRectFilled(a + new Vector2(0f, Px(4)), b + new Vector2(0f, Px(4)), CardShadow, Px(8));
        if (reveal <= 0.5f)
        {
            DrawTurnedBack(dl, a, b);
            return;
        }

        var prize = _prizes[slot];
        var accent = prize?.Accent ?? DefaultAccent;
        dl.AddRectFilled(a, b, PrizePaper, Px(8));
        dl.AddRect(a, b, ImGui.ColorConvertFloat4ToU32(accent), Px(8),
            ImDrawFlags.RoundCornersAll, Px(2.2f));
        if (width < cardSize.X * FaceReadableShare)
        {
            return;
        }

        var artBox = new Vector2(cardSize.X - Px(18), cardSize.Y - Px(46));
        var artTop = new Vector2(centreX - (artBox.X * 0.5f), top + Px(10));
        if (prize?.Art?.GetWrapOrDefault() is { } wrap)
        {
            // These renders put the creature small in a lot of transparent room, so the whole canvas
            // fitted into a card is a speck with a wide empty margin. The store's own windows, measured
            // off the same art, crop to the part that carries the item.
            var (uv0, uv1) = StoreArtCrop.PetCardUv(
                prize.Kind, wrap.Width, wrap.Height, artBox.X, artBox.Y);
            dl.AddImage(wrap.Handle, artTop, artTop + artBox, uv0, uv1);
        }
        else
        {
            var mid = artTop + (artBox * 0.5f);
            var disc = MathF.Min(artBox.X, artBox.Y) * 0.34f;
            dl.AddCircleFilled(mid, disc, ImGui.ColorConvertFloat4ToU32(accent with { W = 0.25f }), 28);
            dl.AddCircle(mid, disc, ImGui.ColorConvertFloat4ToU32(accent), 28, Px(1.6f));
        }

        var name = prize?.Name ?? Prettify(slot == 0 ? pack.PrizeRef1 : pack.PrizeRef2);
        GrandstandFrame.WrappedLabel(ctx, name,
            new Vector2(centreX - cardSize.X / 2 + Px(6), top + cardSize.Y - Px(36)),
            new Vector2(cardSize.X - Px(12), Px(32)), GrandstandFrame.Ink, RacerTextSize.Caption);
        GrandstandFrame.WrappedLabel(ctx, label,
            new Vector2(centreX - cardSize.X / 2, top + cardSize.Y + Px(10)),
            new Vector2(cardSize.X, Px(42)), GrandstandFrame.Cream, RacerTextSize.Caption);
    }

    /// <summary>A racing card mid-turn: the back while it faces away, then its own framed face at the level
    /// the pack left it, captioned with what the pack did.</summary>
    private void DrawRacingCard(OsAppContext ctx, ImDrawListPtr dl, LumiRacePackCardDto dealt, int slot, float reveal, float centreX, float top, Vector2 cardSize, bool packReady)
    {
        var width = cardSize.X * MathF.Abs((reveal * 2f) - 1f);
        var a = new Vector2(centreX - (width * 0.5f), top);
        var b = new Vector2(centreX + (width * 0.5f), top + cardSize.Y);
        dl.AddRectFilled(a + new Vector2(0f, Px(4)), b + new Vector2(0f, Px(4)), CardShadow, Px(8));
        if (reveal <= 0.5f)
        {
            DrawTurnedBack(dl, a, b);
            return;
        }

        var card = RaceCardCatalogue.Find(dealt.CardId);
        if (width < cardSize.X * FaceReadableShare || card is null)
        {
            dl.AddRectFilled(a, b, PrizePaper, Px(8));
            return;
        }

        var faceW = cardSize.X * RacingFaceShare;
        var faceSize = new Vector2(faceW, faceW * Cards.CardFaceLayout.Ratio);
        var faceAt = new Vector2(centreX - (faceW * 0.5f), top + ((cardSize.Y - faceSize.Y) * 0.5f));
        var hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(faceAt, faceAt + faceSize);
        _faces.Draw(ctx, dl, "rip" + slot, card, RaceCardLevels.Clamp(dealt.LevelAfter), faceAt, faceSize, false, hovered, packReady);
        if (hovered)
        {
            Cards.CardChrome.Tooltip(Cards.CardStrings.NameOf(ctx, card), Cards.CardStrings.TypeLine(ctx, card));
        }

        var caption = dealt.LevelAfter <= RaceCardLevels.MinLevel
            ? ctx.Localize("os.racer_card_reveal_new")
            : string.Format(ctx.Localize("os.racer_card_reveal_level_up"), dealt.LevelAfter);
        GrandstandFrame.WrappedLabel(ctx, caption,
            new Vector2(centreX - cardSize.X / 2, top + cardSize.Y + Px(6)),
            new Vector2(cardSize.X, Px(42)), GrandstandFrame.Cream, RacerTextSize.Caption);
    }

    private static void DrawTurnedBack(ImDrawListPtr dl, Vector2 a, Vector2 b)
    {
        dl.AddRectFilled(a, b, TurnedBackFill, Px(8));
        dl.AddRect(a, b, TurnedBackEdge, Px(8), ImDrawFlags.RoundCornersAll, Px(1.8f));
    }

    private void DrawLeave(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        if (_torn && _flip < 1f)
        {
            return;
        }

        var width = MathF.Min(Px(200), size.X - Px(56));
        var height = Px(36);
        var at = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - Px(52));
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton("##packDone", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var br = at + new Vector2(width, height);
        dl.AddRectFilled(at, br, hovered ? Cards.CardChrome.ChipFillHover : Cards.CardChrome.ChipFill, Px(6));
        dl.AddRect(at, br, hovered ? GrandstandFrame.Gold : Cards.CardChrome.ChipEdge, Px(6), ImDrawFlags.None, Px(1));

        if (_revealTask is { IsCompleted: false })
        {
            GrandstandFrame.Label(ctx, ctx.Localize("os.racer_loading"), at, new Vector2(width, height), GrandstandFrame.Cream, RacerTextSize.Caption);
            return;
        }
        if (_revealError is { } error)
        {
            GrandstandFrame.Label(ctx, error, at - new Vector2(0, Px(32)), new Vector2(width, Px(28)), GrandstandFrame.Cream);
            GrandstandFrame.Label(ctx, ctx.Localize("os.racer_cup_retry"), at, new Vector2(width, height), GrandstandFrame.Cream, RacerTextSize.Caption);
            if (pressed)
            {
                _revealTask = null;
                SendReveal();
            }
            return;
        }

        var label = ctx.Localize(compact || startAt.HasValue ? "os.racer_packs_back" : "os.racer_back_main");
        IconDraw.AddCentered(dl, FontAwesomeIcon.ArrowLeft, Px(12), at + new Vector2(Px(20), height / 2), GrandstandFrame.Gold);
        GrandstandFrame.Label(ctx, label, at + new Vector2(Px(34), 0), new Vector2(width - Px(46), height), GrandstandFrame.Cream, RacerTextSize.Caption);

        if (pressed)
        {
            Closed = true;
            backToMain();
        }
    }

    /// <summary>Asks the racing service what the two prizes look like. Results are parked on a queue and
    /// drained on the draw thread; nothing here may touch screen state.</summary>
    private void Resolve(string cacheDir)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var prizes = await host.GetPackPrizesAsync(pack.PackId).ConfigureAwait(false);
                for (var slot = 0; slot < prizes.Length && slot < 2; slot++)
                {
                    var prize = prizes[slot];
                    ISharedImmediateTexture? art = null;
                    if (prize.HasImage)
                    {
                        var bytes = await host.GetStoreProductImageAsync(prize.ProductId).ConfigureAwait(false);
                        art = AvatarDiskCache.Store(cacheDir, $"prize{prize.ProductId:N}", bytes ?? []);
                    }

                    var name = prize.NameEnglish.Length == 0 ? Prettify(prize.Ref) : NameOf(prize);
                    _resolved.Enqueue((slot,
                        new Prize(name, AccentOf(prize.AccentColor), art, (StoreItemKind)prize.Kind)));
                }
            }
            catch (Exception)
            {
                // The pack still opens on its item refs; the art is a nicety, not the prize.
            }
        });
    }

    /// <summary>The store stores an accent as 0xAARRGGBB; every draw list here wants ABGR.</summary>
    private static Vector4 AccentOf(uint accentColor) => accentColor == 0
        ? DefaultAccent
        : new Vector4(
            ((accentColor >> 16) & 0xFF) / 255f,
            ((accentColor >> 8) & 0xFF) / 255f,
            (accentColor & 0xFF) / 255f,
            1f);

    private static string NameOf(LumiRacePrizeDto p)
    {
        var lang = Enum.TryParse<Language>(UiHost.Configuration.PluginLanguage, ignoreCase: true, out var parsed)
            ? parsed
            : Language.English;
        var name = lang switch
        {
            Language.Spanish => p.NameSpanish,
            Language.French => p.NameFrench,
            Language.Russian => p.NameRussian,
            Language.German => p.NameGerman,
            Language.Portuguese => p.NamePortuguese,
            _ => p.NameEnglish,
        };
        return string.IsNullOrWhiteSpace(name) ? p.NameEnglish : name!;
    }

    private void SendReveal()
    {
        if (_revealTask is not null)
        {
            return;
        }
        _revealError = null;
        try
        {
            _revealTask = host.RevealPackAsync(pack.PackId);
        }
        catch (Exception ex)
        {
            _revealTask = Task.FromException<LumiRacePackDto>(ex);
        }
    }

    private static string Prettify(string itemRef)
    {
        var words = itemRef.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            }
        }
        return string.Join(' ', words);
    }
}
