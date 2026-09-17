using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.UI;
using AetherLove.Shared.Racing;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The Starlight Cup page: the rules and the entry button before a reservation, the animated
/// standings and the next leg during one, the podium and the rewards after the fourth. Hub replies park
/// in tasks and are drained at the top of <see cref="Draw"/>, never touched from a continuation. The entry button
/// opens the race hand screen, which saves the hand and then calls <see cref="Enter"/>.</summary>
internal sealed class CupScreen(IRacerHost host, IAppCapabilities caps, Action<LumiRaceStartResultDto> race, Action back, Action openHand)
{
    private const float BackGap = 6f;
    private const uint RewardPaper = 0xFFFFFBF4;
    private static readonly uint Paper = ImGui.ColorConvertFloat4ToU32(RacerChrome.Paper with { W = 1f });
    private static readonly uint Ink = ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = 1f });
    private static readonly uint Gold = ImGui.ColorConvertFloat4ToU32(RacerChrome.CupGold);
    private static readonly uint Rule = ImGui.ColorConvertFloat4ToU32(RacerChrome.Rule);
    private static readonly uint Shade = ImGui.ColorConvertFloat4ToU32(RacerChrome.PictureShade);
    private static readonly uint White = ImGui.ColorConvertFloat4ToU32(RacerChrome.WhiteInk);
    private LumiCupStateDto? _state;
    private LumiCupDto? _cup;
    private Task<LumiCupStateDto>? _refresh;
    private Task<LumiCupDto>? _write;
    private bool _openRace;
    private string? _error;
    private PetRuntime[] _pets = [];
    private Guid _loadedId;
    private double _revealedAt;
    private TimeSpan _serverOffset;

    private bool _holdPodium;

    public void OnShow()
    {
        if (_write is null)
        {
            _refresh = host.GetCupAsync();
        }
    }

    public void FinishRace()
    {
        if (_cup?.ActiveRace is { } active)
        {
            _holdPodium = _cup.FinishedRaces.Length == LumiCupRules.RaceCount - 1;
            _write = host.FinishCupRaceAsync(_cup.Id, active.RaceId);
        }
    }

    public bool ShowingPodium => _cup?.FinishedRaces.Length == LumiCupRules.RaceCount && !_holdPodium;

    public void Drain()
    {
        try
        {
            if (_refresh is { IsCompleted: true } refresh)
            {
                _refresh = null;
                _state = refresh.GetAwaiter().GetResult();
                _serverOffset = _state.ServerNowUtc - DateTimeOffset.UtcNow;
                SetCup(_state.Cup);
                _error = null;
            }
            if (_write is { IsCompleted: true } write)
            {
                _write = null;
                SetCup(write.GetAwaiter().GetResult());
                _error = null;
                if (_openRace && _cup?.ActiveRace is { } active)
                {
                    _openRace = false;
                    race(new(active, new(0, 0, false, 0, false)));
                }
            }
        }
        catch (Exception ex)
        {
            _openRace = false;
            _error = host.DescribeError(ex);
        }
    }

    /// <summary>Enters this week's cup with the hand just saved. Called by the race hand screen.</summary>
    public void Enter()
    {
        if (_write is not null)
        {
            return;
        }

        _error = null;
        try
        {
            _write = host.EnterCupAsync();
        }
        catch (Exception ex)
        {
            _error = host.DescribeError(ex);
        }
    }

    private void SetCup(LumiCupDto? cup)
    {
        if (_cup?.FinishedRaces.Length != cup?.FinishedRaces.Length)
        {
            _revealedAt = ImGui.GetTime();
        }
        _cup = cup;
        if (cup is null || _loadedId == cup.Id)
        {
            return;
        }
        _loadedId = cup.Id;
        _pets = cup.Field.Select(entry =>
        {
            var pet = new PetRuntime();
            pet.SetPhaseSeed($"{entry.Name}#{entry.Slot}");
            pet.EnsureLoaded(host.PetAssetRoot, PetState.ShellFolderFor(entry.Shell));
            pet.ApplyDraftLook(entry.Palette, entry.Accessories, string.Empty, []);
            return pet;
        }).ToArray();
    }

    public void Draw(OsAppContext ctx, bool framed = false)
    {
        Drain();
        var avail = ImGui.GetContentRegionAvail();
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(Px(12)));
        using var child = ImRaii.Child("##starlightCup", avail, false, ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoBackground);
        if (!child)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        if (!framed)
        {
            dl.AddRectFilled(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), Paper);
            dl.AddRect(ImGui.GetWindowPos() + new Vector2(Px(4)), ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(Px(4)), Ink, Px(7));
        }
        using var text = ImRaii.PushColor(ImGuiCol.Text, Ink);
        var showingPodium = _cup?.FinishedRaces.Length == LumiCupRules.RaceCount && !_holdPodium;
        if (showingPodium)
        {
            ResultCelebration.Backdrop(ctx, host.PetAssetRoot, ImGui.GetWindowPos(), ImGui.GetWindowSize());
            ResultCelebration.Header(ctx, origin, width, "os.racer_cup_complete");
            ImGui.Dummy(new Vector2(width, Px(190)));
        }
        else if (!framed)
        {
            Art(ctx, LumiCupRules.Finale, origin, new Vector2(width, Px(110)));
            ImGui.Dummy(new Vector2(width, Px(116)));
            using (ctx.TitleFont?.Push())
            {
                RacerChrome.CenteredText(ctx.Localize("os.racer_cup_title"));
            }
            dl.AddLine(new Vector2(origin.X + Px(12), ImGui.GetCursorScreenPos().Y), new Vector2(origin.X + width - Px(12), ImGui.GetCursorScreenPos().Y), Gold, Px(2));
            ImGui.Dummy(new Vector2(1, Px(6)));
        }
        var busy = _refresh is not null || _write is not null;
        if (_cup is not { } cup)
        {
            Art(ctx, LumiCupRules.Finale, ImGui.GetCursorScreenPos(), new Vector2(width, Px(90)));
            ImGui.Dummy(new Vector2(width, Px(102)));
            Paragraph(ctx.Localize("os.racer_cup_rules"));
            ImGui.Dummy(new Vector2(1, Px(10)));
            Paragraph(string.Format(ctx.Localize("os.racer_cup_prizes"), _state?.CompletionSparks ?? 0));
            ImGui.Dummy(new Vector2(1, Px(10)));
            Paragraph(ctx.Localize("os.racer_cup_scoring"));
            ImGui.Dummy(new Vector2(1, Px(12)));
            var reason = caps.Party.InParty ? ctx.Localize("os.racer_cup_solo")
                : _state is { CanEnter: false } ? ctx.Localize("os.racer_cup_unavailable") : null;
            if (Button(ctx, "enter", "os.racer_cup_enter", !busy && _state is not null, reason))
            {
                openHand();
            }
        }
        else
        {
            var count = cup.FinishedRaces.Length;
            var complete = count == LumiCupRules.RaceCount;
            if (!showingPodium)
            {
                RacerChrome.CenteredText(string.Format(ctx.Localize(complete ? "os.racer_cup_complete" : "os.racer_cup_progress"), count));
                Progress(ctx, count);
            }
            if (complete && !_holdPodium)
            {
                Podium(ctx, cup);
                var place = Array.IndexOf(LumiCupRules.Standings(cup.FinishedRaces), (short)0) + 1;
                using (ctx.TitleFont?.Push())
                {
                    var label = string.Format(ctx.Localize("os.racer_cup_overall"), place);
                    var center = ImGui.GetCursorScreenPos() + new Vector2(width / 2, ImGui.GetTextLineHeight() / 2);
                    var labelWidth = ImGui.CalcTextSize(label).X;
                    if (labelWidth < width - Px(60))
                    {
                        ResultCelebration.Laurels(dl, center, labelWidth / 2);
                    }

                    RacerChrome.CenteredText(label);
                }
                var points = cup.FinishedRaces.Sum(r => LumiCupRules.Points(Array.IndexOf(r.Placements, (short)0)));
                RacerChrome.CenteredText(string.Format(ctx.Localize("os.racer_cup_points"), points));
                ImGui.Dummy(new Vector2(1, Px(8)));
                Rewards(ctx, cup);
                ImGui.Dummy(new Vector2(1, Px(6)));
                Paragraph(ctx.Localize("os.racer_cup_paid"));
            }
            else
            {
                Standings(ctx, cup);
                ImGui.Dummy(new Vector2(1, Px(8)));
                if (complete)
                {
                    if (Button(ctx, "rewards", "os.racer_cup_view_rewards", !busy))
                    {
                        _holdPodium = false;
                    }
                }
                else
                {
                    NextRace(ctx, dl, cup, count, width);
                    if (Button(ctx, "race", cup.ActiveRace is null ? "os.racer_cup_continue" : "os.racer_cup_resume",
                        !busy, caps.Party.InParty ? ctx.Localize("os.racer_cup_solo") : null))
                    {
                        _openRace = true;
                        _write = host.StartCupRaceAsync(cup.Id, count);
                    }
                }
            }
        }
        if (busy)
        {
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
        }
        if (_error is not null)
        {
            Paragraph(_error);
            if (Button(ctx, "retry", "os.racer_cup_retry", !busy))
            {
                OnShow();
            }
        }
        ImGui.Dummy(new Vector2(1, Px(BackGap)));
        if (!framed && Button(ctx, "back", showingPodium ? "os.racer_home" : "os.racer_back", !busy))
        {
            back();
        }
    }

    private static void Paragraph(string label)
    {
        ImGui.TextWrapped(label);
    }

    private bool Button(OsAppContext ctx, string id, string key, bool enabled, string? reason = null) =>
        GrandstandFrame.ActionButton(ctx, host, "##cup" + id, ctx.Localize(key), enabled, reason, secondary: id == "back");

    private void Art(OsAppContext ctx, string course, Vector2 at, Vector2 size)
    {
        var path = Path.Combine(host.PetAssetRoot, "racer", "courses", course + ".png");
        if (ctx.Capabilities.Textures.Get(path) is { } art)
        {
            var (uv0, uv1) = OsDrawShared.CoverUv(RacerChrome.CourseArtAspect, 1f, size.X, size.Y);
            ImGui.GetWindowDrawList().AddImageRounded(art, at, at + size, uv0, uv1, 0xFFFFFFFF, Px(7));
        }
    }

    private void NextRace(OsAppContext ctx, ImDrawListPtr dl, LumiCupDto cup, int count, float width)
    {
        var next = cup.Courses[count];
        var at = ImGui.GetCursorScreenPos();
        var gap = Px(8);
        var buttons = Px(60) + gap + ImGui.GetStyle().ItemSpacing.Y * 2;
        var height = MathF.Max(Px(56), MathF.Min(ImGui.GetContentRegionAvail().Y - buttons, width / RacerChrome.CourseArtAspect));
        var size = new Vector2(width, height);
        Art(ctx, next, at, size);
        dl.AddRectFilled(at, at + size, Shade, Px(7));
        var label = string.Format(ctx.Localize("os.racer_cup_next"), ctx.Localize("os.racer_course_" + next));
        dl.AddText(at + new Vector2(Px(10), Px(8)), White, label);
        var course = AetherRaceLive.CourseByKey(next);
        if (course is not null)
        {
            dl.AddText(at + new Vector2(Px(10), Px(31)), White,
                RacerChrome.DifficultyLabel(ctx, (short)LumiRaceDifficultyRules.For(cup.Field[0].Element, course)));
        }
        ImGui.Dummy(new Vector2(width, height + gap));
    }

    private static void Progress(OsAppContext ctx, int count)
    {
        var at = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(at + new Vector2(w / 8, Px(20)), at + new Vector2(w * 7 / 8, Px(20)), Rule, Px(2));
        for (var i = 0; i < LumiCupRules.RaceCount; i++)
        {
            var p = at + new Vector2(w * (i + .5f) / LumiCupRules.RaceCount, Px(20));
            dl.AddCircleFilled(p, Px(13), i < count ? Ink : Gold);
            if (i < count || i == LumiCupRules.RaceCount - 1)
            {
                IconDraw.AddCentered(dl, i < count ? FontAwesomeIcon.Check : FontAwesomeIcon.Trophy, Px(13), p, i < count ? Paper : Ink);
            }
            else
            {
                var label = (i + 1).ToString();
                dl.AddText(p - ImGui.CalcTextSize(label) / 2, Ink, label);
            }
        }
        ImGui.Dummy(new Vector2(w, Px(42)));
    }

    private void Standings(OsAppContext ctx, LumiCupDto cup)
    {
        var at = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(at + new Vector2(Px(8), 0), Ink, ctx.Localize("os.racer_cup_racer"));
        dl.AddText(at + new Vector2(width - Px(88), 0), Ink, "+");
        dl.AddText(at + new Vector2(width - Px(43), 0), Ink, "Σ");
        at.Y += Px(24);
        var order = LumiCupRules.Standings(cup.FinishedRaces);
        var previous = LumiCupRules.Standings(cup.FinishedRaces.SkipLast(1).ToArray());
        var elapsed = ctx.ReduceMotion ? 2f : (float)(ImGui.GetTime() - _revealedAt);
        var slide = Math.Clamp((elapsed - .5f) / .6f, 0, 1);
        slide = slide * slide * (3 - 2 * slide);
        var rowH = Px(39);
        for (var rank = 0; rank < order.Length; rank++)
        {
            var slot = order[rank];
            var old = Array.IndexOf(previous, slot);
            var y = at.Y + (old + (rank - old) * slide) * rowH;
            if (slot == 0)
            {
                dl.AddRectFilled(new Vector2(at.X, y), new Vector2(at.X + width, y + rowH - Px(2)), Gold, Px(6));
            }
            dl.AddText(new Vector2(at.X + Px(5), y + Px(10)), Ink, (rank + 1).ToString());
            var pet = _pets[slot];
            pet.Tick(ctx.ReduceMotion);
            pet.Draw(dl, ctx.Capabilities.Textures, new Vector2(at.X + Px(48), y + rowH - Px(3)), Px(36), pet.Pose, props: false);
            var name = slot == 0 ? ctx.Localize("os.racer_cup_you") : cup.Field[slot].Name;
            dl.PushClipRect(new Vector2(at.X + Px(75), y), new Vector2(at.X + width - Px(85), y + rowH), true);
            using (RacerFonts.Get(RacerTextSize.Caption)?.Push())
            {
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(at.X + Px(75), y + Px(3)), Ink, name, width - Px(165));
            }
            dl.PopClipRect();
            var total = cup.FinishedRaces.Sum(r => LumiCupRules.Points(Array.IndexOf(r.Placements, slot)));
            var latest = cup.FinishedRaces.Length == 0 ? 0 : LumiCupRules.Points(Array.IndexOf(cup.FinishedRaces[^1].Placements, slot));
            var shown = total - latest + (int)MathF.Round(latest * Math.Clamp(elapsed / .5f, 0, 1));
            dl.AddText(new Vector2(at.X + width - Px(76), y + Px(10)), Ink, "+" + latest);
            dl.AddText(new Vector2(at.X + width - Px(30), y + Px(10)), Ink, shown.ToString());
            dl.AddLine(new Vector2(at.X, y + rowH), new Vector2(at.X + width, y + rowH), Rule);
        }
        ImGui.Dummy(new Vector2(width, rowH * order.Length + Px(24)));
    }

    private void Podium(OsAppContext ctx, LumiCupDto cup)
    {
        var at = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var order = LumiCupRules.Standings(cup.FinishedRaces);
        ResultCelebration.Stage(dl, at, width);
        float[] columns = [.5f, .19f, .81f];
        var nameTop = at.Y + Px(192);
        for (var rank = 0; rank < 3; rank++)
        {
            var x = at.X + width * columns[rank];
            var top = at.Y + Px(rank == 0 ? 112 : rank == 1 ? 134 : 145);
            ResultCelebration.Step(dl, new Vector2(x, top), width * .285f, at.Y + Px(186) - top, rank);
            var slot = order[rank];
            var pet = _pets[slot];
            pet.Tick(ctx.ReduceMotion);
            pet.Draw(dl, ctx.Capabilities.Textures, new Vector2(x, top - Px(3)), MathF.Min(width * .29f, Px(rank == 0 ? 104 : 86)), pet.Pose, props: false);
            if (slot < cup.Field.Length)
            {
                ResultCelebration.NamePlate(dl, new Vector2(x, nameTop), width * .31f, cup.Field[slot].Name, slot == 0);
            }
        }
        ImGui.Dummy(new Vector2(width, Px(198) + ResultCelebration.NamePlateHeight));
    }

    private void Rewards(OsAppContext ctx, LumiCupDto cup)
    {
        var at = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var count = cup.PacksAwarded;
        var label = string.Format(ctx.Localize("os.racer_cup_reward"), cup.SparksAwarded, count);
        var wrap = width - Px(32);
        var textSize = ImGui.CalcTextSize(label, false, wrap);
        var height = textSize.Y + Px(count > 0 ? 83 : 30);
        dl.AddRectFilled(at, at + new Vector2(width, height), RewardPaper, Px(10));
        dl.AddRect(at, at + new Vector2(width, height), Ink, Px(10), ImDrawFlags.None, Px(1.2f));
        dl.AddRect(at + new Vector2(Px(3)), at + new Vector2(width, height) - new Vector2(Px(3)), Gold, Px(8));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), at + new Vector2((width - textSize.X) / 2, Px(12)), Ink, label, wrap);
        var path = Path.Combine(host.PetAssetRoot, "racer", "foil-pack.png");
        if (ctx.Capabilities.Textures.Get(path) is { } art)
        {
            for (var i = 0; i < count; i++)
            {
                var p = at + new Vector2((width - Px(count * 39 - 5)) / 2 + Px(i * 39), textSize.Y + Px(20));
                dl.AddImage(art, p, p + new Vector2(Px(34), Px(46)));
            }
        }
        ImGui.Dummy(new Vector2(width, height));
    }
}
