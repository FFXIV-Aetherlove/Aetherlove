using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Wallet;

/// <summary>The second tab: the character's in-game currencies, grouped into cards with the game's own
/// localized names and icons. Every currency with a reachable cap carries a progress bar and turns gold
/// when it is full. Starred currencies lift into a Favorites card at the top. Read-only, and polled while
/// visible so a hand-in updates without any interaction.</summary>
internal sealed class CurrenciesScreen
{
    private const float PadX = 16f;
    private const float PollSeconds = 2f;
    private const float HeroHeight = 96f;
    private const float IconSize = 30f;
    private const float FlightSeconds = 0.34f;

    private readonly IWalletHost _host;
    private readonly WalletFavorites _favorites;
    private readonly EntranceAnimation _entrance = new();

    private volatile IReadOnlyList<SectionGroup>? _groups;
    private volatile IReadOnlyList<WalletCurrencyRow>? _rows;
    private volatile WalletCurrencyRow? _gil;
    private volatile bool _loading;
    private volatile bool _loaded;
    private int _generation;
    private int _snapshotVersion;
    private float _sincePoll;
    private uint _flightItemId;
    private float _flightFromY;
    private double _flightStart = -1.0;
    private DeferredRow? _deferred;

    private sealed record SectionGroup(string HeaderKey, WalletCurrencyRow[] Rows);

    /// <summary>A row mid-flight, held back until every card has been drawn so it glides over them rather
    /// than under the sections that come after its destination.</summary>
    private readonly record struct DeferredRow(
        WalletCurrencyRow Row, Vector2 Tl, float CardW, float RowH, float Rounding, bool First, bool Last);

    private static readonly WalletCurrencySection[] SectionOrder =
    [
        WalletCurrencySection.Common,
        WalletCurrencySection.Tomestones,
        WalletCurrencySection.Hunt,
        WalletCurrencySection.Pvp,
        WalletCurrencySection.Scrips,
        WalletCurrencySection.Field,
        WalletCurrencySection.Other,
    ];

    public CurrenciesScreen(IWalletHost host, WalletFavorites favorites)
    {
        _host = host;
        _favorites = favorites;
    }

    public void OnShow()
    {
        _entrance.Arm();
        Refresh();
    }

    public void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);
        _loading = true;
        _sincePoll = 0f;
        _ = Task.Run(async () =>
        {
            IReadOnlyList<WalletCurrencyRow> rows;
            try
            {
                rows = await _host.ReadCurrenciesAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                rows = [];
            }
            if (generation != _generation)
            {
                return;
            }
            Apply(rows);
            _loaded = true;
            _loading = false;
        });
    }

    /// <summary>Groups the flat snapshot once per read so the draw loop never sorts: gil lifts out to the
    /// hero card, starred currencies lift into Favorites in the order they were starred, and everything
    /// else keeps its natural section.</summary>
    private void Apply(IReadOnlyList<WalletCurrencyRow> rows)
    {
        _rows = rows;
        WalletCurrencyRow? gil = null;
        var groups = new List<SectionGroup>(SectionOrder.Length + 1);

        var favoriteRows = _favorites.Pick(rows);
        if (favoriteRows.Count > 0)
        {
            groups.Add(new SectionGroup("os.wallet_cur_section_favorites", favoriteRows.ToArray()));
        }

        foreach (var section in SectionOrder)
        {
            var inSection = new List<WalletCurrencyRow>();
            foreach (var row in rows)
            {
                if (row.Section != section || _favorites.Contains(row.ItemId))
                {
                    continue;
                }
                if (row.IsPrimary && gil is null)
                {
                    gil = row;
                    continue;
                }
                inSection.Add(row);
            }
            if (inSection.Count > 0)
            {
                groups.Add(new SectionGroup(HeaderKeyFor(section), inSection.ToArray()));
            }
        }
        _gil = gil;
        _groups = groups;
    }

    /// <summary>Stars or unstars a currency and regroups immediately, remembering where the row was so the
    /// next frame can fly it to its new home.</summary>
    private void ToggleFavorite(uint itemId, float fromY, bool reduceMotion)
    {
        _favorites.Toggle(itemId);

        if (reduceMotion)
        {
            _flightStart = -1.0;
            _flightItemId = 0;
        }
        else
        {
            _flightItemId = itemId;
            _flightFromY = fromY;
            _flightStart = ImGui.GetTime();
        }

        if (_rows is { } rows)
        {
            Apply(rows);
        }
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;

        if (_host.SnapshotVersion != _snapshotVersion)
        {
            _snapshotVersion = _host.SnapshotVersion;
            _groups = null;
            _gil = null;
            _loaded = false;
            Refresh();
        }
        else if (!_loading)
        {
            _sincePoll += ImGui.GetIO().DeltaTime;
            if (_sincePoll >= PollSeconds)
            {
                Refresh();
            }
        }

        if (!_host.InGame)
        {
            DrawNotice(Loc.T("os.wallet_ingame_hint"), winW);
            _entrance.EndFrame();
            return;
        }

        var groups = _groups;
        if (groups is null || groups.Count == 0)
        {
            if (!_loaded)
            {
                ImGui.Dummy(new Vector2(0f, Px(60f)));
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(20f));
                LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(ThemeService.Current.Accent));
                ImGui.Dummy(new Vector2(0f, Px(40f)));
            }
            else
            {
                DrawNotice(Loc.T("os.wallet_cur_empty"), winW);
            }
            _entrance.EndFrame();
            return;
        }

        if (_gil is { } gil)
        {
            DrawGilHero(ctx, gil, winW);
        }
        _deferred = null;
        foreach (var group in groups)
        {
            DrawSectionHeader(group.HeaderKey);
            DrawSectionCard(ctx, group.Rows, winW);
        }
        if (_deferred is { } flight)
        {
            DrawRow(ctx, ImGui.GetWindowDrawList(), flight.Row, flight.Tl, flight.CardW, flight.RowH,
                flight.Rounding, flight.First, flight.Last, flying: true);
            _deferred = null;
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    /// <summary>Gil gets the hero treatment the Sparks tab gives the balance, in the same wallet gold, so
    /// the tab opens on the number people came to see.</summary>
    private void DrawGilHero(OsAppContext ctx, WalletCurrencyRow gil, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(HeroHeight);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(cardW, cardH);
        OsDrawShared.RoundedGradient(dl, tl, br, Px(18f), WalletApp.TileTopColor, WalletApp.TileBottomColor);

        dl.AddText(new Vector2(tl.X + Px(16f), tl.Y + Px(12f)), OsDrawShared.White(0.82f), gil.Name);

        var amount = gil.Amount.ToString("N0", ctx.Culture);
        var iconSz = Px(IconSize);
        using (UiFonts.H1?.Push())
        {
            var amountSz = ImGui.CalcTextSize(amount);
            var rowY = tl.Y + cardH - Px(16f) - amountSz.Y;
            var x = tl.X + Px(16f);
            if (_host.GetCurrencyIcon(gil.IconId) is { } icon)
            {
                var iconY = rowY + (amountSz.Y - iconSz) * 0.5f;
                dl.AddImage(icon, new Vector2(x, iconY), new Vector2(x + iconSz, iconY + iconSz));
                x += iconSz + Px(10f);
            }
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(x, rowY), 0xFFFFFFFFu, amount);
        }

        ImGui.Dummy(new Vector2(0f, cardH + Px(10f)));
    }

    private static string HeaderKeyFor(WalletCurrencySection section) => section switch
    {
        WalletCurrencySection.Tomestones => "os.wallet_cur_section_tomestones",
        WalletCurrencySection.Hunt => "os.wallet_cur_section_hunt",
        WalletCurrencySection.Pvp => "os.wallet_cur_section_pvp",
        WalletCurrencySection.Scrips => "os.wallet_cur_section_scrips",
        WalletCurrencySection.Field => "os.wallet_cur_section_field",
        WalletCurrencySection.Other => "os.wallet_cur_section_other",
        _ => "os.wallet_cur_section_common",
    };

    private static void DrawSectionHeader(string key)
    {
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T(key));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));
    }

    /// <summary>One rounded card per section with hairline separators, so a section reads as a single
    /// object instead of a stack of floating rows.</summary>
    private void DrawSectionCard(OsAppContext ctx, WalletCurrencyRow[] rows, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var totalH = 0f;
        foreach (var row in rows)
        {
            totalH += CurrencyRowDraw.RowHeight(row);
        }

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var rounding = Px(CurrencyRowDraw.CardRounding);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, totalH), OsDrawShared.White(0.05f), rounding);

        var y = tl.Y;
        for (var i = 0; i < rows.Length; i++)
        {
            var rowH = CurrencyRowDraw.RowHeight(rows[i]);
            if (i > 0)
            {
                dl.AddLine(new Vector2(tl.X + Px(12f), y), new Vector2(tl.X + cardW - Px(12f), y),
                    OsDrawShared.White(0.06f), Px(1f));
            }
            var rowTl = new Vector2(tl.X, y);
            if (TryTakeFlight(rows[i].ItemId, rowTl, out var flightTl))
            {
                _deferred = new DeferredRow(rows[i], flightTl, cardW, rowH, rounding, i == 0, i == rows.Length - 1);
            }
            else
            {
                DrawRow(ctx, dl, rows[i], rowTl, cardW, rowH, rounding, i == 0, i == rows.Length - 1, flying: false);
            }
            y += rowH;
        }

        ImGui.Dummy(new Vector2(cardW, totalH + Px(6f)));
    }

    /// <summary>True while this row is gliding to a slot it just moved into, handing back the position to
    /// draw it at so the eye follows it instead of hunting for what changed.</summary>
    private bool TryTakeFlight(uint itemId, Vector2 layoutTl, out Vector2 flightTl)
    {
        flightTl = layoutTl;
        if (_flightItemId != itemId || _flightStart < 0.0)
        {
            return false;
        }

        var progress = (float)((ImGui.GetTime() - _flightStart) / FlightSeconds);
        if (progress >= 1f)
        {
            _flightStart = -1.0;
            _flightItemId = 0;
            return false;
        }
        flightTl.Y = Lerp(_flightFromY, layoutTl.Y, EaseOut(Math.Clamp(progress, 0f, 1f)));
        return true;
    }

    private void DrawRow(OsAppContext ctx, ImDrawListPtr dl, WalletCurrencyRow row, Vector2 tl, float cardW,
        float rowH, float rounding, bool first, bool last, bool flying)
    {
        var rowTop = tl.Y;
        var star = new CurrencyStar(_favorites.Contains(row.ItemId),
            () => ToggleFavorite(row.ItemId, rowTop, ctx.ReduceMotion));
        CurrencyRowDraw.Draw(ctx, dl, row, tl, cardW, rowH, rounding, first, last, flying,
            _host.GetCurrencyIcon, star);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    private static void DrawNotice(string text, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(60f)));
        var wrapW = winW - Px(PadX) * 2.5f;
        var sz = ImGui.CalcTextSize(text, false, wrapW);
        ImGui.SetCursorPosX((winW - Math.Min(sz.X, wrapW)) * 0.5f);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextColored(UiColors.Hint, text);
        ImGui.PopTextWrapPos();
    }
}
