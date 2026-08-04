using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using AetherLove.Shared.Profile.Enums;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Wayfinder;

/// <summary>The scout's authoring surface: stand at the spot, frame a landscape shot, name it, and submit.
/// The position is stamped host-side the moment the photo is taken, so walking away afterwards cannot move
/// the answer.</summary>
internal sealed class CreateScreen
{
    private const float PadX = 16f;

    /// <summary>Width-over-height of the challenge card, so the framed shot is what players will see.</summary>
    private const float ShotAspect = 1.61f;

    /// <summary>The camera frame wants height/width, the inverse of <see cref="ShotAspect"/>.</summary>
    private const float ShotCameraAspect = 1f / ShotAspect;

    private static string CacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "WayfinderCache");

    private readonly IWayfinderHost _host;
    private readonly Action _backToHome;
    private readonly EntranceAnimation _entrance = new();
    private readonly ConcurrentQueue<Action> _uiActions = new();

    private string _name = string.Empty;
    private Expansion _expansion = Expansion.ARealmReborn;
    private string? _shotPath;
    private Vector4 _shotCrop;
    private ISharedImmediateTexture? _shotTex;
    private string? _zoneName;
    private volatile bool _submitting;
    private volatile string? _error;
    private bool _done;
    private bool _pendingReview;

    public CreateScreen(IWayfinderHost host, Action backToHome)
    {
        _host = host;
        _backToHome = backToHome;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _name = string.Empty;
        _expansion = Expansion.ARealmReborn;
        _shotPath = null;
        _shotTex = null;
        _zoneName = null;
        _submitting = false;
        _error = null;
        _done = false;
        _pendingReview = false;
    }

    public void Draw(OsAppContext ctx)
    {
        while (_uiActions.TryDequeue(out var action))
        {
            action();
        }

        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;
        var winH = ImGui.GetWindowSize().Y;

        if (_done)
        {
            DrawDone(winW, winH);
            _entrance.EndFrame();
            return;
        }

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawHeader(winW);
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        DrawShotCard(ctx, winW);
        DrawNameField(winW);
        DrawExpansionPicker(winW);

        if (_error is { } error)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(58f)));
        var ready = _shotPath is not null && _name.Trim().Length > 0;
        if (_submitting)
        {
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(18f));
            LoadingSpinner.Draw(center, Px(12f), Px(3f), ImGui.GetColorU32(ThemeService.Current.Accent));
        }
        else
        {
            if (!ready)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);
            }
            var pressed = ModalUi.Button(Loc.T("os.wf_create_submit"), winW - Px(PadX) * 2f);
            if (!ready)
            {
                ImGui.PopStyleVar();
            }
            if (pressed && ready)
            {
                Submit();
            }
        }

        _entrance.EndFrame();
    }

    private void DrawHeader(float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(Px(PadX));
        var backH = ImGui.GetTextLineHeight() + Px(10f);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfCreateBack", new Vector2(backH, backH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        dl.AddCircleFilled(tl + new Vector2(backH * 0.5f, backH * 0.5f), backH * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f)));
        var arrowPx = ImGui.GetFontSize() * 0.7f;
        var arrowSz = IconDraw.Measure(FontAwesomeIcon.ArrowLeft, arrowPx);
        IconDraw.Add(dl, FontAwesomeIcon.ArrowLeft, arrowPx,
            tl + new Vector2((backH - arrowSz.X) * 0.5f, (backH - arrowSz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Body));

        float titleH;
        using (UiFonts.H3?.Push())
        {
            titleH = ImGui.GetTextLineHeight();
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(tl.X + backH + Px(10f), tl.Y + (backH - titleH) * 0.5f),
                ImGui.GetColorU32(UiColors.Body), Loc.T("os.wf_create_title"));
        }
        ImGui.Dummy(new Vector2(0f, Px(2f)));

        if (clicked)
        {
            _backToHome();
        }
    }

    /// <summary>The framed landscape shot, or the prompt that takes it.</summary>
    private void DrawShotCard(OsAppContext ctx, float winW)
    {
        var t = ThemeService.Current;
        var cardW = winW - Px(PadX) * 2f;
        var cardH = cardW / ShotAspect;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfCreateShot", new Vector2(cardW, cardH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        var wrap = _shotTex?.GetWrapOrDefault();
        if (wrap is not null)
        {
            dl.AddImageRounded(wrap.Handle, tl, tl + new Vector2(cardW, cardH), Vector2.Zero, Vector2.One,
                0xFFFFFFFFu, Px(16f));
            var retake = Loc.T("os.wf_create_retake");
            var retakeSz = ImGui.CalcTextSize(retake);
            var pillW = retakeSz.X + Px(20f);
            var pillH = retakeSz.Y + Px(10f);
            var pillTl = new Vector2(tl.X + cardW - pillW - Px(8f), tl.Y + cardH - pillH - Px(8f));
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillW, pillH),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, hovered ? 0.75f : 0.55f)), pillH * 0.5f);
            dl.AddText(pillTl + new Vector2(Px(10f), Px(5f)), 0xFFFFFFFFu, retake);
        }
        else
        {
            dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
                ImGui.GetColorU32(t.Accent with { W = hovered ? 0.20f : 0.12f }), Px(16f));
            dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(t.Accent with { W = 0.5f }), Px(16f),
                ImDrawFlags.None, Px(1.4f));
            var camPx = Px(30f);
            var camSz = IconDraw.Measure(FontAwesomeIcon.CameraRetro, camPx);
            var label = Loc.T("os.wf_create_take");
            var labelSz = ImGui.CalcTextSize(label);
            var blockH = camSz.Y + Px(10f) + labelSz.Y;
            IconDraw.Add(dl, FontAwesomeIcon.CameraRetro, camPx,
                new Vector2(tl.X + (cardW - camSz.X) * 0.5f, tl.Y + (cardH - blockH) * 0.5f),
                ImGui.GetColorU32(t.AccentLight));
            dl.AddText(new Vector2(tl.X + (cardW - labelSz.X) * 0.5f, tl.Y + (cardH - blockH) * 0.5f + camSz.Y + Px(10f)),
                ImGui.GetColorU32(UiColors.Body), label);
        }

        ImGui.Dummy(new Vector2(0f, Px(8f)));

        ImGui.SetCursorPosX(Px(PadX));
        var hint = _zoneName is { Length: > 0 } zone
            ? Loc.T("os.wf_create_stamped", zone)
            : Loc.T("os.wf_create_hint");
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(_zoneName is { Length: > 0 } ? t.AccentLight : UiColors.Hint, hint);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        if (clicked && !_submitting)
        {
            TakeShot(ctx);
        }
    }

    private void DrawNameField(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.wf_create_name"));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.SetNextItemWidth(winW - Px(PadX) * 2f);
        ImGui.InputTextWithHint("##wfCreateName", Loc.T("os.wf_create_name_hint"), ref _name, 100);
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    private void DrawExpansionPicker(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.wf_create_expansion"));
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var choices = ProfileFields.ExpansionValues.Where(e => e != Expansion.None).ToArray();
        var rowW = winW - Px(PadX) * 2f;
        var gap = Px(8f);
        // Sized to fit one row whatever the phone scale, so the picker never wraps mid-list.
        var badge = MathF.Min(Px(38f), (rowW - gap * (choices.Length - 1)) / choices.Length);
        var rowTl = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(rowW, badge));

        for (var i = 0; i < choices.Length; i++)
        {
            var expansion = choices[i];
            var tl = new Vector2(rowTl.X + Px(PadX) + i * (badge + gap), rowTl.Y);
            if (expansion == _expansion)
            {
                dl.AddCircleFilled(tl + new Vector2(badge * 0.5f, badge * 0.5f), badge * 0.5f + Px(3f),
                    ImGui.GetColorU32(t.Accent with { W = 0.55f }));
            }
            ExpansionBadge.Draw(dl, tl, badge, (short)expansion);
            if (ImGui.IsMouseHoveringRect(tl, tl + new Vector2(badge, badge)))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _expansion = expansion;
                }
            }
        }
        ImGui.Dummy(new Vector2(0f, Px(2f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Body, ExpansionBadge.Label((short)_expansion));
        ImGui.Dummy(new Vector2(0f, Px(8f)));
    }

    private void DrawDone(float winW, float winH)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();

        ImGui.Dummy(new Vector2(0f, Px(60f)));
        var circle = Px(84f);
        var center = new Vector2(winPos.X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(46f));
        var tint = _pendingReview ? UiColors.WarningAccent : UiColors.LiveGreen;
        dl.AddCircleFilled(center, circle * 0.5f, ImGui.GetColorU32(tint with { W = 0.20f }));
        dl.AddCircle(center, circle * 0.5f, ImGui.GetColorU32(tint), 0, Px(2f));
        IconDraw.AddCentered(dl, _pendingReview ? FontAwesomeIcon.Hourglass : FontAwesomeIcon.Check,
            circle * 0.45f, center, ImGui.GetColorU32(tint));
        ImGui.Dummy(new Vector2(0f, Px(96f)));

        using (UiFonts.H1?.Push())
        {
            var title = _pendingReview ? Loc.T("os.wf_create_pending_title") : Loc.T("os.wf_create_done_title");
            var sz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((winW - sz.X) * 0.5f);
            ImGui.TextColored(t.AccentLight, title);
        }

        var body = _pendingReview ? Loc.T("os.wf_create_pending_body") : Loc.T("os.wf_create_done_body");
        var wrapW = winW - Px(PadX) * 3f;
        var bodySz = ImGui.CalcTextSize(body, false, wrapW);
        ImGui.SetCursorPosX((winW - Math.Min(bodySz.X, wrapW)) * 0.5f);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextColored(UiColors.Body, body);
        ImGui.PopTextWrapPos();

        ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(58f)));
        if (ModalUi.Button(Loc.T("os.wf_done"), winW - Px(PadX) * 2f))
        {
            _backToHome();
        }
    }

    private void TakeShot(OsAppContext ctx)
    {
        ctx.Capabilities.Camera.Capture(new CameraRequest(ShotCameraAspect, 320),
            shot => _uiActions.Enqueue(() => StageShot(shot.Path, shot.Crop)));
    }

    private void StageShot(string path, Vector4 crop)
    {
        _shotPath = path;
        _shotCrop = crop;
        _shotTex = null;
        _error = null;
        // The position is stamped now, with the picture, so the answer cannot drift if the author walks off.
        _ = Task.Run(async () =>
        {
            try
            {
                var zone = await _host.CaptureWaypointAsync().ConfigureAwait(false);
                _uiActions.Enqueue(() => _zoneName = zone);
            }
            catch (Exception ex)
            {
                var message = HubErrorText.Localize(ex);
                _uiActions.Enqueue(() => _error = message);
            }
        });
        try
        {
            _shotTex = AvatarDiskCache.Store(CacheDir, $"draft_{Guid.NewGuid():N}", File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            _shotTex = null;
        }
    }

    private void Submit()
    {
        if (_shotPath is not { } path)
        {
            return;
        }
        _submitting = true;
        _error = null;
        var name = _name.Trim();
        var expansion = (short)_expansion;
        var crop = _shotCrop;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _host.SubmitWaypointAsync(name, expansion, path, crop).ConfigureAwait(false);
                _uiActions.Enqueue(() =>
                {
                    _pendingReview = result.PendingReview;
                    _done = true;
                });
            }
            catch (Exception ex)
            {
                var message = HubErrorText.Localize(ex);
                _uiActions.Enqueue(() => _error = message);
            }
            finally
            {
                _submitting = false;
            }
        });
    }
}
