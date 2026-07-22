using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Photos;

/// <summary>iPhone-style Photos app: album cards, a square photo grid, a full-screen viewer, and a cross-app picker mode.</summary>
public sealed class PhotosApp : IAetherApp
{
    private const float CardAspect = 1.15f;
    private const double FadeSeconds = 0.15;

    private static readonly Vector4 TileTopColor = new(0.99f, 0.62f, 0.42f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.55f, 0.30f, 0.85f, 1f);
    private static readonly Vector4 WhiteText = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 MutedText = new(1f, 1f, 1f, 0.55f);
    private static readonly Vector4 CardBorder = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 PanelBg = new(0.11f, 0.10f, 0.13f, 1f);
    private static readonly Vector4 PanelBorder = new(0.32f, 0.30f, 0.38f, 0.65f);
    private static readonly Vector4 DangerFill = new(0.82f, 0.22f, 0.28f, 1f);
    private static readonly Vector4 PlaceholderTop = new(0.46f, 0.38f, 0.60f, 1f);
    private static readonly Vector4 PlaceholderBottom = new(0.27f, 0.22f, 0.40f, 1f);
    private static readonly Vector4 ShadowColor = new(0f, 0f, 0f, 0.32f);
    private static readonly Vector4 DimColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Vector4 ViewerBg = new(0.03f, 0.03f, 0.04f, 1f);
    private static readonly Vector4 NoFill = new(0f, 0f, 0f, 0f);
    private static readonly Vector4 HoverFill = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 GhostFill = new(1f, 1f, 1f, 0.08f);
    private static readonly Vector4 GhostHover = new(1f, 1f, 1f, 0.16f);

    private enum View
    {
        Albums,
        Album,
        Viewer,
        Edit,
    }

    private static readonly (ImageFilter Filter, string Key)[] Filters =
    [
        (ImageFilter.None, "os.filter_original"),
        (ImageFilter.Mono, "os.filter_mono"),
        (ImageFilter.Noir, "os.filter_noir"),
        (ImageFilter.Sepia, "os.filter_sepia"),
        (ImageFilter.Retro, "os.filter_retro"),
        (ImageFilter.Cool, "os.filter_cool"),
        (ImageFilter.Vivid, "os.filter_vivid"),
        (ImageFilter.Fade, "os.filter_fade"),
    ];

    private enum ConfirmResult
    {
        Pending,
        Confirmed,
        Cancelled,
    }

    private readonly Func<string> name;
    private readonly IPhotoLibrary library;
    private readonly IAppCapabilities caps;
    private readonly Dictionary<string, float> aspectCache = new();

    private View view = View.Albums;
    private string albumId = "";
    private string photoId = "";
    private Action<string>? cameraReply;
    private bool pickerArmed;
    private bool newAlbumPrompt;
    private string newAlbumName = "";
    private bool renamingAlbum;
    private string albumNameEdit = "";
    private bool renamingPhoto;
    private string photoNameEdit = "";
    private sealed class EditState
    {
        public string Source = "";
        public string AlbumId = "";
        public string Name = "";
        public ImageFilter Filter = ImageFilter.None;
        public int Brightness;
        public int Contrast;
        public int TintHue;
        public int TintStrength;
        public int Tab;
        public string Shown = "";

        // "" marks a combination still rendering; the worker callback swaps in the finished path.
        public readonly ConcurrentDictionary<string, string> Rendered = new();
    }

    private bool confirmDeleteAlbum;
    private bool confirmDeletePhoto;
    private bool movingPhoto;
    private bool focusPending;
    private bool resetAlbumScroll;
    private DateTime fadeStartUtc = DateTime.MinValue;
    private EditState? edit;

    public PhotosApp(Func<string> name, IPhotoLibrary library, IAppCapabilities caps)
    {
        this.name = name;
        this.library = library;
        this.caps = caps;
    }

    public string Id => "photos";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Images;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void Draw(OsAppContext ctx)
    {
        switch (this.view)
        {
            case View.Albums:
                this.DrawAlbums(ctx);
                break;
            case View.Album:
                this.DrawAlbumView(ctx);
                break;
            case View.Viewer:
                this.DrawViewer(ctx);
                break;
            case View.Edit:
                this.DrawEdit(ctx);
                break;
        }
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.CameraCaptured)
        {
            if (OsIntents.TryGetCameraShot(intent, out var shotPath, out _))
            {
                var reply = this.cameraReply;
                this.cameraReply = null;
                reply?.Invoke(shotPath);
            }
            return;
        }
        if (intent.Type != OsIntents.PickPhoto)
        {
            return;
        }
        this.pickerArmed = true;
        this.newAlbumPrompt = false;
        this.confirmDeleteAlbum = false;
        this.confirmDeletePhoto = false;
        this.movingPhoto = false;
        this.renamingAlbum = false;
        this.renamingPhoto = false;
        if (this.view is View.Viewer or View.Edit)
        {
            this.view = View.Album;
            this.edit = null;
        }
    }

    private void DrawAlbums(OsAppContext ctx)
    {
        var flags = this.newAlbumPrompt ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;
        ImGui.BeginChild("##photosAlbums", ImGui.GetContentRegionAvail(), false, flags);
        if (this.newAlbumPrompt)
        {
            ImGui.BeginDisabled();
        }

        var winPos = ImGui.GetWindowPos();
        var winW = ImGui.GetWindowSize().X;
        var pad = ctx.Px(14f);

        if (this.pickerArmed)
        {
            this.DrawPickerBanner(ctx);
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        ImGui.SetCursorPosX(pad);
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextUnformatted(ctx.Localize("os.photos_title"));
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));

        var albums = this.library.Albums;
        var gap = ctx.Px(10f);
        var cardW = (winW - pad * 2f - gap) * 0.5f;
        var cardH = MathF.Round(cardW / CardAspect);
        var origin = ImGui.GetCursorScreenPos();
        var cells = albums.Count + 1;
        for (var i = 0; i < cells; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var tl = new Vector2(winPos.X + pad + col * (cardW + gap), origin.Y + row * (cardH + gap));
            var size = new Vector2(cardW, cardH);
            if (i == 0)
            {
                this.DrawNewAlbumCard(ctx, tl, size);
            }
            else
            {
                this.DrawAlbumCard(ctx, albums[i - 1], tl, size);
            }
        }
        var rows = (cells + 1) / 2;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(winW, rows * cardH + (rows - 1) * gap + ctx.Px(14f)));

        if (this.newAlbumPrompt)
        {
            ImGui.EndDisabled();
            this.DrawNewAlbumPrompt(ctx);
        }
        ImGui.EndChild();
    }

    private void DrawAlbumCard(OsAppContext ctx, PhotoAlbum album, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##album{album.Id}", size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var br = tl + size;
        var rounding = ctx.Px(16f);

        dl.AddRectFilled(tl + ctx.Px(0f, 3f), br + ctx.Px(0f, 3f), U32(ShadowColor), rounding);
        var tex = album.CoverPath == null ? null : this.caps.Textures.Get(album.CoverPath);
        if (tex is { } handle)
        {
            var (uv0, uv1) = CoverUv(this.AspectOf(album.CoverPath!), size.X / size.Y);
            dl.AddImageRounded(handle, tl, br, uv0, uv1, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            RoundedGradientV(dl, tl, br, rounding, PlaceholderTop, PlaceholderBottom, ImDrawFlags.RoundCornersAll);
            AddIconCentered(dl, FontAwesomeIcon.Images, size.Y * 0.28f, tl + new Vector2(size.X * 0.5f, size.Y * 0.42f), U32(new Vector4(1f, 1f, 1f, 0.45f)));
        }

        RoundedGradientV(dl, new Vector2(tl.X, br.Y - size.Y * 0.46f), br, rounding, new Vector4(0f, 0f, 0f, 0f), new Vector4(0f, 0f, 0f, 0.78f), ImDrawFlags.RoundCornersBottom);

        var inset = ctx.Px(10f);
        var countPx = ImGui.GetFontSize() * 0.82f;
        var countY = br.Y - inset - countPx;
        var nameY = countY - ImGui.GetTextLineHeight() - ctx.Px(1f);
        dl.PushClipRect(new Vector2(tl.X + inset * 0.5f, tl.Y), new Vector2(br.X - inset * 0.5f, br.Y), true);
        dl.AddText(new Vector2(tl.X + inset, nameY), U32(WhiteText), album.Name);
        dl.AddText(ImGui.GetFont(), countPx, new Vector2(tl.X + inset, countY), U32(new Vector4(1f, 1f, 1f, 0.70f)), album.Count.ToString(CultureInfo.InvariantCulture));
        dl.PopClipRect();

        if (hovered)
        {
            dl.AddRectFilled(tl, br, U32(new Vector4(1f, 1f, 1f, 0.06f)), rounding);
            dl.AddRect(tl, br, U32(ctx.Theme.Accent), rounding, ImDrawFlags.RoundCornersAll, ctx.Px(1.4f));
        }
        dl.AddRect(tl, br, U32(CardBorder), rounding, ImDrawFlags.RoundCornersAll, 1f);

        if (clicked)
        {
            this.OpenAlbum(album.Id);
        }
    }

    private void DrawNewAlbumCard(OsAppContext ctx, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##photosNewAlbum", size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var br = tl + size;
        var rounding = ctx.Px(16f);

        dl.AddRectFilled(tl + ctx.Px(0f, 3f), br + ctx.Px(0f, 3f), U32(ShadowColor), rounding);
        dl.AddRectFilled(tl, br, U32(new Vector4(1f, 1f, 1f, hovered ? 0.09f : 0.05f)), rounding);
        var borderCol = hovered ? ctx.Theme.Accent : new Vector4(1f, 1f, 1f, 0.22f);
        dl.AddRect(tl, br, U32(borderCol), rounding, ImDrawFlags.RoundCornersAll, ctx.Px(1.2f));

        var center = (tl + br) * 0.5f;
        AddIconCentered(dl, FontAwesomeIcon.Plus, size.Y * 0.24f, center - new Vector2(0f, size.Y * 0.10f), U32(new Vector4(1f, 1f, 1f, 0.80f)));
        var label = ctx.Localize("os.photos_new_album");
        var ts = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, br, true);
        dl.AddText(new Vector2(center.X - ts.X * 0.5f, center.Y + size.Y * 0.10f), U32(MutedText), label);
        dl.PopClipRect();

        if (clicked)
        {
            this.newAlbumPrompt = true;
            this.newAlbumName = "";
            this.focusPending = true;
        }
    }

    private void DrawNewAlbumPrompt(OsAppContext ctx)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(DimColor));

        var padIn = ctx.Px(16f);
        var panelW = MathF.Min(winSize.X - ctx.Px(44f), ctx.Px(270f));
        var innerW = panelW - padIn * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var inputH = lineH + ctx.Px(14f);
        var btnH = ctx.Px(34f);
        var panelH = padIn + lineH + ctx.Px(8f) + inputH + ctx.Px(14f) + btnH + padIn;
        var panelTL = winPos + (winSize - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL + ctx.Px(0f, 4f), panelBR + ctx.Px(0f, 4f), U32(ShadowColor), ctx.Px(16f));
        dl.AddRectFilled(panelTL, panelBR, U32(PanelBg), ctx.Px(16f));
        dl.AddRect(panelTL, panelBR, U32(PanelBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);
        dl.AddText(panelTL + new Vector2(padIn, padIn), U32(MutedText), ctx.Localize("os.photos_album_name"));

        ImGui.SetCursorScreenPos(panelTL + new Vector2(padIn, padIn + lineH + ctx.Px(8f)));
        ImGui.SetNextItemWidth(innerW);
        var commit = this.OverlayInput(ctx, "##newAlbumName", "", ref this.newAlbumName);

        var btnW = (innerW - ctx.Px(8f)) * 0.5f;
        var btnY = panelBR.Y - padIn - btnH;
        if (PanelButton(ctx, "##newAlbumCancel", ctx.Localize("common.cancel"), new Vector2(panelTL.X + padIn, btnY), new Vector2(btnW, btnH), ctx.Theme.ChipFill))
        {
            this.newAlbumPrompt = false;
        }
        if (PanelButton(ctx, "##newAlbumOk", ctx.Localize("common.ok"), new Vector2(panelTL.X + padIn + btnW + ctx.Px(8f), btnY), new Vector2(btnW, btnH), ctx.Theme.Accent))
        {
            commit = true;
        }
        if (commit && this.newAlbumName.Trim().Length > 0)
        {
            var id = this.library.CreateAlbum(this.newAlbumName.Trim());
            this.newAlbumPrompt = false;
            this.OpenAlbum(id);
        }

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel widgets stay clickable.
        ImGui.SetCursorScreenPos(winPos);
        if (ImGui.InvisibleButton("##newAlbumScrim", winSize) && !InRect(ImGui.GetMousePos(), panelTL, panelBR))
        {
            this.newAlbumPrompt = false;
        }
    }

    private string photoSearch = "";

    private void DrawAlbumView(OsAppContext ctx)
    {
        var album = this.library.Albums.FirstOrDefault(a => a.Id == this.albumId);
        if (album == null)
        {
            this.view = View.Albums;
            this.DrawAlbums(ctx);
            return;
        }

        var flags = this.confirmDeleteAlbum ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None;
        ImGui.BeginChild("##photosAlbum", ImGui.GetContentRegionAvail(), false, flags);
        if (this.resetAlbumScroll)
        {
            ImGui.SetScrollY(0f);
            this.resetAlbumScroll = false;
        }
        if (this.confirmDeleteAlbum)
        {
            ImGui.BeginDisabled();
        }

        var winPos = ImGui.GetWindowPos();
        var winW = ImGui.GetWindowSize().X;
        var pad = ctx.Px(14f);

        if (this.pickerArmed)
        {
            this.DrawPickerBanner(ctx);
        }

        this.DrawAlbumTopBar(ctx, album, winPos, winW, pad);
        this.DrawAlbumActions(ctx, album, winPos, winW, pad);
        this.DrawSearchRow(ctx, winPos, winW, pad);

        var photos = this.library.Photos(album.Id);
        if (this.photoSearch.Trim() is { Length: > 0 } query)
        {
            photos = photos
                .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (p.Location?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();
        }
        if (photos.Count == 0)
        {
            this.DrawEmptyState(ctx, winPos, winW);
        }
        else
        {
            this.DrawPhotoGrid(ctx, photos, winPos, winW, pad);
        }

        if (this.confirmDeleteAlbum)
        {
            ImGui.EndDisabled();
            var result = this.DrawConfirmOverlay(ctx, "albumDelete");
            if (result == ConfirmResult.Confirmed)
            {
                this.library.DeleteAlbum(album.Id);
                this.confirmDeleteAlbum = false;
                this.view = View.Albums;
            }
            if (result == ConfirmResult.Cancelled)
            {
                this.confirmDeleteAlbum = false;
            }
        }
        ImGui.EndChild();
    }

    private void DrawAlbumTopBar(OsAppContext ctx, PhotoAlbum album, Vector2 winPos, float winW, float pad)
    {
        var barTL = ImGui.GetCursorScreenPos();
        var barH = ctx.Px(46f);
        var cy = barTL.Y + barH * 0.5f;
        var r = ctx.Px(16f);
        var dl = ImGui.GetWindowDrawList();

        if (RoundIconButton("##albumBack", FontAwesomeIcon.ChevronLeft, new Vector2(winPos.X + pad + r * 0.5f, cy), r, ctx.Px(14f), NoFill, HoverFill, WhiteText))
        {
            this.view = View.Albums;
            this.renamingAlbum = false;
        }

        var trashCX = winPos.X + winW - pad - r * 0.5f;
        var folderCX = trashCX - r * 2f - ctx.Px(6f);
        var titleX = winPos.X + pad + r * 1.7f + ctx.Px(8f);
        if (this.renamingAlbum)
        {
            var inputH = ImGui.GetTextLineHeight() + ctx.Px(14f);
            ImGui.SetCursorScreenPos(new Vector2(titleX, cy - inputH * 0.5f));
            ImGui.SetNextItemWidth(folderCX - r - ctx.Px(8f) - titleX);
            var commit = this.OverlayInput(ctx, "##albumRename", ctx.Localize("os.photos_rename"), ref this.albumNameEdit);
            if (commit)
            {
                this.library.RenameAlbum(album.Id, this.albumNameEdit);
                this.renamingAlbum = false;
            }
            else if (ImGui.IsItemDeactivated())
            {
                this.renamingAlbum = false;
            }
        }
        else
        {
            var titleMaxW = folderCX - r - ctx.Px(46f) - titleX;
            float titleW;
            using (ctx.HeadingFont?.Push())
            {
                var ts = ImGui.CalcTextSize(album.Name);
                titleW = MathF.Min(ts.X, titleMaxW);
                dl.PushClipRect(new Vector2(titleX, barTL.Y), new Vector2(titleX + titleW, barTL.Y + barH), true);
                dl.AddText(new Vector2(titleX, cy - ts.Y * 0.5f), U32(WhiteText), album.Name);
                dl.PopClipRect();
            }
            if (RoundIconButton("##albumRenameTgl", FontAwesomeIcon.Pen, new Vector2(titleX + titleW + ctx.Px(16f), cy), ctx.Px(13f), ctx.Px(11f), NoFill, HoverFill, MutedText))
            {
                this.renamingAlbum = true;
                this.albumNameEdit = album.Name;
                this.focusPending = true;
            }
        }

        if (RoundIconButton("##albumOpenDisk", FontAwesomeIcon.ExternalLinkAlt, new Vector2(folderCX, cy), r, ctx.Px(12f), NoFill, HoverFill, MutedText, ctx.Localize("os.tt_open_folder"))
            && this.library.AlbumFolder(album.Id) is { } diskDir)
        {
            this.caps.System.OpenFolder(diskDir);
        }

        if (RoundIconButton("##albumDelete", FontAwesomeIcon.Trash, new Vector2(trashCX, cy), r, ctx.Px(13f), NoFill, HoverFill, new Vector4(1f, 1f, 1f, 0.75f)))
        {
            this.confirmDeleteAlbum = true;
        }

        ImGui.SetCursorScreenPos(barTL);
        ImGui.Dummy(new Vector2(winW, barH));
    }

    private void DrawAlbumActions(OsAppContext ctx, PhotoAlbum album, Vector2 winPos, float winW, float pad)
    {
        var rowTL = ImGui.GetCursorScreenPos();
        var btnH = ctx.Px(38f);
        var gap = ctx.Px(10f);
        var btnW = (winW - pad * 2f - gap) * 0.5f;
        if (PillButton(ctx, "##photosImport", FontAwesomeIcon.FolderOpen, ctx.Localize("os.photos_import"), new Vector2(winPos.X + pad, rowTL.Y), new Vector2(btnW, btnH)))
        {
            var albumId = album.Id;
            var request = new ImagePickRequest(ctx.Localize("os.photos_import"), ctx.Localize("profile.image_files_filter") + "{.png,.jpg,.jpeg}");
            this.caps.Images.PickFile(request, path => this.library.AddPhoto(albumId, path, null));
        }
        if (PillButton(ctx, "##photosSelfie", FontAwesomeIcon.Camera, ctx.Localize("os.photos_selfie"), new Vector2(winPos.X + pad + btnW + gap, rowTL.Y), new Vector2(btnW, btnH)))
        {
            var albumId = album.Id;
            var selfieName = ctx.Localize("os.photos_selfie_name");
            this.cameraReply = path => this.library.AddPhoto(albumId, path, selfieName);
            ctx.Shell.SendIntent("camera", OsIntents.CreateCameraCapture(this.Id, 1f, 128));
        }
        ImGui.SetCursorScreenPos(rowTL);
        ImGui.Dummy(new Vector2(winW, btnH + ctx.Px(12f)));
    }

    private void DrawSearchRow(OsAppContext ctx, Vector2 winPos, float winW, float pad)
    {
        ImGui.SetCursorScreenPos(new Vector2(winPos.X + pad, ImGui.GetCursorScreenPos().Y));
        ImGui.SetNextItemWidth(winW - pad * 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(10f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, GhostFill);
        ImGui.InputTextWithHint("##photosSearch", ctx.Localize("os.photos_search_hint"), ref this.photoSearch, 64);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        ImGui.Dummy(new Vector2(0f, ctx.Px(8f)));
    }

    private void DrawPhotoGrid(OsAppContext ctx, IReadOnlyList<PhotoItem> photos, Vector2 winPos, float winW, float pad)
    {
        var gap = ctx.Px(4f);
        var cell = MathF.Floor((winW - pad * 2f - gap * 2f) / 3f);
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var headerH = ImGui.GetTextLineHeight() + ctx.Px(10f);
        var y = origin.Y;
        var i = 0;
        while (i < photos.Count)
        {
            var date = photos[i].AddedAtUtc.ToLocalTime().Date;
            dl.AddText(new Vector2(winPos.X + pad, y + ctx.Px(2f)), U32(WhiteText), DateHeader(ctx, date));
            y += headerH;
            var start = i;
            while (i < photos.Count && photos[i].AddedAtUtc.ToLocalTime().Date == date)
            {
                i++;
            }
            for (var j = start; j < i; j++)
            {
                var col = (j - start) % 3;
                var row = (j - start) / 3;
                var tl = new Vector2(winPos.X + pad + col * (cell + gap), y + row * (cell + gap));
                this.DrawThumb(ctx, photos[j], tl, cell);
            }
            var rows = (i - start + 2) / 3;
            y += rows * cell + (rows - 1) * gap + ctx.Px(12f);
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(winW, y - origin.Y + ctx.Px(4f)));
    }

    private static string DateHeader(OsAppContext ctx, DateTime date)
    {
        var today = DateTime.Now.Date;
        if (date == today)
        {
            return ctx.Localize("os.photos_today");
        }
        if (date == today.AddDays(-1))
        {
            return ctx.Localize("os.photos_yesterday");
        }
        return date.ToString("d MMMM yyyy", ctx.Culture);
    }

    private void DrawThumb(OsAppContext ctx, PhotoItem photo, Vector2 tl, float side)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##ph{photo.Id}", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var br = tl + new Vector2(side, side);
        var rounding = ctx.Px(10f);

        var tex = this.caps.Textures.Get(photo.Path);
        if (tex is { } handle)
        {
            var (uv0, uv1) = CoverUv(this.AspectOf(photo.Path), 1f);
            dl.AddImageRounded(handle, tl, br, uv0, uv1, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(tl, br, U32(PlaceholderBottom), rounding);
            AddIconCentered(dl, FontAwesomeIcon.Images, side * 0.30f, (tl + br) * 0.5f, U32(MutedText));
        }
        dl.AddRect(tl, br, U32(CardBorder), rounding, ImDrawFlags.RoundCornersAll, 1f);
        if (hovered)
        {
            dl.AddRectFilled(tl, br, U32(new Vector4(1f, 1f, 1f, 0.08f)), rounding);
            dl.AddRect(tl, br, U32(ctx.Theme.Accent), rounding, ImDrawFlags.RoundCornersAll, ctx.Px(1.3f));
        }

        if (clicked)
        {
            if (this.pickerArmed)
            {
                ctx.Shell.SendIntent("messenger", OsIntents.CreatePath(OsIntents.PhotoPicked, photo.Path));
                this.pickerArmed = false;
            }
            else
            {
                this.photoId = photo.Id;
                this.view = View.Viewer;
                this.fadeStartUtc = DateTime.UtcNow;
                this.renamingPhoto = false;
                this.confirmDeletePhoto = false;
                this.movingPhoto = false;
            }
        }
    }

    private void DrawEmptyState(OsAppContext ctx, Vector2 winPos, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var top = ImGui.GetCursorScreenPos();
        var winH = ImGui.GetWindowSize().Y;
        var remaining = MathF.Max(winPos.Y + winH - top.Y, ctx.Px(180f));
        var center = new Vector2(winPos.X + winW * 0.5f, top.Y + remaining * 0.5f);
        AddIconCentered(dl, FontAwesomeIcon.Images, ctx.Px(40f), center - new Vector2(0f, ctx.Px(26f)), U32(new Vector4(1f, 1f, 1f, 0.30f)));

        var text = ctx.Localize("os.photos_empty");
        var wrapW = winW - ctx.Px(56f);
        var ts = ImGui.CalcTextSize(text, false, wrapW);
        ImGui.SetCursorScreenPos(new Vector2(winPos.X + (winW - ts.X) * 0.5f, center.Y + ctx.Px(4f)));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ts.X + 2f);
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();

        ImGui.SetCursorScreenPos(top);
        ImGui.Dummy(new Vector2(winW, remaining));
    }

    private void DrawViewer(OsAppContext ctx)
    {
        var photos = this.library.Photos(this.albumId);
        var index = -1;
        for (var i = 0; i < photos.Count; i++)
        {
            if (photos[i].Id == this.photoId)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            this.view = View.Album;
            this.DrawAlbumView(ctx);
            return;
        }
        var photo = photos[index];

        ImGui.BeginChild("##photosViewer", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        var overlayActive = this.confirmDeletePhoto || this.movingPhoto;
        if (overlayActive)
        {
            ImGui.BeginDisabled();
        }

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(ViewerBg));

        var tex = this.caps.Textures.Get(photo.Path);
        if (tex is { } handle)
        {
            var texAspect = this.AspectOf(photo.Path);
            var destAspect = winSize.X / winSize.Y;
            var imgSize = texAspect >= destAspect
                ? new Vector2(winSize.X, winSize.X / texAspect)
                : new Vector2(winSize.Y * texAspect, winSize.Y);
            var imgTL = winPos + (winSize - imgSize) * 0.5f;
            dl.AddImage(handle, imgTL, imgTL + imgSize, Vector2.Zero, Vector2.One, U32(new Vector4(1f, 1f, 1f, this.FadeAlpha(ctx))));
        }
        else
        {
            AddIconCentered(dl, FontAwesomeIcon.Images, ctx.Px(42f), winPos + winSize * 0.5f, U32(new Vector4(1f, 1f, 1f, 0.25f)));
        }

        this.DrawViewerTopBar(ctx, photo, winPos, winSize, dl);
        this.DrawViewerBottomBar(ctx, photos, index, winPos, winSize, dl);

        if (overlayActive)
        {
            ImGui.EndDisabled();
        }
        if (this.confirmDeletePhoto)
        {
            var result = this.DrawConfirmOverlay(ctx, "photoDelete");
            if (result == ConfirmResult.Confirmed)
            {
                this.library.DeletePhoto(photo.Id);
                this.confirmDeletePhoto = false;
                this.view = View.Album;
            }
            if (result == ConfirmResult.Cancelled)
            {
                this.confirmDeletePhoto = false;
            }
        }
        else if (this.movingPhoto)
        {
            this.DrawMoveOverlay(ctx, photo);
        }
        ImGui.EndChild();
    }

    private void DrawMoveOverlay(OsAppContext ctx, PhotoItem photo)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(DimColor));

        var albums = this.library.Albums.Where(a => a.Id != photo.AlbumId).ToArray();
        var padIn = ctx.Px(16f);
        var rowH = ctx.Px(40f);
        var lineH = ImGui.GetTextLineHeight();
        var panelW = MathF.Min(winSize.X - ctx.Px(44f), ctx.Px(270f));
        var innerW = panelW - padIn * 2f;
        var listH = MathF.Min(MathF.Max(albums.Length, 1) * rowH, rowH * 5f);
        var panelH = padIn + lineH + ctx.Px(10f) + listH + padIn;
        var panelTL = winPos + (winSize - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL + ctx.Px(0f, 4f), panelBR + ctx.Px(0f, 4f), U32(ShadowColor), ctx.Px(16f));
        dl.AddRectFilled(panelTL, panelBR, U32(PanelBg), ctx.Px(16f));
        dl.AddRect(panelTL, panelBR, U32(PanelBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);
        dl.AddText(panelTL + new Vector2(padIn, padIn), U32(MutedText), ctx.Localize("os.photos_move_title"));

        ImGui.SetCursorScreenPos(panelTL + new Vector2(padIn, padIn + lineH + ctx.Px(10f)));
        ImGui.BeginChild("##photosMoveList", new Vector2(innerW, listH), false, ImGuiWindowFlags.None);
        if (albums.Length == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
            ImGui.TextUnformatted(ctx.Localize("os.photos_move_none"));
            ImGui.PopStyleColor();
        }
        var listDl = ImGui.GetWindowDrawList();
        foreach (var album in albums)
        {
            var tl = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton($"##moveTo{album.Id}", new Vector2(innerW, rowH));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                listDl.AddRectFilled(tl, tl + new Vector2(innerW, rowH), U32(HoverFill), ctx.Px(9f));
            }
            listDl.PushClipRect(tl, tl + new Vector2(innerW - ctx.Px(34f), rowH), true);
            listDl.AddText(new Vector2(tl.X + ctx.Px(10f), tl.Y + (rowH - lineH) * 0.5f), U32(WhiteText), album.Name);
            listDl.PopClipRect();
            var count = album.Count.ToString(CultureInfo.InvariantCulture);
            var countW = ImGui.CalcTextSize(count).X;
            listDl.AddText(new Vector2(tl.X + innerW - ctx.Px(10f) - countW, tl.Y + (rowH - lineH) * 0.5f), U32(MutedText), count);
            if (clicked)
            {
                this.library.MovePhoto(photo.Id, album.Id);
                this.movingPhoto = false;
                this.view = View.Album;
            }
        }
        ImGui.EndChild();

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel widgets stay clickable.
        ImGui.SetCursorScreenPos(winPos);
        if (ImGui.InvisibleButton("##photosMoveScrim", winSize) && !InRect(ImGui.GetMousePos(), panelTL, panelBR))
        {
            this.movingPhoto = false;
        }
    }

    private void DrawEdit(OsAppContext ctx)
    {
        var edit = this.edit;
        if (edit == null || !File.Exists(edit.Source))
        {
            this.edit = null;
            this.view = View.Album;
            this.DrawAlbumView(ctx);
            return;
        }

        ImGui.BeginChild("##photosEdit", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(ViewerBg));

        var pad = ctx.Px(14f);
        var width = winSize.X - pad * 2f;
        var btnH = ctx.Px(32f);
        var chipH = ctx.Px(24f);
        var chipGap = ctx.Px(6f);
        var tabH = ctx.Px(22f);
        var sliderRowH = ctx.Px(26f);
        var editH = tabH + ctx.Px(8f) + (edit.Tab == 0 ? chipH * 2f + chipGap : sliderRowH * 4f) + ctx.Px(10f);
        var controlsTop = winPos.Y + winSize.Y - ctx.Px(16f) - btnH - editH;

        var shownPath = edit.Shown.Length > 0 ? edit.Shown : edit.Source;
        var tex = this.caps.Textures.Get(shownPath);
        var imgTop = winPos.Y + ctx.Px(14f);
        var area = new Vector2(winSize.X, controlsTop - ctx.Px(10f) - imgTop);
        if (tex is { } handle && area.Y > 0f)
        {
            var texAspect = this.AspectOf(shownPath);
            var destAspect = area.X / area.Y;
            var imgSize = texAspect >= destAspect
                ? new Vector2(area.X, area.X / texAspect)
                : new Vector2(area.Y * texAspect, area.Y);
            var imgTL = new Vector2(winPos.X, imgTop) + (area - imgSize) * 0.5f;
            dl.AddImage(handle, imgTL, imgTL + imgSize);
        }

        if (IsApplying(edit))
        {
            var applying = ctx.Localize("os.filter_applying");
            var appSize = ImGui.CalcTextSize(applying);
            dl.AddText(new Vector2(winPos.X + (winSize.X - appSize.X) * 0.5f, controlsTop - ctx.Px(10f) - appSize.Y - ctx.Px(6f)),
                U32(MutedText), applying);
        }

        var cardTL = new Vector2(winPos.X + ctx.Px(8f), controlsTop - ctx.Px(12f));
        var cardBR = new Vector2(winPos.X + winSize.X - ctx.Px(8f), winPos.Y + winSize.Y - ctx.Px(8f));
        dl.AddRectFilled(cardTL, cardBR, U32(new Vector4(0.07f, 0.07f, 0.10f, 0.94f)), ctx.Px(16f));
        dl.AddRect(cardTL, cardBR, U32(CardBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);

        this.DrawEditPanel(ctx, edit, new Vector2(winPos.X + pad, controlsTop), width, tabH, chipH, chipGap, sliderRowH);

        var btnGap = ctx.Px(8f);
        var btnW = (width - btnGap) * 0.5f;
        var btnY = winPos.Y + winSize.Y - ctx.Px(16f) - btnH;
        if (PanelButton(ctx, "##photosEditCancel", ctx.Localize("common.cancel"), new Vector2(winPos.X + pad, btnY), new Vector2(btnW, btnH), ctx.Theme.ChipFill))
        {
            this.edit = null;
            this.view = View.Viewer;
        }
        var canSave = !IsNeutral(edit) && !IsApplying(edit) && shownPath != edit.Source;
        if (!canSave)
        {
            ImGui.BeginDisabled();
        }
        if (PanelButton(ctx, "##photosEditSave", ctx.Localize("os.photos_save_copy"), new Vector2(winPos.X + pad + btnW + btnGap, btnY), new Vector2(btnW, btnH), canSave ? ctx.Theme.Accent : ctx.Theme.ChipFill))
        {
            this.library.AddPhoto(edit.AlbumId, shownPath, edit.Name);
            this.edit = null;
            this.view = View.Album;
            this.resetAlbumScroll = true;
        }
        if (!canSave)
        {
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    private void DrawEditPanel(OsAppContext ctx, EditState edit, Vector2 tl, float width, float tabH, float chipH, float chipGap, float sliderRowH)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(width, tabH), U32(GhostFill), tabH * 0.5f);
        var tabW = width * 0.5f;
        for (var t = 0; t < 2; t++)
        {
            var tabTL = new Vector2(tl.X + t * tabW, tl.Y);
            var tabSelected = edit.Tab == t;
            ImGui.SetCursorScreenPos(tabTL);
            if (ImGui.InvisibleButton($"##photosEditTab{t}", new Vector2(tabW, tabH)))
            {
                edit.Tab = t;
            }
            var tabHovered = ImGui.IsItemHovered();
            if (tabSelected)
            {
                dl.AddRectFilled(tabTL + new Vector2(ctx.Px(2f), ctx.Px(2f)),
                    tabTL + new Vector2(tabW - ctx.Px(2f), tabH - ctx.Px(2f)),
                    U32(ctx.Theme.Accent), (tabH - ctx.Px(4f)) * 0.5f);
            }
            else if (tabHovered)
            {
                dl.AddRectFilled(tabTL + new Vector2(ctx.Px(2f), ctx.Px(2f)),
                    tabTL + new Vector2(tabW - ctx.Px(2f), tabH - ctx.Px(2f)),
                    U32(new Vector4(1f, 1f, 1f, 0.06f)), (tabH - ctx.Px(4f)) * 0.5f);
            }
            var tabLabel = ctx.Localize(t == 0 ? "os.edit_filters" : "os.edit_adjust");
            var tabSz = ImGui.CalcTextSize(tabLabel);
            dl.AddText(tabTL + new Vector2((tabW - tabSz.X) * 0.5f, (tabH - tabSz.Y) * 0.5f),
                U32(tabSelected ? WhiteText : MutedText), tabLabel);
        }

        var contentTop = tl.Y + tabH + ctx.Px(8f);
        if (edit.Tab == 0)
        {
            const int PerRow = 4;
            var chipW = (width - chipGap * (PerRow - 1)) / PerRow;
            for (var i = 0; i < Filters.Length; i++)
            {
                var (filter, key) = Filters[i];
                var chipTL = new Vector2(
                    tl.X + (i % PerRow) * (chipW + chipGap),
                    contentTop + (i / PerRow) * (chipH + chipGap));
                var selected = edit.Filter == filter;
                ImGui.SetCursorScreenPos(chipTL);
                if (ImGui.InvisibleButton($"##photosFilter{i}", new Vector2(chipW, chipH)))
                {
                    edit.Filter = filter;
                    this.RequestRender(edit);
                }
                var hovered = ImGui.IsItemHovered();
                var fill = selected ? ctx.Theme.Accent : hovered ? new Vector4(1f, 1f, 1f, 0.14f) : new Vector4(1f, 1f, 1f, 0.08f);
                dl.AddRectFilled(chipTL, chipTL + new Vector2(chipW, chipH), U32(fill), chipH * 0.5f);
                if (!selected)
                {
                    dl.AddRect(chipTL, chipTL + new Vector2(chipW, chipH), U32(CardBorder), chipH * 0.5f, ImDrawFlags.RoundCornersAll, 1f);
                }
                var label = ctx.Localize(key);
                var sz = ImGui.CalcTextSize(label);
                dl.PushClipRect(chipTL, chipTL + new Vector2(chipW, chipH), true);
                dl.AddText(chipTL + new Vector2(MathF.Max((chipW - sz.X) * 0.5f, ctx.Px(4f)), (chipH - sz.Y) * 0.5f),
                    U32(selected ? WhiteText : new Vector4(1f, 1f, 1f, 0.82f)), label);
                dl.PopClipRect();
            }
        }
        else
        {
            var rowY = contentTop;
            var released = AdjustRow(ctx, "##phAdjB", ctx.Localize("os.edit_brightness"), ref edit.Brightness, -50, 50, "%+d%%", new Vector2(tl.X, rowY), width, null);
            rowY += sliderRowH;
            released |= AdjustRow(ctx, "##phAdjC", ctx.Localize("os.edit_contrast"), ref edit.Contrast, -50, 50, "%+d%%", new Vector2(tl.X, rowY), width, null);
            rowY += sliderRowH;
            released |= AdjustRow(ctx, "##phAdjH", ctx.Localize("os.edit_hue"), ref edit.TintHue, 0, 360, "%d°", new Vector2(tl.X, rowY), width, HueColor(edit.TintHue));
            rowY += sliderRowH;
            released |= AdjustRow(ctx, "##phAdjT", ctx.Localize("os.edit_tint"), ref edit.TintStrength, 0, 100, "%d%%", new Vector2(tl.X, rowY), width, HueColor(edit.TintHue) with { W = edit.TintStrength / 100f });
            if (released)
            {
                this.RequestRender(edit);
            }
        }
    }

    private static bool AdjustRow(OsAppContext ctx, string id, string label, ref int value, int min, int max, string fmt, Vector2 tl, float width, Vector4? swatch)
    {
        var dl = ImGui.GetWindowDrawList();
        var labelW = ctx.Px(86f);
        var swatchW = swatch == null ? 0f : ctx.Px(24f);
        dl.PushClipRect(tl, tl + new Vector2(labelW - ctx.Px(4f), ctx.Px(22f)), true);
        dl.AddText(tl + new Vector2(0f, ctx.Px(3f)), U32(MutedText), label);
        dl.PopClipRect();
        ImGui.SetCursorScreenPos(new Vector2(tl.X + labelW, tl.Y));
        ImGui.SetNextItemWidth(width - labelW - swatchW);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(9f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, GhostFill);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, GhostFill);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, GhostFill);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ctx.Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ctx.Theme.AccentLight);
        ImGui.SliderInt(id, ref value, min, max, fmt);
        var released = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar();
        if (swatch is { } color)
        {
            var center = new Vector2(tl.X + width - swatchW * 0.5f, tl.Y + ctx.Px(10f));
            dl.AddCircleFilled(center, ctx.Px(8f), U32(color with { W = MathF.Max(color.W, 0.15f) }), 24);
            dl.AddCircle(center, ctx.Px(8f), U32(CardBorder), 24, 1f);
        }
        return released;
    }

    private static string EditKey(EditState edit) =>
        $"{edit.Filter}|{edit.Brightness}|{edit.Contrast}|{edit.TintHue}|{edit.TintStrength}";

    private static bool IsNeutral(EditState edit) =>
        edit.Filter == ImageFilter.None && edit.Brightness == 0 && edit.Contrast == 0
        && edit.TintStrength == 0 && edit.TintHue % 360 == 0;

    private static bool IsApplying(EditState edit) =>
        !IsNeutral(edit) && edit.Rendered.TryGetValue(EditKey(edit), out var value) && value.Length == 0;

    private static ImageAdjustments AdjustmentsOf(EditState edit) => new(
        1f + edit.Brightness / 100f,
        1f + edit.Contrast / 100f,
        edit.TintHue,
        edit.TintStrength / 100f);

    private static Vector4 HueColor(float hueDeg)
    {
        var h = ((hueDeg % 360f) + 360f) % 360f / 60f;
        var t = 1f - MathF.Abs(h % 2f - 1f);
        var (r, g, b) = (int)h switch
        {
            0 => (1f, t, 0f),
            1 => (t, 1f, 0f),
            2 => (0f, 1f, t),
            3 => (0f, t, 1f),
            4 => (t, 0f, 1f),
            _ => (1f, 0f, t),
        };
        return new Vector4(r, g, b, 1f);
    }

    /// <summary>Initiates async rendering of current filter and adjustment combination.</summary>
    private void RequestRender(EditState edit)
    {
        if (IsNeutral(edit))
        {
            edit.Shown = edit.Source;
            return;
        }
        var key = EditKey(edit);
        if (edit.Rendered.TryGetValue(key, out var existing))
        {
            if (existing.Length > 0)
            {
                edit.Shown = existing;
            }
            return;
        }
        if (!edit.Rendered.TryAdd(key, ""))
        {
            return;
        }
        this.caps.Effects.Apply(edit.Source, edit.Filter, AdjustmentsOf(edit), result =>
        {
            if (result == null)
            {
                edit.Rendered.TryRemove(key, out _);
                return;
            }
            edit.Rendered[key] = result;
            if (this.edit == edit && EditKey(edit) == key)
            {
                edit.Shown = result;
            }
        });
    }

    private void DrawViewerTopBar(OsAppContext ctx, PhotoItem photo, Vector2 winPos, Vector2 winSize, ImDrawListPtr dl)
    {
        var dark = U32(new Vector4(0f, 0f, 0f, 0.60f));
        var clear = U32(new Vector4(0f, 0f, 0f, 0f));
        dl.AddRectFilledMultiColor(winPos, winPos + new Vector2(winSize.X, ctx.Px(58f)), dark, dark, clear, clear);

        var r = ctx.Px(16f);
        var cy = winPos.Y + ctx.Px(26f);
        if (RoundIconButton("##viewerBack", FontAwesomeIcon.ChevronLeft, new Vector2(winPos.X + ctx.Px(12f) + r, cy), r, ctx.Px(14f), NoFill, HoverFill, WhiteText))
        {
            this.view = View.Album;
            this.renamingPhoto = false;
        }
        if (RoundIconButton("##photoDelete", FontAwesomeIcon.Trash, new Vector2(winPos.X + winSize.X - ctx.Px(12f) - r, cy), r, ctx.Px(13f), NoFill, HoverFill, new Vector4(1f, 1f, 1f, 0.75f)))
        {
            this.confirmDeletePhoto = true;
        }

        if (this.renamingPhoto)
        {
            var inputW = winSize.X * 0.52f;
            var inputH = ImGui.GetTextLineHeight() + ctx.Px(14f);
            ImGui.SetCursorScreenPos(new Vector2(winPos.X + (winSize.X - inputW) * 0.5f, cy - inputH * 0.5f));
            ImGui.SetNextItemWidth(inputW);
            var commit = this.OverlayInput(ctx, "##photoRename", ctx.Localize("os.photos_rename"), ref this.photoNameEdit);
            if (commit)
            {
                this.library.RenamePhoto(photo.Id, this.photoNameEdit);
                this.renamingPhoto = false;
            }
            else if (ImGui.IsItemDeactivated())
            {
                this.renamingPhoto = false;
            }
        }
        else
        {
            var maxW = winSize.X - (r * 2f + ctx.Px(12f)) * 2f - ctx.Px(16f);
            var ts = ImGui.CalcTextSize(photo.Name);
            var nameW = MathF.Min(ts.X, maxW);
            var nameTL = new Vector2(winPos.X + (winSize.X - nameW) * 0.5f, cy - ts.Y * 0.5f);
            ImGui.SetCursorScreenPos(nameTL - ctx.Px(6f, 4f));
            var clicked = ImGui.InvisibleButton("##photoName", new Vector2(nameW + ctx.Px(12f), ts.Y + ctx.Px(8f)));
            var hovered = ImGui.IsItemHovered();
            dl.PushClipRect(nameTL, nameTL + new Vector2(nameW, ts.Y), true);
            dl.AddText(nameTL, U32(WhiteText), photo.Name);
            dl.PopClipRect();
            if (hovered)
            {
                var underlineY = nameTL.Y + ts.Y + ctx.Px(2f);
                dl.AddLine(new Vector2(nameTL.X, underlineY), new Vector2(nameTL.X + nameW, underlineY), U32(MutedText), 1f);
            }
            if (clicked)
            {
                this.renamingPhoto = true;
                this.photoNameEdit = photo.Name;
                this.focusPending = true;
            }
        }
    }

    private void DrawViewerBottomBar(OsAppContext ctx, IReadOnlyList<PhotoItem> photos, int index, Vector2 winPos, Vector2 winSize, ImDrawListPtr dl)
    {
        var barH = ctx.Px(86f);
        var barTL = new Vector2(winPos.X, winPos.Y + winSize.Y - barH);
        var dark = U32(new Vector4(0f, 0f, 0f, 0.62f));
        var clear = U32(new Vector4(0f, 0f, 0f, 0f));
        dl.AddRectFilledMultiColor(barTL, winPos + winSize, clear, clear, dark, dark);

        var photo = photos[index];
        var date = photo.AddedAtUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
        if (photo.Location is { Length: > 0 } location)
        {
            date = $"{date}  ·  {location}";
        }
        var datePx = ImGui.GetFontSize() * 0.85f;
        var dateW = ImGui.CalcTextSize(date).X * 0.85f;
        dl.AddText(ImGui.GetFont(), datePx, new Vector2(winPos.X + (winSize.X - dateW) * 0.5f, barTL.Y + ctx.Px(14f)), U32(MutedText), date);

        var cy = winPos.Y + winSize.Y - ctx.Px(30f);
        var r = ctx.Px(17f);
        var centerX = winPos.X + winSize.X * 0.5f;
        if (RoundIconButton("##viewerPrev", FontAwesomeIcon.ChevronLeft, new Vector2(winPos.X + ctx.Px(16f) + r, cy), r, ctx.Px(14f), GhostFill, GhostHover, WhiteText, ctx.Localize("os.tt_prev")))
        {
            this.StepPhoto(photos, index, -1);
        }
        if (RoundIconButton("##viewerNext", FontAwesomeIcon.ChevronRight, new Vector2(winPos.X + winSize.X - ctx.Px(16f) - r, cy), r, ctx.Px(14f), GhostFill, GhostHover, WhiteText, ctx.Localize("os.tt_next")))
        {
            this.StepPhoto(photos, index, 1);
        }
        if (RoundIconButton("##viewerEdit", FontAwesomeIcon.Magic, new Vector2(centerX - ctx.Px(58f), cy), r, ctx.Px(13f), GhostFill, GhostHover, WhiteText, ctx.Localize("os.tt_edit")))
        {
            this.edit = new EditState
            {
                Source = photo.Path,
                Shown = photo.Path,
                AlbumId = photo.AlbumId,
                Name = photo.Name,
            };
            this.view = View.Edit;
        }
        if (RoundIconButton("##viewerMove", FontAwesomeIcon.FolderOpen, new Vector2(centerX + ctx.Px(58f), cy), r, ctx.Px(13f), GhostFill, GhostHover, WhiteText, ctx.Localize("os.tt_move")))
        {
            this.movingPhoto = true;
        }
        if (RoundIconButton("##viewerShare", FontAwesomeIcon.Share, new Vector2(centerX, cy), ctx.Px(20f), ctx.Px(15f), ctx.Theme.Accent, ctx.Theme.AccentLight, WhiteText, ctx.Localize("os.tt_share")))
        {
            ctx.Capabilities.Share.Offer(new ShareItem
            {
                Type = ShareTypes.Photo,
                LocalPath = photo.Path,
                Title = photo.Name,
                SourceAppId = "photos",
            });
        }
    }

    private void StepPhoto(IReadOnlyList<PhotoItem> photos, int index, int delta)
    {
        var next = (index + delta + photos.Count) % photos.Count;
        this.photoId = photos[next].Id;
        this.fadeStartUtc = DateTime.UtcNow;
        this.renamingPhoto = false;
    }

    private void DrawPickerBanner(OsAppContext ctx)
    {
        var winPos = ImGui.GetWindowPos();
        var winW = ImGui.GetWindowSize().X;
        var pad = ctx.Px(12f);
        var startY = ImGui.GetCursorScreenPos().Y;
        var h = ctx.Px(34f);
        var tl = new Vector2(winPos.X + pad, startY + ctx.Px(8f));
        var br = new Vector2(winPos.X + winW - pad, tl.Y + h);
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(tl, br, U32(ctx.Theme.Accent with { W = 0.22f }), ctx.Px(11f));
        dl.AddRect(tl, br, U32(ctx.Theme.Accent), ctx.Px(11f), ImDrawFlags.RoundCornersAll, ctx.Px(1.2f));
        var text = ctx.Localize("os.photos_pick_banner");
        var ts = ImGui.CalcTextSize(text);
        var side = h - ctx.Px(8f);
        dl.PushClipRect(tl, new Vector2(br.X - side - ctx.Px(8f), br.Y), true);
        dl.AddText(new Vector2(tl.X + ctx.Px(12f), tl.Y + (h - ts.Y) * 0.5f), U32(WhiteText), text);
        dl.PopClipRect();

        if (RoundIconButton("##pickerCancel", FontAwesomeIcon.Times, new Vector2(br.X - ctx.Px(4f) - side * 0.5f, tl.Y + h * 0.5f), side * 0.5f, ctx.Px(11f), NoFill, HoverFill, WhiteText))
        {
            this.pickerArmed = false;
        }

        ImGui.SetCursorScreenPos(new Vector2(winPos.X, startY));
        ImGui.Dummy(new Vector2(winW, h + ctx.Px(12f)));
    }

    private ConfirmResult DrawConfirmOverlay(OsAppContext ctx, string id)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(DimColor));

        var text = ctx.Localize("os.photos_delete_confirm");
        var padIn = ctx.Px(16f);
        var panelW = MathF.Min(winSize.X - ctx.Px(48f), ctx.Px(260f));
        var innerW = panelW - padIn * 2f;
        var textSize = ImGui.CalcTextSize(text, false, innerW);
        var btnH = ctx.Px(34f);
        var panelH = padIn + textSize.Y + ctx.Px(14f) + btnH + padIn;
        var panelTL = winPos + (winSize - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL + ctx.Px(0f, 4f), panelBR + ctx.Px(0f, 4f), U32(ShadowColor), ctx.Px(16f));
        dl.AddRectFilled(panelTL, panelBR, U32(PanelBg), ctx.Px(16f));
        dl.AddRect(panelTL, panelBR, U32(PanelBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);

        ImGui.SetCursorScreenPos(panelTL + new Vector2(padIn, padIn));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerW);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();

        var result = ConfirmResult.Pending;
        var btnW = (innerW - ctx.Px(8f)) * 0.5f;
        var btnY = panelBR.Y - padIn - btnH;
        if (PanelButton(ctx, $"##{id}Cancel", ctx.Localize("common.cancel"), new Vector2(panelTL.X + padIn, btnY), new Vector2(btnW, btnH), ctx.Theme.ChipFill))
        {
            result = ConfirmResult.Cancelled;
        }
        if (PanelButton(ctx, $"##{id}Ok", ctx.Localize("common.ok"), new Vector2(panelTL.X + padIn + btnW + ctx.Px(8f), btnY), new Vector2(btnW, btnH), DangerFill))
        {
            result = ConfirmResult.Confirmed;
        }

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel widgets stay clickable.
        ImGui.SetCursorScreenPos(winPos);
        if (ImGui.InvisibleButton($"##{id}Scrim", winSize) && !InRect(ImGui.GetMousePos(), panelTL, panelBR))
        {
            result = ConfirmResult.Cancelled;
        }
        return result;
    }

    private bool OverlayInput(OsAppContext ctx, string id, string hint, ref string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(9f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ctx.Px(10f, 7f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.11f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.13f));
        if (this.focusPending)
        {
            ImGui.SetKeyboardFocusHere();
            this.focusPending = false;
        }
        var commit = ImGui.InputTextWithHint(id, hint, ref text, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        return commit;
    }

    private void OpenAlbum(string id)
    {
        this.albumId = id;
        this.view = View.Album;
        this.renamingAlbum = false;
        this.confirmDeleteAlbum = false;
        this.resetAlbumScroll = true;
    }

    private float FadeAlpha(OsAppContext ctx)
    {
        if (ctx.ReduceMotion)
        {
            return 1f;
        }
        var t = (DateTime.UtcNow - this.fadeStartUtc).TotalSeconds / FadeSeconds;
        return (float)Math.Clamp(t, 0.0, 1.0);
    }

    private float AspectOf(string path)
    {
        if (this.aspectCache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        var aspect = 1f;
        try
        {
            // IPhotoLibrary hands out only a bare texture id, so aspect ratios come from the image headers on disk.
            var size = ReadImageSize(path);
            if (size is { X: > 0f, Y: > 0f } s)
            {
                aspect = s.X / s.Y;
            }
        }
        catch (Exception)
        {
        }
        this.aspectCache[path] = aspect;
        return aspect;
    }

    private static Vector2? ReadImageSize(string path)
    {
        using var fs = File.OpenRead(path);
        var b0 = fs.ReadByte();
        var b1 = fs.ReadByte();
        if (b0 == 0x89 && b1 == 0x50)
        {
            return ReadPngSize(fs);
        }
        if (b0 == 0xFF && b1 == 0xD8)
        {
            return ReadJpegSize(fs);
        }
        return null;
    }

    private static Vector2? ReadPngSize(Stream fs)
    {
        Span<byte> header = stackalloc byte[22];
        if (fs.ReadAtLeast(header, header.Length, false) < header.Length)
        {
            return null;
        }
        if (header[10] != (byte)'I' || header[11] != (byte)'H' || header[12] != (byte)'D' || header[13] != (byte)'R')
        {
            return null;
        }
        var w = (header[14] << 24) | (header[15] << 16) | (header[16] << 8) | header[17];
        var h = (header[18] << 24) | (header[19] << 16) | (header[20] << 8) | header[21];
        return new Vector2(w, h);
    }

    private static Vector2? ReadJpegSize(Stream fs)
    {
        while (true)
        {
            var b = fs.ReadByte();
            if (b != 0xFF)
            {
                return null;
            }
            var marker = fs.ReadByte();
            while (marker == 0xFF)
            {
                marker = fs.ReadByte();
            }
            if (marker < 0)
            {
                return null;
            }
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                continue;
            }
            var lenHi = fs.ReadByte();
            var lenLo = fs.ReadByte();
            if (lenHi < 0 || lenLo < 0)
            {
                return null;
            }
            var len = (lenHi << 8) | lenLo;
            if (len < 2 || marker == 0xDA)
            {
                return null;
            }
            var isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof)
            {
                if (len < 7)
                {
                    return null;
                }
                fs.ReadByte();
                var h = (fs.ReadByte() << 8) | fs.ReadByte();
                var w = (fs.ReadByte() << 8) | fs.ReadByte();
                if (w <= 0 || h <= 0)
                {
                    return null;
                }
                return new Vector2(w, h);
            }
            fs.Seek(len - 2, SeekOrigin.Current);
        }
    }

    private static (Vector2 Uv0, Vector2 Uv1) CoverUv(float texAspect, float destAspect)
    {
        if (texAspect > destAspect)
        {
            var crop = destAspect / texAspect;
            var x0 = (1f - crop) * 0.5f;
            return (new Vector2(x0, 0f), new Vector2(x0 + crop, 1f));
        }
        var cropY = texAspect / destAspect;
        var y0 = (1f - cropY) * 0.5f;
        return (new Vector2(0f, y0), new Vector2(1f, y0 + cropY));
    }

    private static void RoundedGradientV(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, Vector4 top, Vector4 bottom, ImDrawFlags corners)
    {
        var vtxStart = dl.VtxBuffer.Size;
        dl.AddRectFilled(tl, br, 0xFFFFFFFFu, rounding, corners);
        var h = MathF.Max(1f, br.Y - tl.Y);
        for (var v = vtxStart; v < dl.VtxBuffer.Size; v++)
        {
            var vert = dl.VtxBuffer[v];
            var t = Math.Clamp((vert.Pos.Y - tl.Y) / h, 0f, 1f);
            var col = Vector4.Lerp(top, bottom, t);
            col.W *= ((vert.Col >> 24) & 0xFF) / 255f;
            vert.Col = ImGui.ColorConvertFloat4ToU32(col);
            dl.VtxBuffer[v] = vert;
        }
    }

    private static bool RoundIconButton(string id, FontAwesomeIcon icon, Vector2 center, float radius, float iconPx, Vector4 fill, Vector4 hoverFill, Vector4 iconColor, string? tooltip = null)
    {
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton(id, new Vector2(radius * 2f, radius * 2f));
        var hovered = ImGui.IsItemHovered();
        if (hovered && tooltip != null)
        {
            ImGui.SetTooltip(tooltip);
        }
        var dl = ImGui.GetWindowDrawList();
        var col = hovered ? hoverFill : fill;
        if (col.W > 0f)
        {
            dl.AddCircleFilled(center, radius, U32(col), 32);
        }
        AddIconCentered(dl, icon, iconPx, center, U32(iconColor));
        return clicked;
    }

    private static bool PillButton(OsAppContext ctx, string id, FontAwesomeIcon icon, string label, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var dl = ImGui.GetWindowDrawList();
        var br = tl + size;
        var rounding = size.Y * 0.5f;
        // Theme-neutral: pale theme accents (YoRHa, Aetherless) make white-on-accent unreadable.
        var fill = active ? new Vector4(1f, 1f, 1f, 0.07f)
            : hovered ? new Vector4(1f, 1f, 1f, 0.17f)
            : new Vector4(1f, 1f, 1f, 0.10f);

        dl.AddRectFilled(tl + ctx.Px(0f, 2f), br + ctx.Px(0f, 2f), U32(ShadowColor), rounding);
        dl.AddRectFilled(tl, br, U32(fill), rounding);
        dl.AddRect(tl, br, U32(CardBorder), rounding, ImDrawFlags.RoundCornersAll, 1f);

        var glyph = icon.ToIconString();
        var iconPx = size.Y * 0.42f;
        ImGui.PushFont(UiBuilder.IconFont);
        var iconW = ImGui.CalcTextSize(glyph).X * (iconPx / ImGui.GetFontSize());
        ImGui.PopFont();
        var textSize = ImGui.CalcTextSize(label);
        var gap = ctx.Px(7f);
        var total = iconW + gap + MathF.Min(textSize.X, size.X - iconW - gap - ctx.Px(16f));
        var startX = tl.X + MathF.Max((size.X - total) * 0.5f, ctx.Px(8f));
        var cy = tl.Y + size.Y * 0.5f;
        dl.PushClipRect(tl, br, true);
        AddIconCentered(dl, icon, iconPx, new Vector2(startX + iconW * 0.5f, cy), U32(WhiteText));
        dl.AddText(new Vector2(startX + iconW + gap, cy - textSize.Y * 0.5f), U32(WhiteText), label);
        dl.PopClipRect();
        return clicked;
    }

    private static bool PanelButton(OsAppContext ctx, string id, string label, Vector2 tl, Vector2 size, Vector4 fill)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var col = hovered ? Lighten(fill, 0.12f) : fill;
        dl.AddRectFilled(tl, tl + size, U32(col), ctx.Px(10f));
        var textSize = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + (size - textSize) * 0.5f, U32(WhiteText), label);
        dl.PopClipRect();
        return clicked;
    }

    private static void AddIconCentered(ImDrawListPtr dl, FontAwesomeIcon icon, float px, Vector2 center, uint col)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(glyph) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, center - size * 0.5f, col, glyph);
        ImGui.PopFont();
    }

    private static Vector4 Lighten(Vector4 c, float t)
    {
        return new Vector4(c.X + (1f - c.X) * t, c.Y + (1f - c.Y) * t, c.Z + (1f - c.Z) * t, c.W);
    }

    private static bool InRect(Vector2 p, Vector2 tl, Vector2 br)
    {
        return p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y && p.Y <= br.Y;
    }

    private static uint U32(Vector4 c)
    {
        return ImGui.ColorConvertFloat4ToU32(c);
    }
}
