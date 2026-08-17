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
/// support tool whose copyable text is read by whoever is helping. Read-only; the server owns
/// every fact shown here.</summary>
public sealed class AetherlingDebugWindow : Window
{
    private readonly AetherlingHostService _host;
    private readonly AetherHubContext _hub;

    private volatile StoreInventoryItemDto[]? _inventory;
    private volatile string? _inventoryError;
    private bool _loading;

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
        DrawCards(core);
        DrawInventory();
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
