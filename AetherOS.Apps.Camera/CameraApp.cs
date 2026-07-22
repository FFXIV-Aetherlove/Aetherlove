using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Camera;

/// <summary>The phone camera: in-game shots land in the Photos app's camera album (crop baked in, time and
/// in-game location stamped), and other apps can request a framed selfie via the
/// <see cref="OsIntents.CameraCapture"/> round trip.</summary>
public sealed class CameraApp : IAetherApp
{
    private sealed record CaptureRequest(string ReturnApp, float Aspect, int MinWidth);

    private sealed class ReviewState
    {
        public string Path = "";
        public Vector4 Crop;
        public ImageFilter Filter = ImageFilter.None;
        public int Brightness;
        public int Contrast;
        public int TintHue;
        public int TintStrength;
        public bool Editing;
        public int EditTab;
        public string Shown = "";

        // "" marks a combination still rendering; the worker callback swaps in the finished path.
        public readonly ConcurrentDictionary<string, string> Rendered = new();
    }

    private static readonly Vector4 TileTopColor = new(0.46f, 0.48f, 0.55f, 1f);
    private static readonly Vector4 TileBottomColor = new(0.13f, 0.14f, 0.18f, 1f);
    private static readonly Vector4 WhiteText = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 MutedText = new(1f, 1f, 1f, 0.55f);
    private static readonly Vector4 PanelFill = new(1f, 1f, 1f, 0.07f);
    private static readonly Vector4 CardBorder = new(1f, 1f, 1f, 0.10f);
    private static readonly Vector4 DangerFill = new(0.82f, 0.22f, 0.28f, 1f);
    private static readonly Vector4 ViewerBg = new(0.03f, 0.03f, 0.04f, 1f);

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

    private readonly Func<string> name;
    private readonly IAppCapabilities caps;
    private readonly ICameraLibrary library;
    private readonly Dictionary<string, Vector2> sizeCache = new();

    private CaptureRequest? pending;
    private bool pendingLaunched;
    private IOsShell? shell;
    private int viewerIndex = -1;
    private bool confirmDelete;
    private ReviewState? review;
    private CameraRequest lastRequest = new(1f, 128, FreeForm: true);

    public CameraApp(Func<string> name, IAppCapabilities caps, ICameraLibrary library)
    {
        this.name = name;
        this.caps = caps;
        this.library = library;
    }

    public string Id => "camera";

    public string Name => this.name();

    public FontAwesomeIcon Icon => FontAwesomeIcon.Camera;

    public Vector4 TileTop => TileTopColor;

    public Vector4 TileBottom => TileBottomColor;

    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type != OsIntents.CameraCapture
            || !OsIntents.TryGetCameraCapture(intent, out var returnApp, out var aspect, out var minWidth))
        {
            return;
        }
        this.pending = new CaptureRequest(returnApp, aspect, minWidth);
        this.pendingLaunched = false;
        this.viewerIndex = -1;
        this.confirmDelete = false;
        this.review = null;
    }

    public void Draw(OsAppContext ctx)
    {
        this.shell = ctx.Shell;

        if (this.pending is { } request && !this.pendingLaunched)
        {
            this.pendingLaunched = true;
            this.StartCapture(new CameraRequest(request.Aspect, request.MinWidth));
        }

        if (this.review != null)
        {
            this.DrawReview(ctx, this.review);
            return;
        }
        if (this.viewerIndex >= 0)
        {
            this.DrawViewer(ctx);
            return;
        }
        this.DrawMain(ctx);
    }

    private void DrawReview(OsAppContext ctx, ReviewState review)
    {
        var winPos = ImGui.GetWindowPos() + ImGui.GetCursorPos();
        var winSize = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(ViewerBg));

        var pad = ctx.Px(14f);
        var width = winSize.X - pad * 2f;
        var btnH = ctx.Px(30f);
        var chipH = ctx.Px(24f);
        var chipGap = ctx.Px(6f);
        var tabH = ctx.Px(22f);
        var sliderRowH = ctx.Px(26f);
        var editH = review.Editing
            ? tabH + ctx.Px(8f) + (review.EditTab == 0 ? chipH * 2f + chipGap : sliderRowH * 4f) + ctx.Px(10f)
            : 0f;
        var controlsTop = winPos.Y + winSize.Y - ctx.Px(16f) - btnH - editH;

        var shownPath = review.Shown.Length > 0 ? review.Shown : review.Path;
        var tex = this.caps.Textures.Get(shownPath);
        var imgTop = winPos.Y + ctx.Px(52f);
        var area = new Vector2(winSize.X, controlsTop - ctx.Px(10f) - imgTop);
        if (tex is { } handle && area.Y > 0f)
        {
            var cropAspect = this.FramedAspect(review.Crop.Z, review.Crop.W, shownPath);
            var destAspect = area.X / area.Y;
            var size = cropAspect > destAspect
                ? new Vector2(area.X, area.X / cropAspect)
                : new Vector2(area.Y * cropAspect, area.Y);
            var tl = new Vector2(winPos.X, imgTop) + (area - size) * 0.5f;
            var (uv0, uv1) = this.FramedUv(review.Crop.X, review.Crop.Y, review.Crop.Z, review.Crop.W, shownPath, cropAspect);
            dl.AddImage(handle, tl, tl + size, uv0, uv1);
        }

        var caption = ctx.Localize("os.camera_review_title");
        var capSize = ImGui.CalcTextSize(caption);
        dl.AddText(new Vector2(winPos.X + (winSize.X - capSize.X) * 0.5f, winPos.Y + ctx.Px(24f)), U32(WhiteText), caption);

        if (IsApplying(review))
        {
            var applying = ctx.Localize("os.filter_applying");
            var appSize = ImGui.CalcTextSize(applying);
            dl.AddText(new Vector2(winPos.X + (winSize.X - appSize.X) * 0.5f, controlsTop - ctx.Px(10f) - appSize.Y - ctx.Px(6f)),
                U32(MutedText), applying);
        }

        if (RoundIconButton("##cameraReviewBack", FontAwesomeIcon.ArrowLeft,
                winPos + new Vector2(ctx.Px(24f), ctx.Px(24f)), ctx.Px(17f), ctx.Px(15f)))
        {
            this.review = null;
            return;
        }

        var cardTL = new Vector2(winPos.X + ctx.Px(8f), controlsTop - ctx.Px(12f));
        var cardBR = new Vector2(winPos.X + winSize.X - ctx.Px(8f), winPos.Y + winSize.Y - ctx.Px(8f));
        dl.AddRectFilled(cardTL, cardBR, U32(new Vector4(0.07f, 0.07f, 0.10f, 0.94f)), ctx.Px(16f));
        dl.AddRect(cardTL, cardBR, U32(CardBorder), ctx.Px(16f), ImDrawFlags.RoundCornersAll, 1f);

        if (review.Editing)
        {
            this.DrawEditPanel(ctx, review, new Vector2(winPos.X + pad, controlsTop), width, tabH, chipH, chipGap, sliderRowH);
        }

        var btnGap = ctx.Px(8f);
        var btnW = (width - btnGap * 2f) / 3f;
        var btnY = winPos.Y + winSize.Y - ctx.Px(16f) - btnH;
        if (GhostButton(ctx, ctx.Localize("os.camera_retake"), "##cameraReviewRetake",
                new Vector2(winPos.X + pad, btnY), new Vector2(btnW, btnH)))
        {
            this.review = null;
            this.StartCapture(this.lastRequest);
            return;
        }
        if (GhostButton(ctx, ctx.Localize("os.camera_edit"), "##cameraReviewEdit",
                new Vector2(winPos.X + pad + btnW + btnGap, btnY), new Vector2(btnW, btnH), accentOutline: review.Editing))
        {
            review.Editing = !review.Editing;
        }
        var saveLabel = this.pending != null ? ctx.Localize("os.camera_use") : ctx.Localize("os.camera_save");
        if (AccentButton(ctx, $"{saveLabel}###cameraReviewSave",
                new Vector2(winPos.X + pad + (btnW + btnGap) * 2f, btnY), new Vector2(btnW, btnH)))
        {
            this.SaveReview(review);
        }
    }

    private void DrawEditPanel(OsAppContext ctx, ReviewState review, Vector2 tl, float width, float tabH, float chipH, float chipGap, float sliderRowH)
    {
        var dl = ImGui.GetWindowDrawList();
        // Segmented control: one shared track, the selected half filled with the accent.
        dl.AddRectFilled(tl, tl + new Vector2(width, tabH), U32(PanelFill), tabH * 0.5f);
        var tabW = width * 0.5f;
        for (var t = 0; t < 2; t++)
        {
            var tabTL = new Vector2(tl.X + t * tabW, tl.Y);
            var tabSelected = review.EditTab == t;
            ImGui.SetCursorScreenPos(tabTL);
            if (ImGui.InvisibleButton($"##cameraEditTab{t}", new Vector2(tabW, tabH)))
            {
                review.EditTab = t;
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
        if (review.EditTab == 0)
        {
            const int PerRow = 4;
            var chipW = (width - chipGap * (PerRow - 1)) / PerRow;
            for (var i = 0; i < Filters.Length; i++)
            {
                var (filter, key) = Filters[i];
                var chipTL = new Vector2(
                    tl.X + (i % PerRow) * (chipW + chipGap),
                    contentTop + (i / PerRow) * (chipH + chipGap));
                var selected = review.Filter == filter;
                ImGui.SetCursorScreenPos(chipTL);
                if (ImGui.InvisibleButton($"##cameraFilter{i}", new Vector2(chipW, chipH)))
                {
                    review.Filter = filter;
                    this.RequestRender(review);
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
            var released = AdjustRow(ctx, "##camAdjB", ctx.Localize("os.edit_brightness"), ref review.Brightness, -50, 50, "%+d%%", new Vector2(tl.X, rowY), width, null);
            rowY += sliderRowH;
            released |= AdjustRow(ctx, "##camAdjC", ctx.Localize("os.edit_contrast"), ref review.Contrast, -50, 50, "%+d%%", new Vector2(tl.X, rowY), width, null);
            rowY += sliderRowH;
            released |= AdjustRow(ctx, "##camAdjH", ctx.Localize("os.edit_hue"), ref review.TintHue, 0, 360, "%d°", new Vector2(tl.X, rowY), width, HueColor(review.TintHue));
            rowY += sliderRowH;
            released |= AdjustRow(ctx, "##camAdjT", ctx.Localize("os.edit_tint"), ref review.TintStrength, 0, 100, "%d%%", new Vector2(tl.X, rowY), width, HueColor(review.TintHue) with { W = review.TintStrength / 100f });
            if (released)
            {
                this.RequestRender(review);
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
        ImGui.PushStyleColor(ImGuiCol.FrameBg, PanelFill);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, PanelFill);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, PanelFill);
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

    private static string EditKey(ReviewState review) =>
        $"{review.Filter}|{review.Brightness}|{review.Contrast}|{review.TintHue}|{review.TintStrength}";

    private static bool IsNeutral(ReviewState review) =>
        review.Filter == ImageFilter.None && review.Brightness == 0 && review.Contrast == 0
        && review.TintStrength == 0 && review.TintHue % 360 == 0;

    private static bool IsApplying(ReviewState review) =>
        !IsNeutral(review) && review.Rendered.TryGetValue(EditKey(review), out var value) && value.Length == 0;

    private static ImageAdjustments AdjustmentsOf(ReviewState review) => new(
        1f + review.Brightness / 100f,
        1f + review.Contrast / 100f,
        review.TintHue,
        review.TintStrength / 100f);

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

    /// <summary>Kicks the async render for the current filter + slider combination; the preview snaps in when
    /// the worker finishes. Repeat combinations reuse the cached result.</summary>
    private void RequestRender(ReviewState review)
    {
        if (IsNeutral(review))
        {
            review.Shown = review.Path;
            return;
        }
        var key = EditKey(review);
        if (review.Rendered.TryGetValue(key, out var existing))
        {
            if (existing.Length > 0)
            {
                review.Shown = existing;
            }
            return;
        }
        if (!review.Rendered.TryAdd(key, ""))
        {
            return;
        }
        this.caps.Effects.Apply(review.Path, review.Filter, AdjustmentsOf(review), result =>
        {
            if (result == null)
            {
                review.Rendered.TryRemove(key, out _);
                return;
            }
            review.Rendered[key] = result;
            if (this.review == review && EditKey(review) == key)
            {
                review.Shown = result;
            }
        });
    }

    private void SaveReview(ReviewState review)
    {
        if (IsApplying(review))
        {
            return;
        }
        var source = review.Shown.Length > 0 ? review.Shown : review.Path;
        var saved = this.library.AddCapture(source, review.Crop);
        this.review = null;
        if (this.pending is not { } request)
        {
            return;
        }
        this.pending = null;
        if (saved != null)
        {
            // The crop is baked into the stored copy, so a full-image rect signals "no further crop".
            this.shell?.SendIntent(request.ReturnApp, ReadImageSize(saved.Path) is { X: > 0f, Y: > 0f } size
                ? OsIntents.CreateCameraShot(saved.Path, new Vector4(0f, 0f, size.X, size.Y))
                : OsIntents.CreateCameraShot(source, review.Crop));
        }
    }

    private void DrawMain(OsAppContext ctx)
    {
        ImGui.BeginChild("##cameraScroll", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.None);
        var pad = ctx.Px(14f);
        ImGui.Indent(pad);
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        var width = ImGui.GetContentRegionAvail().X - pad;

        if (this.pending is { } request)
        {
            this.DrawPendingBanner(ctx, request, width);
        }

        this.DrawShutter(ctx, width);
        this.DrawRoll(ctx, width);

        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        ImGui.Unindent(pad);
        ImGui.EndChild();
    }

    private void DrawPendingBanner(OsAppContext ctx, CaptureRequest request, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var height = ctx.Px(74f);
        dl.AddRectFilled(pos, pos + new Vector2(width, height), U32(ctx.Theme.Accent with { W = 0.25f }), ctx.Px(12f));
        dl.AddRect(pos, pos + new Vector2(width, height), U32(ctx.Theme.Accent), ctx.Px(12f), ImDrawFlags.RoundCornersAll, ctx.Px(1.2f));

        var requester = this.shell?.Apps.FirstOrDefault(a => a.Id == request.ReturnApp)?.Name ?? request.ReturnApp;
        var inset = ctx.Px(12f);
        dl.AddText(pos + new Vector2(inset, ctx.Px(10f)), U32(WhiteText),
            string.Format(CultureInfo.InvariantCulture, ctx.Localize("os.camera_for_app"), requester));

        var btnW = (width - inset * 2f - ctx.Px(8f)) * 0.5f;
        var btnY = pos.Y + height - ctx.Px(34f);
        if (ThemedButton(ctx, $"{ctx.Localize("os.camera_retake")}###cameraRetake",
                new Vector2(pos.X + inset, btnY), new Vector2(btnW, ctx.Px(26f))))
        {
            this.StartCapture(new CameraRequest(request.Aspect, request.MinWidth));
        }
        if (ThemedButton(ctx, $"{ctx.Localize("os.camera_cancel")}###cameraCancelReq",
                new Vector2(pos.X + inset + btnW + ctx.Px(8f), btnY), new Vector2(btnW, ctx.Px(26f))))
        {
            var returnApp = request.ReturnApp;
            this.pending = null;
            this.shell?.OpenApp(returnApp);
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
    }

    private void DrawShutter(OsAppContext ctx, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var radius = ctx.Px(32f);
        var start = ImGui.GetCursorScreenPos();
        var center = new Vector2(start.X + width * 0.5f, start.Y + radius);

        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton("##cameraShutter", new Vector2(radius * 2f, radius * 2f));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        dl.AddCircle(center, radius, U32(WhiteText with { W = 0.85f }), 48, ctx.Px(3f));
        var inner = radius - ctx.Px(6f);
        if (held)
        {
            inner *= 0.9f;
        }
        dl.AddCircleFilled(center, inner, U32(hovered ? WhiteText : WhiteText with { W = 0.92f }), 48);
        AddIconCentered(dl, FontAwesomeIcon.Camera, inner * 0.7f, center, U32(new Vector4(0.12f, 0.12f, 0.15f, 1f)));

        if (clicked)
        {
            this.StartCapture(new CameraRequest(1f, 128, FreeForm: true));
        }

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, radius * 2f));

        var hint = ctx.Localize("os.camera_hint");
        var wrapW = width - ctx.Px(24f);
        var hintSize = ImGui.CalcTextSize(hint, false, wrapW);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (width - hintSize.X) * 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextUnformatted(hint);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
    }

    private void DrawRoll(OsAppContext ctx, float width)
    {
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextUnformatted(ctx.Localize("os.camera_roll"));
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));

        var photos = this.library.Photos;
        if (photos.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, MutedText);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(ctx.Localize("os.camera_empty"));
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            return;
        }

        const int Cols = 3;
        var gap = ctx.Px(6f);
        var side = (width - gap * (Cols - 1)) / Cols;
        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var headerH = ImGui.GetTextLineHeight() + ctx.Px(10f);
        var y = start.Y;
        var i = 0;
        while (i < photos.Count)
        {
            var date = photos[i].TakenAtUtc.ToLocalTime().Date;
            dl.AddText(new Vector2(start.X, y + ctx.Px(2f)), U32(WhiteText), DateHeader(ctx, date));
            y += headerH;
            var first = i;
            while (i < photos.Count && photos[i].TakenAtUtc.ToLocalTime().Date == date)
            {
                i++;
            }
            for (var j = first; j < i; j++)
            {
                var tl = new Vector2(
                    start.X + ((j - first) % Cols) * (side + gap),
                    y + ((j - first) / Cols) * (side + gap));
                this.DrawThumb(ctx, dl, photos[j], tl, side, j);
            }
            var rows = (i - first + Cols - 1) / Cols;
            y += rows * side + (rows - 1) * gap + ctx.Px(12f);
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, y - start.Y + ctx.Px(4f)));
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

    private void DrawThumb(OsAppContext ctx, ImDrawListPtr dl, CameraPhoto photo, Vector2 tl, float side, int index)
    {
        var br = tl + new Vector2(side, side);
        var rounding = ctx.Px(10f);
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##cameraShot{index}", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();

        var tex = this.caps.Textures.Get(photo.Path);
        if (tex is { } handle)
        {
            var (uv0, uv1) = CoverUv(this.FramedAspect(0f, 0f, photo.Path), 1f);
            dl.AddImageRounded(handle, tl, br, uv0, uv1, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(tl, br, U32(PanelFill), rounding);
            AddIconCentered(dl, FontAwesomeIcon.Camera, side * 0.3f, (tl + br) * 0.5f, U32(MutedText));
        }
        dl.AddRect(tl, br, U32(CardBorder), rounding, ImDrawFlags.RoundCornersAll, 1f);
        if (hovered)
        {
            dl.AddRect(tl, br, U32(ctx.Theme.Accent), rounding, ImDrawFlags.RoundCornersAll, ctx.Px(1.3f));
        }

        if (clicked)
        {
            this.viewerIndex = index;
            this.confirmDelete = false;
        }
    }

    private void DrawViewer(OsAppContext ctx)
    {
        var photos = this.library.Photos;
        if (this.viewerIndex >= photos.Count)
        {
            this.viewerIndex = -1;
            return;
        }
        var photo = photos[this.viewerIndex];
        var path = photo.Path;

        var winPos = ImGui.GetWindowPos() + ImGui.GetCursorPos();
        var winSize = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(ViewerBg));

        var tex = this.caps.Textures.Get(path);
        if (tex is { } handle)
        {
            var imgAspect = this.FramedAspect(0f, 0f, path);
            var destAspect = winSize.X / winSize.Y;
            var size = imgAspect > destAspect
                ? new Vector2(winSize.X, winSize.X / imgAspect)
                : new Vector2(winSize.Y * imgAspect, winSize.Y);
            var tl = winPos + (winSize - size) * 0.5f;
            dl.AddImage(handle, tl, tl + size);
        }

        var caption = photo.TakenAtUtc.ToLocalTime().ToString("g", ctx.Culture);
        if (photo.Location is { Length: > 0 } location)
        {
            caption = $"{caption}  ·  {location}";
        }
        var capSize = ImGui.CalcTextSize(caption);
        dl.AddText(new Vector2(winPos.X + (winSize.X - capSize.X) * 0.5f, winPos.Y + winSize.Y - ctx.Px(28f)),
            U32(MutedText), caption);

        var top = winPos + new Vector2(ctx.Px(24f), ctx.Px(24f));
        if (RoundIconButton("##cameraViewerBack", FontAwesomeIcon.ArrowLeft, top, ctx.Px(17f), ctx.Px(15f)))
        {
            this.viewerIndex = -1;
            return;
        }
        var deleteCenter = new Vector2(winPos.X + winSize.X - ctx.Px(24f), top.Y);
        if (RoundIconButton("##cameraViewerDelete", FontAwesomeIcon.Trash, deleteCenter, ctx.Px(17f), ctx.Px(14f)))
        {
            this.confirmDelete = true;
        }

        if (this.confirmDelete)
        {
            this.DrawDeleteConfirm(ctx, winPos, winSize, photo);
        }
    }

    private void DrawDeleteConfirm(OsAppContext ctx, Vector2 winPos, Vector2 winSize, CameraPhoto photo)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + winSize, U32(new Vector4(0f, 0f, 0f, 0.55f)));

        var panelW = winSize.X - ctx.Px(64f);
        var panelH = ctx.Px(104f);
        var tl = winPos + new Vector2((winSize.X - panelW) * 0.5f, winSize.Y * 0.36f);
        dl.AddRectFilled(tl, tl + new Vector2(panelW, panelH), U32(new Vector4(0.11f, 0.10f, 0.13f, 1f)), ctx.Px(14f));
        dl.AddRect(tl, tl + new Vector2(panelW, panelH), U32(CardBorder), ctx.Px(14f), ImDrawFlags.RoundCornersAll, 1f);

        var inset = ctx.Px(14f);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(inset, ctx.Px(12f)), U32(WhiteText),
            ctx.Localize("os.camera_delete_confirm"), panelW - inset * 2f);

        var btnW = (panelW - inset * 2f - ctx.Px(8f)) * 0.5f;
        var btnY = tl.Y + panelH - ctx.Px(38f);
        if (ThemedButton(ctx, $"{ctx.Localize("os.camera_cancel")}###cameraDelCancel",
                new Vector2(tl.X + inset, btnY), new Vector2(btnW, ctx.Px(28f))))
        {
            this.confirmDelete = false;
        }

        ImGui.SetCursorScreenPos(new Vector2(tl.X + inset + btnW + ctx.Px(8f), btnY));
        ImGui.PushStyleColor(ImGuiCol.Button, DangerFill);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, DangerFill with { X = 0.92f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, DangerFill with { W = 0.85f });
        var confirmed = ImGui.Button($"{ctx.Localize("os.camera_delete")}###cameraDelConfirm", new Vector2(btnW, ctx.Px(28f)));
        ImGui.PopStyleColor(3);
        if (confirmed)
        {
            this.library.Delete(photo.Id);
            this.sizeCache.Remove(photo.Path);
            this.confirmDelete = false;
            this.viewerIndex = -1;
        }
    }

    private void StartCapture(CameraRequest request)
    {
        this.lastRequest = request;
        this.caps.Camera.Capture(request, shot =>
        {
            this.review = new ReviewState { Path = shot.Path, Shown = shot.Path, Crop = shot.Crop };
        });
    }

    /// <summary>UVs covering <paramref name="destAspect"/> inside the framed crop; the whole image when no
    /// crop was recorded or the header is unreadable.</summary>
    private (Vector2 Uv0, Vector2 Uv1) FramedUv(float cropX, float cropY, float cropW, float cropH, string path, float destAspect)
    {
        var size = this.SizeOf(path);
        if (size is not { X: > 0f, Y: > 0f } s || cropW <= 0f || cropH <= 0f)
        {
            return CoverUv(size is { X: > 0f, Y: > 0f } full ? full.X / full.Y : 1f, destAspect);
        }

        var u0 = Math.Clamp(cropX / s.X, 0f, 1f);
        var v0 = Math.Clamp(cropY / s.Y, 0f, 1f);
        var u1 = Math.Clamp((cropX + cropW) / s.X, 0f, 1f);
        var v1 = Math.Clamp((cropY + cropH) / s.Y, 0f, 1f);
        var cropAspect = (u1 - u0) * s.X / MathF.Max(1f, (v1 - v0) * s.Y);

        // Cover-fit the destination inside the framed sub-rect.
        if (cropAspect > destAspect)
        {
            var keep = destAspect / cropAspect;
            var trim = (u1 - u0) * (1f - keep) * 0.5f;
            return (new Vector2(u0 + trim, v0), new Vector2(u1 - trim, v1));
        }
        var keepY = cropAspect / destAspect;
        var trimY = (v1 - v0) * (1f - keepY) * 0.5f;
        return (new Vector2(u0, v0 + trimY), new Vector2(u1, v1 - trimY));
    }

    private float FramedAspect(float cropW, float cropH, string path)
    {
        if (cropW > 0f && cropH > 0f)
        {
            return cropW / cropH;
        }
        var size = this.SizeOf(path);
        return size is { X: > 0f, Y: > 0f } s ? s.X / s.Y : 1f;
    }

    private Vector2? SizeOf(string path)
    {
        if (this.sizeCache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        try
        {
            // The texture cache hands out only a bare id, so dimensions come from the image headers on disk.
            var size = ReadImageSize(path);
            if (size is { X: > 0f, Y: > 0f } s)
            {
                this.sizeCache[path] = s;
                return s;
            }
        }
        catch (Exception)
        {
        }
        return null;
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

    private static bool RoundIconButton(string id, FontAwesomeIcon icon, Vector2 center, float radius, float iconPx)
    {
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton(id, new Vector2(radius * 2f, radius * 2f));
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircleFilled(center, radius, U32(new Vector4(0f, 0f, 0f, ImGui.IsItemHovered() ? 0.72f : 0.5f)), 32);
        AddIconCentered(dl, icon, iconPx, center, U32(WhiteText));
        return clicked;
    }

    private static bool ThemedButton(OsAppContext ctx, string label, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        ImGui.PushStyleColor(ImGuiCol.Button, ctx.Theme.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ctx.Theme.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ctx.Theme.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(9f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    /// <summary>A subtle dark pill button for secondary actions; <paramref name="accentOutline"/> marks an
    /// active toggle (the Edit button while the edit panel is open).</summary>
    private static bool GhostButton(OsAppContext ctx, string label, string id, Vector2 tl, Vector2 size, bool accentOutline = false)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + size, U32(new Vector4(1f, 1f, 1f, hovered ? 0.16f : 0.10f)), size.Y * 0.5f);
        if (accentOutline)
        {
            dl.AddRect(tl, tl + size, U32(ctx.Theme.Accent), size.Y * 0.5f, ImDrawFlags.RoundCornersAll, ctx.Px(1.4f));
        }
        var sz = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + (size - sz) * 0.5f, U32(accentOutline ? ctx.Theme.AccentLight : WhiteText), label);
        dl.PopClipRect();
        return clicked;
    }

    private static bool AccentButton(OsAppContext ctx, string label, Vector2 tl, Vector2 size)
    {
        ImGui.SetCursorScreenPos(tl);
        ImGui.PushStyleColor(ImGuiCol.Button, ctx.Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ctx.Theme.AccentLight);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ctx.Theme.AccentDark);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(9f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
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

    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
}
