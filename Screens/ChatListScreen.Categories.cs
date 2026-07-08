using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AetherLove.Config;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Category support for the matches list: category rows, the move/reorder drag-and-drop with its
/// animations, the row-departure animation, and the create/edit overlay.</summary>
public partial class ChatListScreen
{
    private const float CategoryRowHeight = 64f;
    private const float DragStartThreshold = 6f;
    private const float DepartDuration = 0.30f;
    private const float FlightDuration = 0.38f;
    private const float PulseDuration = 0.55f;
    private const float SpringBackDuration = 0.18f;
    private const float PickupDuration = 0.12f;
    private const double OpenFadeDuration = 0.20;

    /// <summary>Dark veil over the source row while it is being dragged.</summary>
    private const uint DragDim = 0xAA101010u;

    /// <summary>Drag-ghost pill fill.</summary>
    private const uint GhostPillBg = 0xF01A1420u;

    /// <summary>Red for the destructive delete entry in the category context menu.</summary>
    private const uint DangerMenuText = 0xFF5050E0u;

    private enum DragKind
    {
        None,
        Chat,
        Category,
    }

    /// <summary>A row collapsing out of the current view; the move applies when the collapse completes.</summary>
    private sealed class DepartAnim
    {
        public float T;
        public Guid TargetCategoryId;
    }

    private sealed class Flight
    {
        public float T;
        public Vector2 From;
        public Guid TargetCategoryId;
        public Vector2 LastTarget;
        public ISharedImmediateTexture? Avatar;
    }

    private struct Burst
    {
        public float T;
        public Vector2 Pos;
        public Vector2 Vel;
        public uint Color;
    }

    private sealed class SpringBack
    {
        public float T;
        public Vector2 From;
        public ISharedImmediateTexture? Avatar;
    }

    private readonly Dictionary<Guid, DepartAnim> _departing = new();
    private readonly List<Flight> _flights = new();
    private readonly List<Burst> _bursts = new();
    private readonly Dictionary<Guid, float> _catPulse = new();
    private SpringBack? _springBack;

    private DragKind _dragKind;
    private bool _dragActive;
    private float _dragT;
    private Guid _pressPeerId;
    private Guid _pressCategoryId;
    private Guid _dragPeerId;
    private MatchSummaryDto? _dragMatch;
    private ChatCategoryConfig? _dragCategory;
    private Vector2 _dragSourceRowCenter;
    private Guid _hoverDropCategoryId;
    private int _reorderInsertIndex = -1;

    private readonly Dictionary<Guid, Vector2> _catAvatarCenters = new();

    private Guid _deleteConfirmCatId;
    private string _deleteConfirmCatName = string.Empty;
    private float _deleteConfirmPanelH;
    private double _catOpenFadeAt = -1;

    private bool _editorOpen;
    private Guid _editorCategoryId;
    private string _editorName = string.Empty;
    private int _editorColorIdx;
    private Guid _editorPendingMovePeer;
    private bool _editorPendingMoveInstant;
    private bool _editorFocusPending;
    private float _editorPanelH;

    private static float EaseInCubic(float t) => t * t * t;
    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    private static uint WithAlpha(uint color, float alpha)
        => (color & 0x00FFFFFFu) | ((uint)(Math.Clamp(alpha, 0f, 1f) * 255f) << 24);

    /// <summary>The uppercased first text element of the category name (surrogate/emoji safe).</summary>
    private static string CategoryLetter(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "?";
        }
        return new StringInfo(trimmed).SubstringByTextElements(0, 1).ToUpperInvariant();
    }

    private void DrawCategoryRows(List<ChatCategoryConfig> cats, Dictionary<Guid, int> countByCat,
                                  Dictionary<Guid, int> unreadByCat)
    {
        _catAvatarCenters.Clear();
        _hoverDropCategoryId = Guid.Empty;
        _reorderInsertIndex = -1;
        if (cats.Count == 0)
        {
            return;
        }

        var rowH = Px(CategoryRowHeight);
        var listTop = ImGui.GetCursorScreenPos().Y;
        var mouse = ImGui.GetMousePos();

        // Reorder target: rows sit at fixed offsets, so the insertion slot falls out of the mouse Y directly.
        if (_dragActive && _dragKind == DragKind.Category)
        {
            var slot = (int)MathF.Floor((mouse.Y - listTop + rowH * 0.5f) / rowH);
            _reorderInsertIndex = Math.Clamp(slot, 0, cats.Count);
        }

        for (var i = 0; i < cats.Count; i++)
        {
            DrawCategoryRow(cats[i], countByCat.GetValueOrDefault(cats[i].Id),
                unreadByCat.GetValueOrDefault(cats[i].Id));
        }

        if (_reorderInsertIndex >= 0)
        {
            var y = listTop + _reorderInsertIndex * rowH;
            var dl = ImGui.GetWindowDrawList();
            var w = ImGui.GetContentRegionAvail().X;
            var x0 = ImGui.GetCursorScreenPos().X;
            dl.AddLine(new Vector2(x0 + Px(8f), y), new Vector2(x0 + w - Px(8f), y),
                ThemeService.Current.AccentLightU32, Px(3f));
        }
    }

    private void DrawCategoryRow(ChatCategoryConfig cat, int chatCount, int unread)
    {
        var dl = ImGui.GetWindowDrawList();
        var cursorStart = ImGui.GetCursorScreenPos();
        var rowH = Px(CategoryRowHeight);
        var width = ImGui.GetContentRegionAvail().X;
        var rowMax = cursorStart + new Vector2(width, rowH);
        var beingDragged = _dragActive && _dragKind == DragKind.Category && _dragCategory?.Id == cat.Id;
        var t = ThemeService.Current;

        ImGui.InvisibleButton($"##cat_{cat.Id}", new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered() && !_dragActive;

        if (ImGui.IsItemActivated())
        {
            _pressCategoryId = cat.Id;
        }
        if (ImGui.IsItemActive() && _pressCategoryId == cat.Id && !_dragActive
            && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() > Px(DragStartThreshold))
        {
            StartCategoryDrag(cat);
        }
        if (ImGui.IsItemDeactivated() && _pressCategoryId == cat.Id)
        {
            _pressCategoryId = Guid.Empty;
            if (!_dragActive && ImGui.IsItemHovered())
            {
                _openCategoryId = cat.Id;
                _catOpenFadeAt = -1;
                _router.Navigate(Screen.ChatCategory);
            }
        }

        DrawCategoryContextMenu(cat);

        var dropHover = _dragActive && _dragKind == DragKind.Chat
                        && ImGui.IsMouseHoveringRect(cursorStart, rowMax);
        if (dropHover)
        {
            _hoverDropCategoryId = cat.Id;
        }

        if (hovered)
        {
            dl.AddRectFilled(cursorStart, rowMax, 0x20FFFFFF);
        }
        if (dropHover)
        {
            dl.AddRectFilled(cursorStart, rowMax, WithAlpha(cat.Color, 0.14f));
            dl.AddRect(cursorStart + new Vector2(Px(2f), Px(2f)), rowMax - new Vector2(Px(2f), Px(2f)),
                WithAlpha(cat.Color, 0.9f), Px(6f), ImDrawFlags.None, Px(2f));
        }

        var avatarCenter = cursorStart + new Vector2(Px(40f), rowH * 0.5f);
        var avatarR = Px(22f);
        _catAvatarCenters[cat.Id] = avatarCenter;

        // Arrival pulse: the avatar swells briefly and emits an expanding ring.
        if (_catPulse.TryGetValue(cat.Id, out var pulseT))
        {
            avatarR *= 1f + 0.28f * MathF.Sin(MathF.PI * pulseT);
            var ringR = Px(22f) * (1f + 1.3f * EaseOutCubic(pulseT));
            dl.AddCircle(avatarCenter, ringR, WithAlpha(cat.Color, 1f - pulseT), 0, Px(2.5f));
        }

        dl.AddCircleFilled(avatarCenter, avatarR, cat.Color);
        dl.AddCircle(avatarCenter, avatarR, 0xFFFFFFFF, 0, Px(1.5f));
        if (dropHover)
        {
            var pulse = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 6f);
            dl.AddCircle(avatarCenter, avatarR + Px(4f) + pulse * Px(2f), WithAlpha(cat.Color, 0.9f), 0, Px(2.5f));
        }

        var letter = CategoryLetter(cat.Name);
        var letterSize = ImGui.GetFontSize() * 1.25f;
        var letterSz = ImGui.CalcTextSize(letter) * (letterSize / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), letterSize, avatarCenter - letterSz * 0.5f, 0xFFFFFFFF, letter);

        if (unread > 0)
        {
            var badgeR = Px(9f);
            var badgeCenter = avatarCenter + new Vector2(avatarR - Px(5f), -avatarR + Px(5f));
            dl.AddCircleFilled(badgeCenter, badgeR, UiColors.UnreadBadge);
            var label = unread > 9 ? "9+" : unread.ToString();
            var fsz = ImGui.GetFontSize() * 0.74f;
            var tsize = ImGui.CalcTextSize(label) * (fsz / ImGui.GetFontSize());
            dl.AddText(ImGui.GetFont(), fsz, badgeCenter - tsize * 0.5f, 0xFFFFFFFF, label);
        }

        dl.AddText(cursorStart + Px(80, 11), 0xFFFFFFFF, cat.Name);
        dl.AddText(cursorStart + Px(80, 34), UiColors.TextMuted, Loc.T("chat.category_count", chatCount));

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        var chevSz = ImGui.CalcTextSize(chevron);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(cursorStart.X + width - chevSz.X - Px(12f), avatarCenter.Y - chevSz.Y * 0.42f),
            hovered ? t.AccentLightU32 : UiColors.TextMuted, chevron);
        ImGui.PopFont();

        if (beingDragged)
        {
            dl.AddRectFilled(cursorStart, rowMax, DragDim);
        }

        dl.AddLine(cursorStart + new Vector2(Px(80), rowH), cursorStart + new Vector2(width, rowH), 0xFF333333);
        ImGui.SetCursorScreenPos(cursorStart + new Vector2(0f, rowH));
    }

    private void DrawCategoryContextMenu(ChatCategoryConfig cat)
    {
        if (!ImGui.BeginPopupContextItem($"##catctx_{cat.Id}", ImGuiPopupFlags.MouseButtonRight))
        {
            return;
        }
        ImGui.TextDisabled(cat.Name);
        ImGui.Separator();
        if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.Pen, Loc.T("chat.category_edit")))
        {
            ImGui.CloseCurrentPopup();
            OpenCategoryEditor(cat, Guid.Empty);
        }

        if (ChatScreen.DrawIconMenuItem(FontAwesomeIcon.Trash, Loc.T("chat.category_delete"), DangerMenuText))
        {
            ImGui.CloseCurrentPopup();
            _deleteConfirmCatId = cat.Id;
            _deleteConfirmCatName = cat.Name;
        }
        ImGui.EndPopup();
    }

    /// <summary>The "Move to category" submenu plus "Remove from category", for a chat row's context menu.
    /// Moves triggered here animate the row away in list views and apply instantly in search results.</summary>
    private void DrawCategoryMenuItems(Guid peerId, RowContext ctx, Vector2 rowAvatarCenter)
    {
        DrawCategoryMenuCore(peerId, instant: ctx == RowContext.Search,
            flightFrom: ctx == RowContext.TopLevel ? rowAvatarCenter : null);
    }

    /// <summary>Same items for the in-chat overflow menu; moves apply instantly (no list rows to animate).</summary>
    public void DrawChatOverflowCategoryItems(Guid peerId)
    {
        DrawCategoryMenuCore(peerId, instant: true, flightFrom: null);
    }

    private void DrawCategoryMenuCore(Guid peerId, bool instant, Vector2? flightFrom)
    {
        var cats = _categories.GetCategories();
        var current = _categories.CategoryOf(peerId);
        var dl = ImGui.GetWindowDrawList();

        // The visible label is blank padding sized to DrawIconMenuItem's layout; icon and text are then drawn
        // at that helper's offsets, centred on the item's real rect. Menu items are text-height based, so the
        // widened item spacing grows their hover band to match the frame-height sibling rows.
        var style = ImGui.GetStyle();
        var moveLabel = Loc.T("chat.menu_move_to_category");
        var moveLabelSz = ImGui.CalcTextSize(moveLabel);
        var spaceW = MathF.Max(1f, ImGui.CalcTextSize(" ").X);
        var fontSize = ImGui.GetFontSize();
        var padCount = (int)MathF.Ceiling(MathF.Max(0f, Px(38f) - style.FramePadding.X + moveLabelSz.X) / spaceW);
        // Anchor on the pre-submit cursor: the item's own rect is unreliable once the submenu opens (BeginMenu
        // has already begun the child window), while the label always renders at the cursor and the widened
        // hover band sits symmetrically around it.
        var itemPos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,
            new Vector2(style.ItemSpacing.X, style.FramePadding.Y * 2f));
        var open = ImGui.BeginMenu($"{new string(' ', padCount)}##mvcat");
        ImGui.PopStyleVar();

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var folderIcon = FontAwesomeIcon.FolderOpen.ToIconString();
        var folderSz = ImGui.CalcTextSize(folderIcon);
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(itemPos.X + Px(10f) + (Px(20f) - folderSz.X) * 0.5f,
                        itemPos.Y + (moveLabelSz.Y - folderSz.Y) * 0.5f),
            0xFFEEEEEE, folderIcon);
        ImGui.PopFont();
        dl.AddText(new Vector2(itemPos.X + Px(38f), itemPos.Y), 0xFFEEEEEE, moveLabel);

        if (open)
        {
            var subDl = ImGui.GetWindowDrawList();
            var textInset = Px(28f);
            var newLabel = $"{Loc.T("chat.category_new")}…";
            var rowW = textInset + ImGui.CalcTextSize(newLabel).X + Px(12f);
            foreach (var cat in cats)
            {
                rowW = MathF.Max(rowW, textInset + ImGui.CalcTextSize(cat.Name).X + Px(12f));
            }

            foreach (var cat in cats)
            {
                var isCurrent = current == cat.Id;
                var pos = ImGui.GetCursorScreenPos();
                var rowH = ImGui.GetFrameHeight();
                if (ImGui.Selectable($"##mv_{cat.Id}", isCurrent, ImGuiSelectableFlags.None, new Vector2(rowW, rowH))
                    && !isCurrent)
                {
                    RequestMove(peerId, cat.Id, instant, flightFrom);
                }
                var dotC = new Vector2(pos.X + Px(12f), pos.Y + rowH * 0.5f);
                subDl.AddCircleFilled(dotC, Px(5.5f), cat.Color);
                subDl.AddCircle(dotC, Px(5.5f), 0xFFFFFFFF, 0, Px(1.2f));
                var nameSz = ImGui.CalcTextSize(cat.Name);
                subDl.AddText(new Vector2(pos.X + textInset, pos.Y + (rowH - nameSz.Y) * 0.5f),
                    0xFFEEEEEE, cat.Name);
            }
            if (cats.Count > 0)
            {
                ImGui.Separator();
            }
            {
                var pos = ImGui.GetCursorScreenPos();
                var rowH = ImGui.GetFrameHeight();
                if (ImGui.Selectable("##mvnew", false, ImGuiSelectableFlags.None, new Vector2(rowW, rowH)))
                {
                    OpenCategoryEditor(null, peerId, instant);
                }
                ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
                var plus = FontAwesomeIcon.Plus.ToIconString();
                var plusFsz = ImGui.GetFontSize() * 0.8f;
                var plusSz = ImGui.CalcTextSize(plus) * 0.8f;
                subDl.AddText(ImGui.GetFont(), plusFsz,
                    new Vector2(pos.X + Px(12f) - plusSz.X * 0.5f, pos.Y + (rowH - plusSz.Y) * 0.5f),
                    0xFFBBBBBB, plus);
                ImGui.PopFont();
                var nlSz = ImGui.CalcTextSize(newLabel);
                subDl.AddText(new Vector2(pos.X + textInset, pos.Y + (rowH - nlSz.Y) * 0.5f),
                    0xFFEEEEEE, newLabel);
            }
            ImGui.EndMenu();
        }

        if (current is not null
            && ChatScreen.DrawIconMenuItem(FontAwesomeIcon.FolderMinus, Loc.T("chat.menu_remove_from_category")))
        {
            ImGui.CloseCurrentPopup();
            RequestMove(peerId, Guid.Empty, instant);
        }
    }

    /// <summary>Moves a chat into a category (or out of one when <paramref name="targetCategoryId"/> is Empty),
    /// animating the row's departure unless <paramref name="instant"/> or reduce-motion is set.</summary>
    private void RequestMove(Guid peerId, Guid targetCategoryId, bool instant, Vector2? flightFrom = null)
    {
        if (_categories.CategoryOf(peerId) == (targetCategoryId == Guid.Empty ? null : targetCategoryId))
        {
            return;
        }
        if (instant || AccessibilityService.ReduceMotion)
        {
            _categories.SetCategory(peerId, targetCategoryId == Guid.Empty ? null : targetCategoryId);
            return;
        }

        _departing[peerId] = new DepartAnim { TargetCategoryId = targetCategoryId };

        if (flightFrom is { } from && targetCategoryId != Guid.Empty
            && _catAvatarCenters.TryGetValue(targetCategoryId, out var target))
        {
            _avatarTexCache.TryGetValue(peerId, out var tex);
            _flights.Add(new Flight
            {
                From = from,
                TargetCategoryId = targetCategoryId,
                LastTarget = target,
                Avatar = tex,
            });
        }
    }

    private void StartChatDrag(MatchSummaryDto m)
    {
        _dragKind = DragKind.Chat;
        _dragActive = true;
        _dragT = 0f;
        _dragPeerId = m.PeerProfileId;
        _dragMatch = m;
        _dragCategory = null;
    }

    private void StartCategoryDrag(ChatCategoryConfig cat)
    {
        _dragKind = DragKind.Category;
        _dragActive = true;
        _dragT = 0f;
        _dragCategory = cat;
        _dragMatch = null;
        _dragPeerId = Guid.Empty;
    }

    private void EndDrag()
    {
        _dragKind = DragKind.None;
        _dragActive = false;
        _dragMatch = null;
        _dragCategory = null;
        _dragPeerId = Guid.Empty;
        _reorderInsertIndex = -1;
        _hoverDropCategoryId = Guid.Empty;
        // Also drop the press candidates: the row's button stays ImGui-active until the mouse releases, and a
        // surviving candidate would re-arm the drag on the very next frame after an Escape cancel.
        _pressPeerId = Guid.Empty;
        _pressCategoryId = Guid.Empty;
    }

    /// <summary>Finishes a drag on mouse release: drops a chat into the hovered category (or springs the ghost
    /// back), or applies a category reorder. Escape cancels.</summary>
    private void ResolveDragAndDrop()
    {
        if (!_dragActive)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            if (_dragKind == DragKind.Chat)
            {
                SpawnSpringBack();
            }
            EndDrag();
            return;
        }
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            return;
        }

        // A search finishing mid-drag hides the category rows, leaving the hover/insert state one frame stale.
        if (_dragKind == DragKind.Chat && _dragMatch is not null)
        {
            if (_hoverDropCategoryId != Guid.Empty && !_searchActive)
            {
                RequestMove(_dragPeerId, _hoverDropCategoryId, instant: false, flightFrom: ImGui.GetMousePos());
            }
            else
            {
                SpawnSpringBack();
            }
        }
        else if (_dragKind == DragKind.Category && _dragCategory is not null && _reorderInsertIndex >= 0
                 && !_searchActive)
        {
            var cats = _categories.GetCategories();
            var curIdx = cats.FindIndex(c => c.Id == _dragCategory.Id);
            if (curIdx >= 0)
            {
                var final = _reorderInsertIndex > curIdx ? _reorderInsertIndex - 1 : _reorderInsertIndex;
                _categories.Reorder(_dragCategory.Id, final);
            }
        }
        EndDrag();
    }

    private void SpawnSpringBack()
    {
        if (AccessibilityService.ReduceMotion || _dragMatch is null)
        {
            return;
        }
        _avatarTexCache.TryGetValue(_dragPeerId, out var tex);
        _springBack = new SpringBack { From = ImGui.GetMousePos(), Avatar = tex };
    }

    private void UpdateCategoryAnimations(float dt)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);

        if (_dragActive && _dragT < 1f)
        {
            _dragT = MathF.Min(1f, _dragT + dt / PickupDuration);
        }

        foreach (var (peerId, anim) in _departing.ToList())
        {
            anim.T += dt / DepartDuration;
            if (anim.T >= 1f)
            {
                _departing.Remove(peerId);
                _categories.SetCategory(peerId,
                    anim.TargetCategoryId == Guid.Empty ? null : anim.TargetCategoryId);
            }
        }

        for (var i = _flights.Count - 1; i >= 0; i--)
        {
            var f = _flights[i];
            f.T += dt / FlightDuration;
            if (_catAvatarCenters.TryGetValue(f.TargetCategoryId, out var live))
            {
                f.LastTarget = live;
            }
            if (f.T >= 1f)
            {
                _catPulse[f.TargetCategoryId] = 0f;
                SpawnBurst(f.LastTarget, f.TargetCategoryId);
                _flights.RemoveAt(i);
            }
        }

        foreach (var catId in _catPulse.Keys.ToList())
        {
            var v = _catPulse[catId] + dt / PulseDuration;
            if (v >= 1f)
            {
                _catPulse.Remove(catId);
            }
            else
            {
                _catPulse[catId] = v;
            }
        }

        for (var i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            b.T += dt / PulseDuration;
            b.Pos += b.Vel * dt;
            if (b.T >= 1f)
            {
                _bursts.RemoveAt(i);
            }
            else
            {
                _bursts[i] = b;
            }
        }

        if (_springBack is { } sb)
        {
            sb.T += dt / SpringBackDuration;
            if (sb.T >= 1f)
            {
                _springBack = null;
            }
        }
    }

    private void SpawnBurst(Vector2 center, Guid categoryId)
    {
        var color = _categories.Get(categoryId)?.Color ?? ThemeService.Current.AccentU32;
        const int Count = 10;
        for (var i = 0; i < Count; i++)
        {
            var angle = i * (MathF.Tau / Count);
            var speed = Px(46f) * (0.7f + 0.3f * (i % 3) / 2f);
            _bursts.Add(new Burst
            {
                Pos = center,
                Vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                Color = color,
            });
        }
    }

    /// <summary>Applies any in-flight moves immediately and clears every transient animation/drag state;
    /// called when the screen hides so nothing is lost mid-animation.</summary>
    private void FinalizeCategoryAnimations()
    {
        foreach (var (peerId, anim) in _departing)
        {
            _categories.SetCategory(peerId, anim.TargetCategoryId == Guid.Empty ? null : anim.TargetCategoryId);
        }
        _departing.Clear();
        _flights.Clear();
        _bursts.Clear();
        _catPulse.Clear();
        _springBack = null;
        EndDrag();
        CloseCategoryEditor();
        _deleteConfirmCatId = Guid.Empty;
    }

    /// <summary>Foreground effects: the drag ghost following the cursor, avatars flying into categories,
    /// arrival particle bursts, and the failed-drop spring-back.</summary>
    private void DrawDragOverlays(Vector2 winPos, Vector2 winSize)
    {
        var fg = ImGui.GetForegroundDrawList();
        fg.PushClipRect(winPos, winPos + winSize, true);

        foreach (var f in _flights)
        {
            var t = EaseInCubic(f.T);
            var pos = Vector2.Lerp(f.From, f.LastTarget, t);
            pos.Y -= MathF.Sin(MathF.PI * f.T) * Px(28f);
            var r = Px(22f) * (1f - 0.7f * t);
            var alpha = 1f - 0.55f * t;
            DrawGhostAvatar(fg, pos, r, alpha, f.Avatar);
        }

        foreach (var b in _bursts)
        {
            fg.AddCircleFilled(b.Pos, Px(3f) * (1f - b.T), WithAlpha(b.Color, 1f - b.T));
        }

        if (_springBack is { } sb)
        {
            var t = EaseOutCubic(sb.T);
            var pos = Vector2.Lerp(sb.From, _dragSourceRowCenter, t);
            DrawGhostAvatar(fg, pos, Px(20f), 0.9f - 0.5f * t, sb.Avatar);
        }

        if (_dragActive)
        {
            DrawDragGhost(fg);
        }

        fg.PopClipRect();
    }

    private static void DrawGhostAvatar(ImDrawListPtr dl, Vector2 center, float radius, float alpha,
                                        ISharedImmediateTexture? avatar)
    {
        var wrap = avatar?.GetWrapOrDefault();
        var tint = WithAlpha(0xFFFFFFFFu, alpha);
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, center - new Vector2(radius, radius), center + new Vector2(radius, radius),
                Vector2.Zero, Vector2.One, tint, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(center, radius, WithAlpha(UiColors.AvatarFallback, alpha));
        }
        dl.AddCircle(center, radius, tint, 0, Px(1.5f));
    }

    private void DrawDragGhost(ImDrawListPtr fg)
    {
        var t = ThemeService.Current;
        var scale = 0.9f + 0.1f * EaseOutCubic(_dragT);
        var mouse = ImGui.GetMousePos();
        var pillSize = new Vector2(Px(180f), Px(44f)) * scale;
        var pillMin = mouse + Px(14f, 10f);
        var pillMax = pillMin + pillSize;
        var accent = _dragKind == DragKind.Category && _dragCategory is not null
            ? _dragCategory.Color
            : t.AccentU32;

        // Soft layered shadow behind the pill.
        for (var i = 3; i >= 1; i--)
        {
            fg.AddRectFilled(pillMin + new Vector2(0f, Px(2f) * i), pillMax + new Vector2(0f, Px(2f) * i),
                WithAlpha(0xFF000000u, 0.10f), Px(12f));
        }
        fg.AddRectFilled(pillMin, pillMax, GhostPillBg, Px(12f));
        fg.AddRect(pillMin, pillMax, WithAlpha(accent, 0.95f), Px(12f), ImDrawFlags.None, Px(1.5f));

        var avatarC = new Vector2(pillMin.X + pillSize.Y * 0.5f, (pillMin.Y + pillMax.Y) * 0.5f);
        var avatarR = pillSize.Y * 0.34f;
        string label;
        if (_dragKind == DragKind.Chat && _dragMatch is not null)
        {
            _avatarTexCache.TryGetValue(_dragPeerId, out var tex);
            DrawGhostAvatar(fg, avatarC, avatarR, 1f, tex);
            label = _dragMatch.PeerDisplayName;
        }
        else if (_dragCategory is not null)
        {
            fg.AddCircleFilled(avatarC, avatarR, _dragCategory.Color);
            fg.AddCircle(avatarC, avatarR, 0xFFFFFFFF, 0, Px(1.5f));
            var letter = CategoryLetter(_dragCategory.Name);
            var lsz = ImGui.CalcTextSize(letter);
            fg.AddText(avatarC - lsz * 0.5f, 0xFFFFFFFF, letter);
            label = _dragCategory.Name;
        }
        else
        {
            return;
        }

        var textX = avatarC.X + avatarR + Px(8f);
        var textMaxW = pillMax.X - Px(10f) - textX;
        fg.PushClipRect(new Vector2(textX, pillMin.Y), new Vector2(textX + textMaxW, pillMax.Y), true);
        fg.AddText(new Vector2(textX, (pillMin.Y + pillMax.Y) * 0.5f - ImGui.GetTextLineHeight() * 0.5f),
            0xFFFFFFFF, label);
        fg.PopClipRect();
    }

    /// <summary>Same page ease-in the chat screen uses: covers the content with the window background and fades
    /// it out, so entering a category glides in instead of flashing. No-op under reduce-motion.</summary>
    private void DrawCategoryOpenFade(Vector2 contentTL, Vector2 contentSize)
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        if (_catOpenFadeAt < 0)
        {
            _catOpenFadeAt = ImGui.GetTime();
        }
        var t = (ImGui.GetTime() - _catOpenFadeAt) / OpenFadeDuration;
        if (t >= 1.0)
        {
            return;
        }
        var a = (uint)(Math.Clamp(1.0 - t, 0.0, 1.0) * 255.0);
        var bg = ImGui.GetColorU32(ImGuiCol.WindowBg) & 0x00FFFFFFu;
        ImGui.GetWindowDrawList().AddRectFilled(contentTL, contentTL + contentSize, bg | (a << 24));
    }

    private void OpenCategoryEditor(ChatCategoryConfig? existing, Guid pendingMovePeer, bool pendingMoveInstant = false)
    {
        _editorOpen = true;
        _editorCategoryId = existing?.Id ?? Guid.Empty;
        _editorName = existing?.Name ?? string.Empty;
        _editorColorIdx = existing is null ? 0 : Math.Max(0, Array.IndexOf(UiColors.CategoryPalette, existing.Color));
        _editorPendingMovePeer = pendingMovePeer;
        _editorPendingMoveInstant = pendingMoveInstant;
        _editorFocusPending = true;
        _editorPanelH = 0f;
    }

    /// <summary>Renders the create/edit overlay over the calling screen's window; ChatScreen calls this so
    /// "New category…" from the in-chat menu works without leaving the chat.</summary>
    public void DrawCategoryEditorOverlay()
    {
        DrawCategoryEditor(ImGui.GetWindowPos(), ImGui.GetWindowSize());
    }

    /// <summary>Abandons an open editor; called when a hosting screen hides.</summary>
    public void CloseCategoryEditor()
    {
        _editorOpen = false;
        _editorPendingMovePeer = Guid.Empty;
    }

    /// <summary>Shared in-page overlay shell: dims the window, centres a measured bordered panel, and reports a
    /// scrim click (tap outside the panel). Drawn as a late child so it layers above the list child.</summary>
    private static bool DrawPageOverlayPanel(string id, Vector2 winPos, Vector2 winSize, ref float panelH,
                                             float fallbackH, Action<float> drawContent)
    {
        var dismissed = false;
        ImGui.SetCursorScreenPos(winPos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        using (var overlay = ImRaii.Child($"##overlay_{id}", winSize, false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            if (!overlay.Success)
            {
                return false;
            }

            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

            ImGui.SetCursorScreenPos(winPos);
            if (ImGui.InvisibleButton($"##scrim_{id}", winSize))
            {
                dismissed = true;
            }

            var w = Px(300f);
            var pad = Px(16f, 16f);
            var h = panelH > 0f ? panelH : fallbackH;
            var panelPos = winPos + (winSize - new Vector2(w, h)) * 0.5f;

            ImGui.SetCursorScreenPos(panelPos);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
            using (var panel = ImRaii.Child($"##panel_{id}", new Vector2(w, h), true,
                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
            {
                if (panel.Success)
                {
                    drawContent(ImGui.GetContentRegionAvail().X);
                    panelH = ImGui.GetCursorPosY() + pad.Y;
                }
            }
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }
        return dismissed;
    }

    /// <summary>In-page create/edit overlay: the name field and the colour palette in a centred panel.</summary>
    private void DrawCategoryEditor(Vector2 winPos, Vector2 winSize)
    {
        if (!_editorOpen)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _editorOpen = false;
            return;
        }
        if (DrawPageOverlayPanel("catEditor", winPos, winSize, ref _editorPanelH, Px(240f), DrawCategoryEditorContent))
        {
            _editorOpen = false;
        }
    }

    /// <summary>In-page confirm shown before a category is deleted; its chats return to the main list.</summary>
    private void DrawCategoryDeleteConfirm(Vector2 winPos, Vector2 winSize)
    {
        if (_deleteConfirmCatId == Guid.Empty)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _deleteConfirmCatId = Guid.Empty;
            return;
        }
        if (DrawPageOverlayPanel("catDelete", winPos, winSize, ref _deleteConfirmPanelH, Px(200f),
                DrawCategoryDeleteConfirmContent))
        {
            _deleteConfirmCatId = Guid.Empty;
        }
    }

    private void DrawCategoryDeleteConfirmContent(float innerW)
    {
        ModalUi.Header(innerW, FontAwesomeIcon.Trash, Loc.T("chat.category_delete"), UiColors.Danger);

        ImGui.PushTextWrapPos(innerW);
        ImGui.TextColored(UiColors.Body, Loc.T("chat.category_delete_body", _deleteConfirmCatName));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        var btnW = (innerW - Px(10f)) * 0.5f;
        PushDangerButton();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var confirm = ImGui.Button($"{Loc.T("chat.category_delete_confirm")}##catDelOk", new Vector2(btnW, Px(32f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0f, Px(10f));
        if (ModalUi.Button($"{Loc.T("common.cancel")}##catDelCancel", btnW))
        {
            _deleteConfirmCatId = Guid.Empty;
        }
        if (confirm)
        {
            _categories.Delete(_deleteConfirmCatId);
            _deleteConfirmCatId = Guid.Empty;
        }
    }

    private void DrawCategoryEditorContent(float innerW)
    {
        var t = ThemeService.Current;
        var creating = _editorCategoryId == Guid.Empty;
        ModalUi.Header(innerW, creating ? FontAwesomeIcon.FolderPlus : FontAwesomeIcon.Pen,
            creating ? Loc.T("chat.category_new") : Loc.T("chat.category_edit"), t.AccentLight);

        var color = UiColors.CategoryPalette[Math.Clamp(_editorColorIdx, 0, UiColors.CategoryPalette.Length - 1)];
        var dl = ImGui.GetWindowDrawList();
        var avatarR = Px(16f);
        var rowStart = ImGui.GetCursorScreenPos();
        var avatarC = rowStart + new Vector2(avatarR, ImGui.GetFrameHeight() * 0.5f);
        dl.AddCircleFilled(avatarC, avatarR, color);
        dl.AddCircle(avatarC, avatarR, 0xFFFFFFFF, 0, Px(1.5f));
        var letter = CategoryLetter(_editorName);
        var lsz = ImGui.CalcTextSize(letter);
        dl.AddText(avatarC - lsz * 0.5f, 0xFFFFFFFF, letter);

        ImGui.SetCursorScreenPos(rowStart + new Vector2(avatarR * 2f + Px(10f), 0f));
        ImGui.SetNextItemWidth(innerW - avatarR * 2f - Px(10f));
        if (_editorFocusPending)
        {
            ImGui.SetKeyboardFocusHere();
            _editorFocusPending = false;
        }
        var submit = ImGui.InputTextWithHint("##catName", Loc.T("chat.category_name_hint"),
            ref _editorName, 30, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.Spacing();
        ImGui.Spacing();

        var swatch = Px(24f);
        var gap = Px(7f);
        const int PerRow = 8;
        var gridStart = ImGui.GetCursorScreenPos();
        for (var i = 0; i < UiColors.CategoryPalette.Length; i++)
        {
            var col = i % PerRow;
            var row = i / PerRow;
            var min = gridStart + new Vector2(col * (swatch + gap), row * (swatch + gap));
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton($"##swatch{i}", new Vector2(swatch, swatch)))
            {
                _editorColorIdx = i;
            }
            var hovered = ImGui.IsItemHovered();
            dl.AddRectFilled(min, min + new Vector2(swatch, swatch), UiColors.CategoryPalette[i], Px(6f));
            if (i == _editorColorIdx)
            {
                dl.AddRect(min - new Vector2(Px(2f), Px(2f)), min + new Vector2(swatch + Px(2f), swatch + Px(2f)),
                    0xFFFFFFFF, Px(7f), ImDrawFlags.None, Px(2f));
            }
            else if (hovered)
            {
                dl.AddRect(min, min + new Vector2(swatch, swatch), 0xAAFFFFFF, Px(6f), ImDrawFlags.None, Px(1.5f));
            }
        }
        var rowsUsed = (UiColors.CategoryPalette.Length + PerRow - 1) / PerRow;
        ImGui.SetCursorScreenPos(gridStart + new Vector2(0f, rowsUsed * (swatch + gap)));
        ImGui.Spacing();

        var btnW = (innerW - Px(10f)) * 0.5f;
        var confirm = ModalUi.Button(
            $"{(creating ? Loc.T("chat.category_create") : Loc.T("chat.category_save"))}##catEditorOk", btnW);
        ImGui.SameLine(0f, Px(10f));
        if (ModalUi.Button($"{Loc.T("common.cancel")}##catEditorCancel", btnW))
        {
            _editorOpen = false;
        }

        if ((confirm || submit) && _editorName.Trim().Length > 0)
        {
            SubmitCategoryEditor(color);
        }
    }

    private void SubmitCategoryEditor(uint color)
    {
        var name = _editorName.Trim();
        if (_editorCategoryId == Guid.Empty)
        {
            var cat = _categories.Create(name, color);
            if (_editorPendingMovePeer != Guid.Empty)
            {
                RequestMove(_editorPendingMovePeer, cat.Id, _editorPendingMoveInstant);
            }
        }
        else
        {
            _categories.Update(_editorCategoryId, name, color);
        }
        _editorOpen = false;
        _editorPendingMovePeer = Guid.Empty;
    }
}
