using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Os;
using AetherLove.Services.Hub;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherLove.Windows;

/// <summary>Standalone diagnostic window opened by "/aos debuglumi": everything the Aetherling
/// systems know, listed. English-only on purpose, like the debug window it sits beside: a
/// support tool whose copyable text is read by whoever is helping. Read-only (the server owns
/// every fact shown here) except the dev shell override, which is session-local, client-only,
/// and never on the wire: nothing sells or grants a shell yet, so this is the one way to see
/// the drawn bodies.</summary>
public sealed class AetherlingDebugWindow : Window
{
    private readonly AetherlingHostService _host;
    private readonly AetherHubContext _hub;

    private volatile StoreInventoryItemDto[]? _inventory;
    private volatile string? _inventoryError;
    private bool _loading;
    private bool _emotePractice;
    private float _emoteAmplitude = 1f;
    private string _glyphElement = string.Empty;

    private static readonly Vector4 HeadingCol = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 DimCol = new(0.65f, 0.65f, 0.70f, 1f);
    private static readonly Vector4 OkCol = new(0.40f, 0.85f, 0.45f, 1f);

    private static readonly string[] ElementNames =
        ["None", "Fire", "Ice", "Wind", "Earth", "Lightning", "Water"];

    public AetherlingDebugWindow(AetherlingHostService host, AetherHubContext hub)
        : base("Aetherling Debug##aetherlingDebug")
    {
        _host = host;
        _hub = hub;
        Size = new Vector2(520, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 300),
            MaximumSize = new Vector2(4000, 4000),
        };
    }

    public override void OnOpen() => RefreshInventory();

    public override void Draw()
    {
        var core = _host.Snapshot;
        if (ImGui.Button("Refresh"))
        {
            _ = _host.RefreshAsync();
            RefreshInventory();
        }
        ImGui.SameLine();
        ImGui.TextColored(DimCol, core is null ? "no core" : $"stage {core.CoreStage}/{core.MaxStage}");

        DrawCore(core);
        DrawLook(core);
        DrawShellOverride(core);
        DrawEmotes();
        DrawGlyphs();
        DrawCards(core);
        DrawInventory();
    }

    /// <summary>Every choreography, playable on the spot. The lab forces the gates open, so this
    /// plays on a napping or mid-emote creature where a real trigger would decline: the point of
    /// the bench is to SEE the thing, and a bench that honours the rules can only show what the
    /// app was going to show anyway. The pet must be on screen to watch it, so open the app or
    /// the floating window first.</summary>
    private void DrawEmotes()
    {
        Heading("Emotes");
        if (_host.InteractLab is not { } lab)
        {
            ImGui.TextColored(DimCol, "The app has not started yet; open it once.");
            return;
        }

        ImGui.TextColored(DimCol, lab.Status);
        var column = 0;
        foreach (var def in AetherOS.PetKit.Engine.EmoteChoreographies.All)
        {
            if (column++ % 4 != 0)
            {
                ImGui.SameLine();
            }
            if (ImGui.Button($"{def.Name}##emote_{def.Key}", new Vector2(112f, 0f)))
            {
                lab.PlayEmote(def.Key, _emoteAmplitude);
            }
        }

        // The practice attempt is a different animation, not a quieter one: the same curves at
        // reduced excursion with an unsure mouth, which is what a pet still learning looks like.
        ImGui.Checkbox("Play as a practice attempt (60%)", ref _emotePractice);
        _emoteAmplitude = _emotePractice ? 0.6f : 1f;
    }

    /// <summary>The whole glyph library, drawn as itself and playable on the spot: a cell per glyph,
    /// grouped by register, hovering names it. The creature has to be on screen to say it, so open the
    /// app or the floating window first.</summary>
    private void DrawGlyphs()
    {
        Heading("Glyphs");
        if (_host.InteractLab is not { } lab)
        {
            ImGui.TextColored(DimCol, "The app has not started yet; open it once.");
            return;
        }

        ImGui.SetNextItemWidth(120f);
        ImGui.InputText("element (for element glyphs)", ref _glyphElement, 16);

        var dl = ImGui.GetWindowDrawList();
        foreach (AetherOS.PetKit.Engine.GlyphRegister register in Enum.GetValues<AetherOS.PetKit.Engine.GlyphRegister>())
        {
            ImGui.TextColored(DimCol, register.ToString());
            var column = 0;
            foreach (var shape in AetherOS.PetKit.Engine.GlyphShapes.All)
            {
                if (shape.Register != register)
                {
                    continue;
                }
                if (column++ % 8 != 0)
                {
                    ImGui.SameLine();
                }
                if (ImGui.Button($"##glyph_{shape.Name}", new Vector2(46f, 46f)))
                {
                    lab.ShowGlyph(shape.Name, null, _glyphElement);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(shape.Name);
                }
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                AetherOS.PetKit.Rendering.GlyphDraw.DrawIcon(dl, (min + max) * 0.5f, 34f, shape,
                    new Vector4(1f, 1f, 1f, 1f), GlyphSwatch(shape.Tint));
            }
        }
    }

    /// <summary>Stand-in fills for the bench: the pet's own accent is the creature's, and the bench draws
    /// glyphs with no creature in front of it.</summary>
    private static Vector4 GlyphSwatch(AetherOS.PetKit.Engine.GlyphTint tint) => tint switch
    {
        AetherOS.PetKit.Engine.GlyphTint.Accent => new Vector4(0.55f, 0.78f, 0.95f, 1f),
        AetherOS.PetKit.Engine.GlyphTint.Element => new Vector4(0.72f, 0.86f, 0.62f, 1f),
        AetherOS.PetKit.Engine.GlyphTint.Neutral => new Vector4(0.84f, 0.88f, 0.90f, 1f),
        _ => new Vector4(0f, 0f, 0f, 0f),
    };

    /// <summary>The drawn-shell folders under Media/unknown, beside the numeric growth rungs.
    /// One row per shell that ships a manifest; the trueform is the null override.</summary>
    private static readonly (string Folder, string Name)[] Shells =
    [
        ("jellyv1", "Bellow (jelly)"),
        ("crabv1", "Pinch (crab)"),
        ("pufferv1", "Bloat (puffer)"),
        ("nautilusv1", "Curl (nautilus)"),
        ("serpentv1", "Rattle (serpent)"),
        ("mothv1", "Flit (moth)"),
        ("lanternv1", "Lumen (lantern)"),
        ("spintopv1", "Whirl (spintop)"),
        ("pennantv1", "Furl (pennant)"),
        ("mufflev1", "Muffle"),
        ("chimev1", "Chime"),
        ("grumblev1", "Grumble"),
        ("smoulderv1", "Smoulder"),
    ];

    private static void DrawShellOverride(AetherlingDto? core)
    {
        Heading("Shell override (dev, session only)");
        if (core?.Adult is null)
        {
            ImGui.TextColored(DimCol, "Adult pets only.");
            return;
        }

        var current = AetherOS.PetKit.Engine.PetState.ShellOverride;
        ImGui.TextColored(DimCol, $"Active: {(string.IsNullOrEmpty(current) ? "trueform (Lumi)" : current)}");
        if (ImGui.Button("Trueform (Lumi)"))
        {
            AetherOS.PetKit.Engine.PetState.ShellOverride = null;
        }
        var column = 0;
        foreach (var (folder, name) in Shells)
        {
            if (column++ % 3 != 0)
            {
                ImGui.SameLine();
            }
            if (ImGui.Button($"{name}##shell_{folder}"))
            {
                AetherOS.PetKit.Engine.PetState.ShellOverride = folder;
            }
        }
    }

    private static void Heading(string text)
    {
        ImGui.Dummy(new Vector2(1f, 6f));
        ImGui.TextColored(HeadingCol, text);
        ImGui.Separator();
    }

    private void DrawCore(AetherlingDto? core)
    {
        Heading("Core");
        if (core is null)
        {
            ImGui.TextColored(DimCol, "Not adopted.");
            return;
        }

        ImGui.TextUnformatted($"Name: {core.PetName ?? "(unset)"}  chosen: {core.NameChosen}");
        ImGui.TextUnformatted($"Hatched: {core.HatchedAtUtc?.ToString("u") ?? "no"}");
        if (core.Growth is { } growth)
        {
            ImGui.TextUnformatted(
                $"GrowthFed: {growth.GrowthFed}  gate: {growth.FeedGateMinutes}m  last fed: {growth.LastFedAtUtc?.ToString("u") ?? "never"}");
        }
        if (core.Adult is { } adult)
        {
            var element = adult.Element >= 0 && adult.Element < ElementNames.Length
                ? ElementNames[adult.Element]
                : adult.Element.ToString();
            ImGui.TextColored(OkCol,
                $"Adult since {adult.AdultAtUtc:u}  element: {element}  feeds today: {adult.FeedsToday}/{adult.FeedsPerDay}");
            var diet = string.Join("  ", adult.Diet.Select(d =>
                $"{(d.Element >= 0 && d.Element < ElementNames.Length ? ElementNames[d.Element] : d.Element.ToString())}:{d.Count}"));
            ImGui.TextUnformatted($"Diet: {diet}  (turn at {adult.DietTurnThreshold})");
        }
        ImGui.TextUnformatted($"Onboarding done: {core.OnboardingDoneAtUtc?.ToString("u") ?? "no"}");
    }

    private static void DrawLook(AetherlingDto? core)
    {
        Heading("Look");
        if (core?.Look is not { } look)
        {
            ImGui.TextColored(DimCol, "None yet.");
            return;
        }
        ImGui.TextUnformatted($"Palette: {look.Palette}");
        ImGui.TextUnformatted($"Shell: {(look.Shell.Length > 0 ? look.Shell : "(trueform)")}");
        ImGui.TextUnformatted($"Reaction: {(look.Reaction.Length > 0 ? look.Reaction : "(base squish)")}");
        ImGui.TextUnformatted($"Arms follow job: {look.ArmsFollowJob}");
        ImGui.TextUnformatted(look.Accessories.Length > 0
            ? $"Worn: {string.Join(", ", look.Accessories)}"
            : "Worn: nothing");
    }

    private static void DrawCards(AetherlingDto? core)
    {
        Heading("Scratch cards");
        if (core?.Cards is not { Length: > 0 } cards)
        {
            ImGui.TextColored(DimCol, "None dealt.");
            return;
        }
        foreach (var card in cards.OrderBy(c => c.Slot))
        {
            var state = card.RevealedAtUtc is null
                ? "face down"
                : $"revealed {card.RevealedAtUtc:u}: {(StoreItemKind)card.PrizeKind} {string.Join(' ', card.PrizeRefs ?? [])}";
            ImGui.TextUnformatted($"Slot {card.Slot}: {state}");
        }
    }

    private void DrawInventory()
    {
        Heading("Store inventory (Aetherling kinds)");
        if (_inventoryError is { } error)
        {
            ImGui.TextColored(DimCol, error);
            return;
        }
        if (_inventory is not { } items)
        {
            ImGui.TextColored(DimCol, "Loading.");
            return;
        }

        var mine = items
            .Where(i => i.ItemKind is >= StoreItemKind.AetherlingPalette and <= StoreItemKind.AetherlingShell)
            .OrderBy(i => i.ItemKind)
            .ThenBy(i => i.ItemRef, StringComparer.Ordinal)
            .ToList();
        if (mine.Count == 0)
        {
            ImGui.TextColored(DimCol, "Nothing owned.");
            return;
        }
        foreach (var group in mine.GroupBy(i => i.ItemKind))
        {
            ImGui.TextColored(DimCol, group.Key.ToString());
            foreach (var item in group)
            {
                ImGui.TextUnformatted($"  {item.ItemRef}  x{item.Quantity}");
            }
        }
    }

    private void RefreshInventory()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        _inventoryError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                _inventory = await _hub.GetStoreInventoryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _inventoryError = ex.Message;
            }
            finally
            {
                _loading = false;
            }
        });
    }
}
