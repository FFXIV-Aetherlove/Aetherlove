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
using Dalamud.Interface;

namespace AetherOS.Apps.Wallet;

/// <summary>The second tab: in-game currencies, grouped into cards with the game's own localized names
/// and icons. Every currency with a reachable cap carries a progress bar and turns gold when it is full.
/// Starred currencies lift into a Favorites card at the top. Read-only, and polled while visible so a
/// hand-in updates without any interaction.
/// <para>With more than one character remembered, a chip strip sits above the cards: the logged-in
/// character live, every other one as last seen, and an overview that sums gil and lays every
/// character's amount side by side for whatever any of them pinned. Pins are per character, so the
/// crafter alt's scrips and the main's tomestones each stay where they belong, and the overview is
/// their union.</para></summary>
internal sealed class CurrenciesScreen
{
    private const float PadX = 16f;
    private const float PollSeconds = 2f;
    private const float HeroHeight = 96f;
    private const float IconSize = 30f;
    private const float FlightSeconds = 0.34f;
    private const float ChipH = 36f;
    private const float ChipGap = 6f;

    private static readonly Vector4 LiveGreen = new(0.36f, 0.82f, 0.46f, 1f);

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
    private int _charactersVersion = -1;
    private float _sincePoll;
    private uint _flightItemId;
    private float _flightFromY;
    private double _flightStart = -1.0;
    private DeferredRow? _deferred;

    /// <summary>Which character the cards show: null is the logged-in one live, a content id is a
    /// remembered one as last seen. <see cref="_overview"/> wins over both.</summary>
    private ulong? _viewing;
    private bool _overview;
    private ulong _menuFor;
    private float _stripScroll;

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

    /// <summary>The character whose pins the cards edit: the one being viewed, else the logged-in one.</summary>
    private ulong? PinOwner => _viewing ?? _host.CurrentCharacter?.ContentId;

    private bool ViewingLive => _viewing is null || _viewing == _host.CurrentCharacter?.ContentId;

    public void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);
        _sincePoll = 0f;
        if (_overview)
        {
            _loaded = true;
            _loading = false;
            return;
        }
        if (!ViewingLive)
        {
            // A remembered character has no live read; its cards come straight from the snapshot.
            var snapshot = FindSnapshot(_viewing!.Value);
            Apply(snapshot?.Rows() ?? []);
            _loaded = true;
            _loading = false;
            return;
        }
        _loading = true;
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

    private WalletCharacterSnapshot? FindSnapshot(ulong contentId)
    {
        foreach (var c in _host.KnownCharacters)
        {
            if (c.ContentId == contentId)
            {
                return c;
            }
        }
        return null;
    }

    /// <summary>Groups the flat snapshot once per read so the draw loop never sorts: gil lifts out to the
    /// hero card, starred currencies lift into Favorites in the order they were starred, and everything
    /// else keeps its natural section.</summary>
    private void Apply(IReadOnlyList<WalletCurrencyRow> rows)
    {
        _rows = rows;
        WalletCurrencyRow? gil = null;
        var groups = new List<SectionGroup>(SectionOrder.Length + 1);
        var owner = PinOwner;

        var favoriteRows = owner is { } cid ? _favorites.Pick(cid, rows) : [];
        if (favoriteRows.Count > 0)
        {
            groups.Add(new SectionGroup("os.wallet_cur_section_favorites", favoriteRows.ToArray()));
        }

        foreach (var section in SectionOrder)
        {
            var inSection = new List<WalletCurrencyRow>();
            foreach (var row in rows)
            {
                if (row.Section != section || (owner is { } o && _favorites.Contains(o, row.ItemId)))
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

    /// <summary>Stars or unstars a currency for the character on show and regroups immediately,
    /// remembering where the row was so the next frame can fly it to its new home.</summary>
    private void ToggleFavorite(uint itemId, float fromY, bool reduceMotion)
    {
        if (PinOwner is not { } owner)
        {
            return;
        }
        _favorites.Toggle(owner, itemId);

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

    private void Select(ulong? viewing, bool overview)
    {
        _viewing = viewing;
        _overview = overview;
        _groups = null;
        _gil = null;
        _loaded = false;
        _entrance.Arm();
        Refresh();
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;

        if (_host.SnapshotVersion != _snapshotVersion)
        {
            _snapshotVersion = _host.SnapshotVersion;
            // A character switch: whatever was selected, the live view is the one that makes sense now.
            _viewing = null;
            _overview = false;
            _groups = null;
            _gil = null;
            _loaded = false;
            Refresh();
        }
        else if (!_loading && ViewingLive && !_overview)
        {
            _sincePoll += ImGui.GetIO().DeltaTime;
            if (_sincePoll >= PollSeconds)
            {
                Refresh();
            }
        }
        if (_host.CharactersVersion != _charactersVersion)
        {
            _charactersVersion = _host.CharactersVersion;
            if (_viewing is { } gone && FindSnapshot(gone) is null)
            {
                Select(null, false);
            }
        }

        var known = _host.KnownCharacters;
        if (known.Count >= 2)
        {
            DrawCharacterStrip(ctx, known, winW);
        }

        if (_overview)
        {
            DrawOverview(ctx, known, winW);
            _entrance.EndFrame();
            return;
        }

        if (!_host.InGame && ViewingLive)
        {
            DrawNotice(Loc.T("os.wallet_ingame_hint"), winW);
            _entrance.EndFrame();
            return;
        }

        if (!ViewingLive && FindSnapshot(_viewing!.Value) is { } seen)
        {
            DrawSeenLine(ctx, seen, winW);
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

    // ------------------------------------------------------------------ the character strip

    /// <summary>One chip per remembered character plus the overview chip, in a horizontally scrolling
    /// lane so a fifth or sixth alt costs no height. The logged-in one wears a green dot and always sits
    /// first; the rest follow in the order they were last played.</summary>
    private void DrawCharacterStrip(OsAppContext ctx, IReadOnlyList<WalletCharacterSnapshot> known, float winW)
    {
        var t = ThemeService.Current;
        var chipH = Px(ChipH);
        var gap = Px(ChipGap);
        var laneW = winW - Px(PadX) * 2f;
        ImGui.SetCursorPosX(Px(PadX));
        var laneTl = ImGui.GetCursorScreenPos();

        // Measured first so the lane can scroll exactly as far as the chips reach.
        var current = _host.CurrentCharacter;
        var entries = new List<(ulong? Cid, string Label, bool Live, bool Overview)>(known.Count + 1)
        {
            (null, Loc.T("os.wallet_chars_all"), false, true),
        };
        if (current is { } me)
        {
            entries.Add((me.ContentId, FirstName(me.Name), true, false));
        }
        foreach (var c in known)
        {
            if (current is { } l && c.ContentId == l.ContentId)
            {
                continue;
            }
            entries.Add((c.ContentId, FirstName(c.Name), false, false));
        }

        var widths = new float[entries.Count];
        var total = 0f;
        for (var i = 0; i < entries.Count; i++)
        {
            var textW = ImGui.CalcTextSize(entries[i].Label).X;
            widths[i] = MathF.Max(Px(64f), textW + Px(38f));
            total += widths[i] + (i > 0 ? gap : 0f);
        }
        var maxScroll = MathF.Max(0f, total - laneW);
        if (ImGui.IsMouseHoveringRect(laneTl, laneTl + new Vector2(laneW, chipH)))
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
            {
                _stripScroll = Math.Clamp(_stripScroll - wheel * Px(48f), 0f, maxScroll);
            }
        }
        _stripScroll = Math.Clamp(_stripScroll, 0f, maxScroll);

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(laneTl, laneTl + new Vector2(laneW, chipH), true);
        var x = laneTl.X - _stripScroll;
        for (var i = 0; i < entries.Count; i++)
        {
            var (cid, label, live, overview) = entries[i];
            var w = widths[i];
            var tl = new Vector2(x, laneTl.Y);
            var br = tl + new Vector2(w, chipH);
            var visible = br.X > laneTl.X && tl.X < laneTl.X + laneW;
            var selected = overview ? _overview : !_overview && (cid == _viewing || (_viewing is null && live));

            var clicked = false;
            var hovered = false;
            var rightClicked = false;
            if (visible)
            {
                ImGui.SetCursorScreenPos(tl);
                clicked = ImGui.InvisibleButton($"##walletChip{i}", new Vector2(w, chipH));
                hovered = ImGui.IsItemHovered();
                rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if (hovered)
                {
                    SharedUiHelpers.HandOnHover();
                }
            }

            dl.AddRectFilled(tl, br, selected
                ? ImGui.GetColorU32(t.Accent with { W = 0.55f })
                : OsDrawShared.White(hovered ? 0.12f : 0.06f), chipH * 0.5f);
            if (selected)
            {
                dl.AddRect(tl, br, ImGui.GetColorU32(t.AccentLight with { W = 0.4f }), chipH * 0.5f);
            }

            var discR = Px(11f);
            var discC = new Vector2(tl.X + Px(8f) + discR, tl.Y + chipH * 0.5f);
            if (overview)
            {
                dl.AddCircleFilled(discC, discR, OsDrawShared.White(0.10f));
                IconDraw.AddCentered(dl, FontAwesomeIcon.UserFriends, discR * 1.05f, discC,
                    OsDrawShared.White(selected ? 1f : 0.8f));
            }
            else
            {
                dl.AddCircleFilled(discC, discR, ImGui.GetColorU32(t.AccentLight with { W = selected ? 0.5f : 0.28f }));
                var initial = label.Length > 0 ? label[..1].ToUpperInvariant() : "?";
                var initialSz = ImGui.CalcTextSize(initial);
                dl.AddText(discC - initialSz * 0.5f, OsDrawShared.White(0.95f), initial);
                if (live)
                {
                    dl.AddCircleFilled(discC + new Vector2(discR * 0.7f, discR * 0.7f), Px(4f),
                        ImGui.GetColorU32(LiveGreen));
                    dl.AddCircle(discC + new Vector2(discR * 0.7f, discR * 0.7f), Px(4f),
                        OsDrawShared.Black(0.6f), 0, Px(1f));
                }
            }
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(discC.X + discR + Px(7f), tl.Y + (chipH - labelSz.Y) * 0.5f),
                OsDrawShared.White(selected ? 0.98f : 0.78f), label);

            if (hovered && !overview && cid is { } hoveredCid && FindSnapshot(hoveredCid) is { } snap)
            {
                ImGui.SetTooltip(live
                    ? $"{snap.Name} · {snap.World} · {Loc.T("os.wallet_chars_live")}"
                    : $"{snap.Name} · {snap.World}");
            }
            if (clicked)
            {
                Select(overview ? null : (live ? null : cid), overview);
            }
            if (rightClicked && !overview && !live && cid is { } forgettable)
            {
                _menuFor = forgettable;
                ImGui.OpenPopup("##walletCharMenu");
            }
            x += w + gap;
        }
        dl.PopClipRect();

        ImGui.SetCursorScreenPos(new Vector2(laneTl.X, laneTl.Y + chipH));
        ImGui.Dummy(new Vector2(laneW, Px(8f)));
        DrawCharacterMenu();
    }

    /// <summary>The right-click menu on a remembered character: forget it. Low stakes by design, since
    /// the next login as that character brings it straight back.</summary>
    private void DrawCharacterMenu()
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.13f, 0.12f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.Accent with { W = 0.5f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(4f), Px(4f)));
        if (ImGui.BeginPopup("##walletCharMenu"))
        {
            var label = Loc.T("os.wallet_chars_forget");
            var w = AppHeader.MenuWidth(label);
            if (AppHeader.MenuRow(FontAwesomeIcon.UserMinus, label, w, AppHeader.MenuRowHeight()))
            {
                var cid = _menuFor;
                _favorites.Forget(cid);
                _host.ForgetCharacter(cid);
                if (_viewing == cid)
                {
                    Select(null, false);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static string FirstName(string name)
    {
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    /// <summary>Above a remembered character's cards: who this is and when these numbers were true.</summary>
    private static void DrawSeenLine(OsAppContext ctx, WalletCharacterSnapshot seen, float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, string.Format(Loc.T("os.wallet_chars_seen"),
            $"{seen.Name} · {seen.World}", seen.TakenAtUtc.ToLocalTime().ToString("g", ctx.Culture)));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    // ------------------------------------------------------------------ the overview

    /// <summary>Every character at once: the summed gil with each character's share under it, then one
    /// card per pinned currency (pinned by ANY character) laying every character's amount side by side.
    /// Read-only: pinning happens on a character's own page, where the choice means something.</summary>
    private void DrawOverview(OsAppContext ctx, IReadOnlyList<WalletCharacterSnapshot> known, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;

        long totalGil = 0;
        var gilRows = new List<(WalletCharacterSnapshot Who, WalletCurrencyRow Gil)>();
        WalletCurrencyRow? anyGil = null;
        foreach (var c in known)
        {
            foreach (var cur in c.Currencies)
            {
                if (cur.IsPrimary)
                {
                    var row = cur.ToRow();
                    totalGil += row.Amount;
                    gilRows.Add((c, row));
                    anyGil ??= row;
                    break;
                }
            }
        }

        // The hero: total gil, then a line per character.
        var lineH = ImGui.GetTextLineHeight();
        var heroH = Px(HeroHeight) + gilRows.Count * (lineH + Px(4f)) + Px(6f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(cardW, heroH);
        OsDrawShared.RoundedGradient(dl, tl, br, Px(18f), WalletApp.TileTopColor, WalletApp.TileBottomColor);
        dl.AddText(new Vector2(tl.X + Px(16f), tl.Y + Px(12f)), OsDrawShared.White(0.82f),
            string.Format(Loc.T("os.wallet_total_gil"), known.Count));

        var amount = totalGil.ToString("N0", ctx.Culture);
        var iconSz = Px(IconSize);
        float y;
        using (UiFonts.H1?.Push())
        {
            var amountSz = ImGui.CalcTextSize(amount);
            var rowY = tl.Y + Px(HeroHeight) - Px(16f) - amountSz.Y;
            var x = tl.X + Px(16f);
            if (anyGil is { } g && _host.GetCurrencyIcon(g.IconId) is { } icon)
            {
                var iconY = rowY + (amountSz.Y - iconSz) * 0.5f;
                dl.AddImage(icon, new Vector2(x, iconY), new Vector2(x + iconSz, iconY + iconSz));
                x += iconSz + Px(10f);
            }
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(x, rowY), 0xFFFFFFFFu, amount);
            y = rowY + amountSz.Y + Px(6f);
        }
        foreach (var (who, gil) in gilRows)
        {
            var share = gil.Amount.ToString("N0", ctx.Culture);
            var shareSz = ImGui.CalcTextSize(share);
            dl.AddText(new Vector2(tl.X + Px(16f), y), OsDrawShared.White(0.75f),
                TruncateToWidth(FirstName(who.Name), cardW - Px(32f) - shareSz.X - Px(10f)));
            dl.AddText(new Vector2(br.X - Px(16f) - shareSz.X, y), OsDrawShared.White(0.92f), share);
            y += lineH + Px(4f);
        }
        ImGui.Dummy(new Vector2(0f, heroH + Px(10f)));

        // Pinned across characters: one card per currency, one row per character that holds it.
        var cids = new List<ulong>(known.Count);
        foreach (var c in known)
        {
            cids.Add(c.ContentId);
        }
        var pinned = _favorites.PinnedAcross(cids);
        if (pinned.Count == 0)
        {
            DrawNotice(Loc.T("os.wallet_pinned_none"), winW);
            ImGui.Dummy(new Vector2(0f, Px(16f)));
            return;
        }

        DrawSectionHeader("os.wallet_pinned_all");
        foreach (var itemId in pinned)
        {
            var rows = new List<WalletCurrencyRow>();
            string? name = null;
            foreach (var c in known)
            {
                foreach (var cur in c.Currencies)
                {
                    if (cur.ItemId != itemId)
                    {
                        continue;
                    }
                    var row = cur.ToRow();
                    name ??= row.Name;
                    // The row widget draws a name; here the name is the character, the icon says which currency.
                    rows.Add(row with { Name = $"{FirstName(c.Name)} · {c.World}" });
                    break;
                }
            }
            if (rows.Count == 0)
            {
                continue;
            }
            rows.Sort((a, b) => b.Amount.CompareTo(a.Amount));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(UiColors.Hint, name ?? string.Empty);
            ImGui.Dummy(new Vector2(0f, Px(2f)));
            DrawSectionCard(ctx, rows.ToArray(), winW, readOnly: true);
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    // ------------------------------------------------------------------ cards

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
    private void DrawSectionCard(OsAppContext ctx, WalletCurrencyRow[] rows, float winW, bool readOnly = false)
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
            if (!readOnly && TryTakeFlight(rows[i].ItemId, rowTl, out var flightTl))
            {
                _deferred = new DeferredRow(rows[i], flightTl, cardW, rowH, rounding, i == 0, i == rows.Length - 1);
            }
            else if (readOnly)
            {
                CurrencyRowDraw.Draw(ctx, dl, rows[i], rowTl, cardW, rowH, rounding, i == 0, i == rows.Length - 1,
                    flying: false, _host.GetCurrencyIcon, star: null);
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
        var owner = PinOwner;
        CurrencyStar? star = owner is { } cid
            ? new CurrencyStar(_favorites.Contains(cid, row.ItemId),
                () => ToggleFavorite(row.ItemId, rowTop, ctx.ReduceMotion))
            : null;
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
