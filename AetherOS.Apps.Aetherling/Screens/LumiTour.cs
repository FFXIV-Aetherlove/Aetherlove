using System;
using System.Globalization;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The creature's tour: every page in the bar and every thing on it, each shown on the real page
/// with a ring around what is being explained, on the same engine the home screen's tour runs on. It
/// never asks the player to do anything; the scrim makes the app inert until it is over. Played once
/// after the birth gifts, once more for every owner when its script changes, and from the settings
/// page whenever they like.</summary>
internal sealed class LumiTour
{
    /// <summary>Bumped whenever the script changes enough that an owner who saw the old one should see
    /// this one. The stored stamp carries it, so the app replays the tour once per creature per revision.</summary>
    public const int Revision = 2;

    /// <summary>Everything the script reaches into the app for: where to go and where things were drawn.</summary>
    internal sealed class Surfaces
    {
        public required Action<PetNavAction> Navigate { get; init; }

        public required Func<PetNavAction, (Vector2 TL, Vector2 BR)?> NavRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> PetRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> WheelRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> FoodSlotsRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> BasketRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> UnlockRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> FirstGameRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> FirstTrophyRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> PaletteLaneRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> SlotsRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> EmotesHeadingRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> ReactionsHeadingRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> ShopPillRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> ElementRowRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> RadarRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> ShowOutsideRect { get; init; }

        public required Func<(Vector2 TL, Vector2 BR)?> TourRowRect { get; init; }

        public required Func<bool> EmotesAvailable { get; init; }
    }

    private const float PettingLoopSeconds = 3.0f;

    private static readonly TourStep<LumiTour>[] Script =
    [
        new(FontAwesomeIcon.Heart, "os.aetherling_tour_welcome_title", "os.aetherling_tour_welcome_body",
            Panel: TourAnchor.Center, Enter: t => t.Go(PetNavAction.Home)),

        new(FontAwesomeIcon.HandHoldingHeart, "os.aetherling_tour_petting_title", "os.aetherling_tour_petting_body",
            t => t._surfaces.PetRect(), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Home),
            Demo: (t, dl) => t.DemoPetting(dl)),

        new(FontAwesomeIcon.CircleNotch, "os.aetherling_tour_wheel_title", "os.aetherling_tour_wheel_body",
            t => t._surfaces.WheelRect(), Panel: TourAnchor.Bottom,
            Enter: t => t.Go(PetNavAction.Home),
            Available: t => t._surfaces.WheelRect() is not null),

        new(FontAwesomeIcon.Utensils, "os.aetherling_tour_hunger_title", "os.aetherling_tour_hunger_body",
            t => t._surfaces.FoodSlotsRect() ?? t._surfaces.PetRect(), Panel: TourAnchor.Bottom, Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Home)),

        new(FontAwesomeIcon.Gem, "os.aetherling_tour_feeding_title", "os.aetherling_tour_feeding_body",
            t => t._surfaces.BasketRect(), Panel: TourAnchor.Top,
            Enter: t => t.Go(PetNavAction.Home)),

        new(FontAwesomeIcon.Unlock, "os.aetherling_tour_unlocks_title", "os.aetherling_tour_unlocks_body",
            t => t._surfaces.UnlockRect() ?? t._surfaces.BasketRect(), Panel: TourAnchor.Top,
            Enter: t => t.Go(PetNavAction.Home)),

        new(FontAwesomeIcon.Gamepad, "os.aetherling_tour_games_title", "os.aetherling_tour_games_body",
            t => t._surfaces.FirstGameRect() ?? t._surfaces.NavRect(PetNavAction.Games), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Games)),

        new(FontAwesomeIcon.Trophy, "os.aetherling_tour_scores_title", "os.aetherling_tour_scores_body",
            t => t._surfaces.FirstTrophyRect() ?? t._surfaces.NavRect(PetNavAction.Games), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Games)),

        new(FontAwesomeIcon.Palette, "os.aetherling_tour_palette_title", "os.aetherling_tour_palette_body",
            t => t._surfaces.PaletteLaneRect() ?? t._surfaces.NavRect(PetNavAction.Wardrobe), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Wardrobe)),

        new(FontAwesomeIcon.HatWizard, "os.aetherling_tour_dressup_title", "os.aetherling_tour_dressup_body",
            t => t._surfaces.SlotsRect() ?? t._surfaces.NavRect(PetNavAction.Wardrobe), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Wardrobe)),

        new(FontAwesomeIcon.ShoppingBag, "os.aetherling_tour_store_title", "os.aetherling_tour_store_body",
            t => t._surfaces.ShopPillRect() ?? t._surfaces.NavRect(PetNavAction.Wardrobe), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Wardrobe)),

        new(FontAwesomeIcon.Star, "os.aetherling_tour_reactions_title", "os.aetherling_tour_reactions_body",
            t => t._surfaces.ReactionsHeadingRect() ?? t._surfaces.NavRect(PetNavAction.Emotes), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Emotes),
            Available: t => t._surfaces.EmotesAvailable()),

        new(FontAwesomeIcon.TheaterMasks, "os.aetherling_tour_emotes_title", "os.aetherling_tour_emotes_body",
            t => t._surfaces.EmotesHeadingRect() ?? t._surfaces.NavRect(PetNavAction.Emotes), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Emotes),
            Available: t => t._surfaces.EmotesAvailable()),

        new(FontAwesomeIcon.Bolt, "os.aetherling_tour_element_title", "os.aetherling_tour_element_body",
            t => t._surfaces.ElementRowRect() ?? t._surfaces.NavRect(PetNavAction.Stats), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Stats)),

        new(FontAwesomeIcon.ChartArea, "os.aetherling_tour_radar_title", "os.aetherling_tour_radar_body",
            t => t._surfaces.RadarRect() ?? t._surfaces.NavRect(PetNavAction.Stats), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Stats)),

        new(FontAwesomeIcon.Cog, "os.aetherling_tour_settings_title", "os.aetherling_tour_settings_body",
            t => t._surfaces.ShowOutsideRect() ?? t._surfaces.NavRect(PetNavAction.Settings), Dim: 0.45f,
            Enter: t => t.Go(PetNavAction.Settings)),

        new(FontAwesomeIcon.Question, "os.aetherling_tour_again_title", "os.aetherling_tour_again_body",
            t => t._surfaces.TourRowRect() ?? t._surfaces.NavRect(PetNavAction.Settings), Panel: TourAnchor.Top,
            Enter: t => t.Go(PetNavAction.Settings)),
    ];

    private readonly SpotlightTour<LumiTour> _runner = new();

    private Surfaces _surfaces = null!;
    private PetNavAction _from = PetNavAction.Home;
    private string _name = AetherlingLimits.DefaultName;
    private string _element = string.Empty;
    private int _feedsPerDay;
    private int _reactionThreshold;

    public LumiTour()
    {
        _runner.Format = Substitute;
        _runner.Finished += OnRunnerFinished;
    }

    public bool Active => _runner.Active;

    /// <summary>Raised once per run, after the app has been put back on the page the tour started from.</summary>
    public event Action? Finished;

    /// <summary>Starts a run on the given creature. <paramref name="from"/> is where the player was, which
    /// is where they are put back at the end.</summary>
    public void Start(OsAppContext ctx, AetherlingDto core, Surfaces surfaces, PetNavAction from)
    {
        if (_runner.Active)
        {
            return;
        }
        _surfaces = surfaces;
        _from = from;
        _name = core.PetName ?? AetherlingLimits.DefaultName;
        _element = Elements.Find(PetState.AttunedElement(core)) is { } def ? ctx.Localize(Elements.NameKey(def)) : "";
        _feedsPerDay = Math.Clamp((int)(core.Adult?.FeedsPerDay ?? AetherlingLimits.ShownFeedsPerDay), 1,
            AetherlingLimits.ShownFeedsPerDay);
        _reactionThreshold = Math.Max(1, core.Adult?.DietTurnThreshold ?? 1);
        _runner.Start(Script, this);
    }

    /// <summary>Drawn after the page and the bar, in a layer of its own: the pages underneath use children
    /// of their own, and a scrim drawn with the parent's list would land beneath them.</summary>
    public void Draw()
    {
        if (!_runner.Active)
        {
            return;
        }
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##lumiTourLayer", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        _runner.Draw(this, dl, origin, size, origin, origin + size, "##lumiTourScrim");
    }

    private void Go(PetNavAction page) => _surfaces.Navigate(page);

    private void OnRunnerFinished()
    {
        _surfaces.Navigate(_from);
        Finished?.Invoke();
    }

    /// <summary>The copy speaks to the creature by name and quotes the server's own numbers: {0} the
    /// name, {1} the attuned element, {2} the meals a day, {3} the crystals a reaction takes.</summary>
    private string Substitute(string text)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, _name, _element, _feedsPerDay, _reactionThreshold);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <summary>A ghost hand: one tap on the creature, then a slow stroke across it, over and over.</summary>
    private void DemoPetting(ImDrawListPtr dl)
    {
        if (_surfaces.PetRect() is not { } rect)
        {
            return;
        }
        var t = _runner.DemoTime % PettingLoopSeconds;
        var centre = TourDraw.Centre(rect);
        var reach = (rect.BR.X - rect.TL.X) * 0.16f;
        var at = centre + new Vector2(0f, (rect.BR.Y - rect.TL.Y) * 0.12f);

        if (t < 0.6f)
        {
            TourDraw.DrawCursor(dl, at, pressed: t is > 0.15f and < 0.4f);
            return;
        }
        if (t < 2.4f)
        {
            var phase = (t - 0.6f) / 1.8f;
            var x = MathF.Sin(phase * MathF.Tau) * reach;
            TourDraw.DrawCursor(dl, at + new Vector2(x, 0f), pressed: true);
            return;
        }
        TourDraw.DrawCursor(dl, at, pressed: false);
    }
}
