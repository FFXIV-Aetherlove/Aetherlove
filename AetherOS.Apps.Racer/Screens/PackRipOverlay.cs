using System;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The booster pack, full size and in the card's own place. The crimp carries a cut guide the
/// player drags across; the torn strip lifts away and the two prizes turn face up wearing their own
/// store art. The prizes were granted when the card completed; the rip only tells the server it was
/// seen.</summary>
internal sealed class PackRipOverlay(IRacerHost host, LumiRacePackDto pack, Action backToMain)
{
    /// <summary>Where the crimped top meets the printed body, as a fraction of the sleeve's height. The
    /// cut runs along that crease, so the strip that lifts away is the one the artist drew. Measured off
    /// the art: saturation jumps from about 35 to over 100 at this row.</summary>
    private const float CrimpV = 0.215f;

    private static readonly Vector4 DefaultAccent = new(0.79f, 0.58f, 0.17f, 1f);

    private sealed record Prize(string Name, Vector4 Accent, ISharedImmediateTexture? Art, StoreItemKind Kind);

    private readonly Prize?[] _prizes = new Prize?[2];
    private readonly ConcurrentQueue<(int Slot, Prize Value)> _resolved = new();
    private bool _asked;
    private float _age;
    private float _tear;
    private bool _torn;
    private bool _revealSent;
    private float _flip;

    public bool Closed { get; private set; }

    public void Draw(OsAppContext ctx)
    {
        while (_resolved.TryDequeue(out var landed))
        {
            _prizes[landed.Slot] = landed.Value;
        }

        if (!_asked)
        {
            _asked = true;
            Resolve(ctx.Capabilities.Storage("racer").Directory);
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
        dl.AddRectFilled(origin, origin + size, 0xB8000000);

        var (stageTopLeft, stageSize) = RacerCard.Stage(origin, size);
        if (!_torn)
        {
            DrawFoil(ctx, dl, stageTopLeft, stageSize);
        }
        else
        {
            DrawCards(ctx, dl, stageTopLeft, stageSize);
        }

        // An unopened sleeve still needs a way out: it stays pending and the card page offers it again.
        DrawLeave(ctx, dl, origin, size);
    }

    private void DrawFoil(OsAppContext ctx, ImDrawListPtr dl, Vector2 packPos, Vector2 packSize)
    {
        var tearY = packPos.Y + (packSize.Y * CrimpV);
        var art = ctx.Capabilities.Textures.Get(Path.Combine(host.PetAssetRoot, "racer", "foil-pack.png"));

        dl.AddRectFilled(packPos + new Vector2(Px(6), packSize.Y - Px(2)),
            packPos + new Vector2(packSize.X - Px(6), packSize.Y + Px(10)), 0x4C000000u, Px(10));

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
            dl.AddRectFilled(packPos, packPos + packSize, 0xFF6B4C9Au, Px(8));
        }

        DrawCutGuide(ctx, dl, packPos, packSize, tearY);

        ImGui.SetCursorScreenPos(new Vector2(packPos.X, tearY - Px(22)));
        ImGui.InvisibleButton("##tearStrip", new Vector2(packSize.X, Px(46)));
        HandOnHover();
        if (ImGui.IsItemActive())
        {
            var progress = (ImGui.GetMousePos().X - packPos.X) / packSize.X;
            _tear = MathF.Max(_tear, Math.Clamp(progress, 0f, 1f));
        }
        if (_tear >= 0.98f)
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
                dl.AddLine(new Vector2(x, tearY), new Vector2(to, tearY), 0x8CFFFFFFu, Px(1.8f));
            }
        }

        if (_tear > 0f)
        {
            var cut = left + (packSize.X * _tear);
            dl.AddLine(new Vector2(left, tearY), new Vector2(cut, tearY), 0xE6FFFFFFu, Px(2.5f));
            RacerChrome.Halo(dl, new Vector2(cut, tearY), Px(22), new Vector4(1f, 0.95f, 0.7f, 1f), 0.55f, 3);
        }

        // Before the first drag the scissors slide the line on a loop, so the gesture is shown.
        var walk = _tear > 0f
            ? _tear
            : (ctx.ReduceMotion ? 0.5f : ((_age * 0.55f) % 1.45f) / 1f);
        if (walk <= 1f)
        {
            var at = new Vector2(left + (packSize.X * walk), tearY);
            dl.AddCircleFilled(at, Px(15f), 0xC81B1628u, 20);
            dl.AddCircle(at, Px(15f), 0x8CFFFFFFu, 20, Px(1.4f));
            AetherLove.UI.IconDraw.AddCentered(dl, FontAwesomeIcon.Cut, Px(15f), at, 0xFFFFFFFF);
        }

        var hint = ctx.Localize("os.racer_pack_tear");
        var hintSize = ImGui.CalcTextSize(hint);
        dl.AddText(new Vector2(packPos.X + ((packSize.X - hintSize.X) * 0.5f), packPos.Y + packSize.Y + Px(18)),
            0xFFE6E0F5, hint);
    }

    private void DrawCards(OsAppContext ctx, ImDrawListPtr dl, Vector2 stagePos, Vector2 stageSize)
    {
        _flip = MathF.Min(1f, _flip + (ImGui.GetIO().DeltaTime * (ctx.ReduceMotion ? 10f : 2.2f)));

        var cardSize = new Vector2(stageSize.X * 0.42f, stageSize.X * 0.42f / 0.72f);
        var top = stagePos.Y + ((stageSize.Y - cardSize.Y) * 0.42f);
        Span<string> labels =
        [
            ctx.Localize("os.racer_pack_prize_race"),
            ctx.Localize("os.racer_pack_prize_store"),
        ];
        Span<string> refs = [pack.PrizeRef1, pack.PrizeRef2];

        var title = ctx.Localize("os.racer_pack_title");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(new Vector2(stagePos.X + ((stageSize.X - titleSize.X) * 0.5f), stagePos.Y + Px(10)),
                0xFFFFFFFF, title);
        }

        for (var i = 0; i < 2; i++)
        {
            var reveal = Math.Clamp((_flip * 2f) - i, 0f, 1f);
            var width = cardSize.X * MathF.Abs((reveal * 2f) - 1f);
            var centreX = stagePos.X + (stageSize.X * (i == 0 ? 0.27f : 0.73f));
            var a = new Vector2(centreX - (width * 0.5f), top);
            var b = new Vector2(centreX + (width * 0.5f), top + cardSize.Y);
            var faceUp = reveal > 0.5f;

            dl.AddRectFilled(a + new Vector2(0f, Px(4)), b + new Vector2(0f, Px(4)), 0x59000000u, Px(8));
            if (!faceUp)
            {
                dl.AddRectFilled(a, b, 0xFF7C5CDB, Px(8));
                dl.AddRect(a, b, 0xFFB4AAF0, Px(8), ImDrawFlags.RoundCornersAll, Px(1.8f));
                continue;
            }

            var prize = _prizes[i];
            var accent = prize?.Accent ?? DefaultAccent;
            dl.AddRectFilled(a, b, 0xFFF2EDE2, Px(8));
            dl.AddRect(a, b, ImGui.ColorConvertFloat4ToU32(accent), Px(8),
                ImDrawFlags.RoundCornersAll, Px(2.2f));
            if (width < cardSize.X * 0.6f)
            {
                continue;
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

            var name = prize?.Name ?? Prettify(refs[i]);
            DrawFitted(dl, name, new Vector2(centreX, top + cardSize.Y - Px(32)), cardSize.X - Px(12), 0xFF241E32);
            var label = labels[i];
            var labelSize = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(centreX - (labelSize.X * 0.5f), top + cardSize.Y + Px(10)), 0xFFB4AACC, label);
        }
    }

    private void DrawLeave(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        if (_torn && _flip < 1f)
        {
            return;
        }

        var width = MathF.Min(Px(240), size.X - Px(56));
        var height = Px(42);
        var at = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - Px(58));
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton("##packDone", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var br = at + new Vector2(width, height);
        var fill = RacerChrome.DutchBlue with { W = hovered ? 1f : 0.92f };
        dl.AddRectFilled(at + new Vector2(0f, Px(3)), br + new Vector2(0f, Px(3)), 0x66000000u, height * 0.3f);
        dl.AddRectFilled(at, br, ImGui.ColorConvertFloat4ToU32(fill), height * 0.3f);
        dl.AddRect(at, br, hovered ? 0xFFFFFFFFu : 0x8CFFFFFFu, height * 0.3f,
            ImDrawFlags.RoundCornersAll, Px(1.6f));

        var label = ctx.Localize("os.racer_back_main");
        var text = ImGui.CalcTextSize(label);
        dl.AddText(at + ((new Vector2(width, height) - text) * 0.5f), 0xFFFFFFFF, label);

        if (pressed)
        {
            Closed = true;
            backToMain();
        }
    }

    /// <summary>A prize name centred on a card, shrunk to fit rather than clipped, because store copy is
    /// written for a shelf and not for a small card.</summary>
    private static void DrawFitted(ImDrawListPtr dl, string text, Vector2 centre, float room, uint ink)
    {
        var wide = ImGui.CalcTextSize(text).X;
        var scale = wide > room ? MathF.Max(0.62f, room / wide) : 1f;
        var drawn = ImGui.CalcTextSize(text) * scale;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            new Vector2(centre.X - (drawn.X * 0.5f), centre.Y), ink, text);
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
        if (_revealSent)
        {
            return;
        }
        _revealSent = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await host.RevealPackAsync(pack.PackId).ConfigureAwait(false);
            }
            catch
            {
                // The grant already happened at completion; a lost reveal stamp costs nothing and the
                // pack simply offers itself again next visit.
            }
        });
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
