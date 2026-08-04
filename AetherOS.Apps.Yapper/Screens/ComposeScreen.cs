using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Yapper;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>The full-bleed composer: new yap, reply, quote and text edit share it. Emoji-aware char
/// counter, image strip via the shared picker, content-warning toggle and audience picker. The draft
/// survives back-navigation in memory until posted or discarded.</summary>
internal sealed class ComposeScreen
{
    internal enum Mode { New, Reply, Quote, Edit }

    private readonly IYapperHost _host;
    private readonly YapperStore _store;
    private readonly Action<YapDto> _posted;
    private readonly Action _cancelled;

    private Mode _mode = Mode.New;
    private YapDto? _target;
    private string _text = string.Empty;
    private readonly List<(string Path, byte[] Bytes, Vector4? Crop)> _images = [];

    // Attachments finish preparing on a worker thread; they queue here and join _images on the draw thread.
    private readonly ConcurrentQueue<(string Path, byte[] Bytes, Vector4? Crop)> _prepared = new();
    private IAppCapabilities? _caps;
    private bool _contentWarning;
    private bool _followersOnly;
    private YapEmbedKind _embedKind = YapEmbedKind.None;
    private Guid? _embedId;
    private string _embedTitle = string.Empty;
    private volatile bool _posting;
    private volatile string? _error;
    private readonly AetherLove.Emoji.EmojiPickerPopup _emojiPicker = new();
    private readonly SoftWrapInputField _textField = new();

    public ComposeScreen(IYapperHost host, YapperStore store, Action<YapDto> posted, Action cancelled,
        ImageSourceSheet imageSheet, Action<Action<string>> pickFromPhotos)
    {
        _host = host;
        _store = store;
        _posted = posted;
        _cancelled = cancelled;
        _imageSheet = imageSheet;
        _pickFromPhotos = pickFromPhotos;
    }

    private readonly ImageSourceSheet _imageSheet;
    private readonly Action<Action<string>> _pickFromPhotos;

    public void Open(Mode mode, YapDto? target)
    {
        _mode = mode;
        _target = target;
        _text = mode == Mode.Edit ? target?.Text ?? string.Empty : string.Empty;
        _images.Clear();
        while (_prepared.TryDequeue(out _))
        {
        }
        _contentWarning = false;
        _followersOnly = false;
        _embedKind = YapEmbedKind.None;
        _embedId = null;
        _embedTitle = string.Empty;
        _error = null;
        _posting = false;
        _textField.Reset(_text);
    }

    /// <summary>Opens the composer with shared-in content attached as an embed.</summary>
    public void OpenShare(YapEmbedKind kind, Guid id, string title)
    {
        Open(Mode.New, null);
        _embedKind = kind;
        _embedId = id;
        _embedTitle = title;
    }

    public void Draw(OsAppContext ctx)
    {
        _caps = ctx.Capabilities;
        while (_prepared.TryDequeue(out var image))
        {
            if (_images.Count < SupporterLimits.MaxYapImages(_host.IsSupporter))
            {
                _images.Add(image);
            }
        }

        var winW = ImGui.GetWindowSize().X;
        var pad = Px(14f);

        if (_posting)
        {
            // First-submitted wins: this swallows every click while the request runs.
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.InvisibleButton("##yapPostingBlock", ImGui.GetWindowSize());
            ImGui.SetCursorPos(Vector2.Zero);
        }

        ImGui.SetCursorPos(new Vector2(pad, Px(12f)));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("common.cancel"), FontAwesomeIcon.Times))
        {
            _cancelled();
        }

        var postLabel = _posting ? Loc.T("os.yapper_posting") : Loc.T(_mode switch
        {
            Mode.Reply => "os.yapper_reply_btn",
            Mode.Edit => "os.yapper_save_btn",
            _ => "os.yapper_post_btn",
        });
        var canPost = !_posting
            && (YapTextParser.EffectiveLength(_text) > 0 || _images.Count > 0 || _embedKind != YapEmbedKind.None);
        DrawPostPill(ctx, postLabel, canPost, winW, pad);

        if (_mode is Mode.Reply or Mode.Quote && _target is not null)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(winW - pad);
            var context = _mode == Mode.Reply ? "os.yapper_replying_to" : "os.yapper_quoting";
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.5f),
                string.Format(Loc.T(context), $"@{_target.Author?.Handle}"));
            var snippet = _target.Text ?? string.Empty;
            if (snippet.Length > 120)
            {
                snippet = snippet[..120] + "…";
            }
            if (snippet.Length > 0)
            {
                ImGui.SetCursorPosX(pad);
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.35f), snippet);
            }
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(pad);
        _textField.Draw("##yapComposeText", ref _text, YapperLimits.TextRawMaxLength,
            new Vector2(winW - pad * 2f, Px(140f)));
        DrawMentionAutocomplete(ctx, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        var limit = YapperLimits.MaxTextLength(_host.IsSupporter);
        var used = YapTextParser.EffectiveLength(_text);
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(used > limit
            ? new Vector4(0.95f, 0.4f, 0.4f, 1f)
            : new Vector4(1f, 1f, 1f, 0.4f), $"{used}/{limit}");
        ImGui.SameLine(winW - pad - Px(26f));
        var emojiClicked = DrawEmojiButton("##yapComposeEmoji");
        _emojiPicker.Draw();
        if (emojiClicked)
        {
            _emojiPicker.Open(name => _text += $":{name}: ");
        }

        if (_embedKind != YapEmbedKind.None)
        {
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(ctx.Theme.Accent, _embedTitle);
            ImGui.SameLine();
            if (ImGui.SmallButton($"×##yapEmbedRemove"))
            {
                _embedKind = YapEmbedKind.None;
                _embedId = null;
            }
            HandOnHover();
        }

        if (_mode != Mode.Edit)
        {
            DrawImageStrip(ctx, pad, winW);
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            ImGui.SetCursorPosX(pad);
            if (DrawToggleSwitch("##yapCw", Loc.T("os.yapper_cw_toggle"), _contentWarning))
            {
                _contentWarning = !_contentWarning;
            }
            if (_mode != Mode.Reply)
            {
                ImGui.SetCursorPosX(pad);
                if (DrawToggleSwitch("##yapAudience", Loc.T("os.yapper_followers_only"), _followersOnly))
                {
                    _followersOnly = !_followersOnly;
                }
            }
        }

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.4f, 1f), error);
            ImGui.PopTextWrapPos();
        }

        if (_posting)
        {
            var dl = ImGui.GetWindowDrawList();
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            dl.AddRectFilled(winPos, winPos + winSize, 0xB2000000u);
            AetherLove.Widgets.LoadingSpinner.Draw(winPos + winSize * 0.5f, Px(16f), Px(3f),
                ImGui.GetColorU32(ctx.Theme.Accent));
            var label = Loc.T("os.yapper_posting");
            dl.AddText(winPos + new Vector2((winSize.X - ImGui.CalcTextSize(label).X) * 0.5f, winSize.Y * 0.5f + Px(28f)),
                0xFFFFFFFFu, label);
        }
    }

    /// <summary>The shared emoji-picker button: the grinning emoji image with a text fallback.</summary>
    internal static bool DrawEmojiButton(string id)
    {
        var iconH = ImGui.GetTextLineHeight();
        var grinTex = AetherLove.UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
        var clicked = grinTex != null
            ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(2f)))
            : ImGui.SmallButton(":)");
        ImGui.PopStyleVar();
        ImGui.PopID();
        HandOnHover();
        return clicked;
    }

    private void DrawPostPill(OsAppContext ctx, string label, bool enabled, float winW, float pad)
    {
        var pillH = Px(30f);
        var pillW = ImGui.CalcTextSize(label).X + Px(32f);
        ImGui.SetCursorPos(new Vector2(winW - pillW - pad, Px(10f)));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##yapComposePost", new Vector2(pillW, pillH)) && enabled;
        if (enabled)
        {
            HandOnHover();
        }
        var dl = ImGui.GetWindowDrawList();
        var fill = enabled
            ? (ImGui.IsItemHovered() ? ctx.Theme.Accent with { W = 0.88f } : ctx.Theme.Accent)
            : ctx.Theme.Accent with { W = 0.35f };
        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH), ImGui.GetColorU32(fill), pillH * 0.5f);
        dl.AddText(tl + new Vector2(Px(16f), (pillH - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, enabled ? 1f : 0.6f)), label);
        if (clicked)
        {
            Submit();
        }
    }

    private void DrawImageStrip(OsAppContext ctx, float pad, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(pad);
        var thumb = Px(56f);
        for (var i = 0; i < _images.Count; i++)
        {
            var tex = UiHost.TextureProvider.GetFromFile(_images[i].Path).GetWrapOrDefault();
            var dl = ImGui.GetWindowDrawList();
            var tl = ImGui.GetCursorScreenPos();
            if (tex is not null)
            {
                var (uv0, uv1) = CoverFitUvs(tex.Width, tex.Height, thumb, thumb);
                dl.AddImageRounded(tex.Handle, tl, tl + new Vector2(thumb, thumb), uv0, uv1, 0xFFFFFFFFu, Px(8f));
            }
            else
            {
                dl.AddRectFilled(tl, tl + new Vector2(thumb, thumb), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(8f));
            }
            if (ImGui.InvisibleButton($"##yapImg{i}", new Vector2(thumb, thumb)))
            {
                _images.RemoveAt(i);
                return;
            }
            HandOnHover();
            if (ImGui.IsItemHovered())
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Trash, Px(15f), tl + new Vector2(thumb, thumb) * 0.5f,
                    ImGui.GetColorU32(new Vector4(1f, 0.4f, 0.4f, 0.95f)));
            }
            ImGui.SameLine();
        }

        var cap = SupporterLimits.MaxYapImages(_host.IsSupporter);
        if (_images.Count < cap)
        {
            var dl = ImGui.GetWindowDrawList();
            var tl = ImGui.GetCursorScreenPos();
            dl.AddRect(tl, tl + new Vector2(thumb, thumb), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.25f)), Px(8f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, Px(16f), tl + new Vector2(thumb, thumb) * 0.5f,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)));
            if (ImGui.InvisibleButton("##yapAddImg", new Vector2(thumb, thumb)))
            {
                var caps = ctx.Capabilities;
                _imageSheet.Open(
                    onSelfie: () => caps.Camera.Capture(new CameraRequest(FreeForm: true),
                        shot => AddImage(shot.Path, shot.Crop)),
                    onPhotos: () => _pickFromPhotos(path => AddImage(path)),
                    onFile: () => caps.Images.PickFile(
                        new ImagePickRequest(Loc.T("os.yapper_pick_image"), "Images{.png,.jpg,.jpeg,.webp}"),
                        path => AddImage(path)));
            }
            HandOnHover();
        }
        ImGui.NewLine();
    }

    private string? _mentionFetchedToken;
    private volatile YapperUserRowDto[]? _mentionResults;
    private volatile bool _mentionFetching;

    /// <summary>A trailing "@tok" (3+ handle chars, at the start or after whitespace) is the live
    /// mention query; soft-wrap newlines count as whitespace so wrapping never breaks detection.</summary>
    private static string? TrailingMentionToken(string text)
    {
        var i = text.Length - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i--;
        }
        if (i < 0 || text[i] != '@' || (i > 0 && !char.IsWhiteSpace(text[i - 1])))
        {
            return null;
        }
        var token = text[(i + 1)..];
        return token.Length >= 3 ? token : null;
    }

    /// <summary>Floating accent pills hovering just under the typed text inside the input, so the
    /// completion is a tap away from where the user is looking. Drawn in a child so it renders and
    /// hit-tests above the input beneath.</summary>
    private void DrawMentionAutocomplete(OsAppContext ctx, Vector2 fieldMin, Vector2 fieldMax)
    {
        var token = TrailingMentionToken(_text);
        if (token is null)
        {
            _mentionFetchedToken = null;
            _mentionResults = null;
            return;
        }
        if (token != _mentionFetchedToken && !_mentionFetching)
        {
            _mentionFetching = true;
            _mentionFetchedToken = token;
            _ = Task.Run(async () =>
            {
                try
                {
                    var rows = await _host.SearchUsersAsync(token).ConfigureAwait(false);
                    _mentionResults = rows.Length > 5 ? rows[..5] : rows;
                }
                catch (Exception)
                {
                    _mentionResults = null;
                }
                finally
                {
                    _mentionFetching = false;
                }
            });
        }

        if (_mentionResults is not { Length: > 0 } results)
        {
            return;
        }

        var style = ImGui.GetStyle();
        var rowH = Px(24f);
        var textH = ImGui.CalcTextSize(_text.Length == 0 ? " " : _text).Y;
        var y = Math.Min(fieldMin.Y + style.FramePadding.Y + textH + Px(4f), fieldMax.Y - rowH - Px(4f));
        var origin = new Vector2(fieldMin.X + Px(6f), y);
        var saved = ImGui.GetCursorPos();

        ImGui.SetCursorScreenPos(origin);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using (var layer = Dalamud.Interface.Utility.Raii.ImRaii.Child("##yapMentionAc",
                   new Vector2(fieldMax.X - origin.X - Px(6f), rowH), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground))
        {
            if (layer)
            {
                var dl = ImGui.GetWindowDrawList();
                var availW = ImGui.GetWindowSize().X;
                var x = 0f;
                foreach (var row in results)
                {
                    var label = $"@{row.Handle}";
                    var pillW = ImGui.CalcTextSize(label).X + Px(16f);
                    if (x + pillW > availW)
                    {
                        break;
                    }
                    ImGui.SetCursorPos(new Vector2(x, 0f));
                    var tl = ImGui.GetCursorScreenPos();
                    var clicked = ImGui.InvisibleButton($"##yapMention{row.ProfileId:N}", new Vector2(pillW, rowH));
                    HandOnHover();
                    var hovered = ImGui.IsItemHovered();
                    dl.AddRectFilled(tl, tl + new Vector2(pillW, rowH),
                        ImGui.GetColorU32(ctx.Theme.Accent with { W = hovered ? 1f : 0.85f }), rowH * 0.5f);
                    dl.AddText(tl + new Vector2(Px(8f), (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                        0xFFFFFFFFu, label);
                    if (clicked)
                    {
                        if (TrailingMentionToken(_text) is { } current)
                        {
                            _text = _text[..^current.Length] + row.Handle + " ";
                        }
                        _mentionFetchedToken = null;
                        _mentionResults = null;
                        break;
                    }
                    x += pillW + Px(6f);
                }
            }
        }
        ImGui.PopStyleVar();
        ImGui.SetCursorPos(saved);
    }

    /// <summary>Oversized picks are downscaled host-side before upload (the server re-encodes to fit
    /// 1920x1080 anyway), so a stack of raw screenshots can never blow past the hub message limit.</summary>
    private void AddImage(string path, Vector4? crop = null)
    {
        var effects = _caps?.Effects;
        if (effects is null)
        {
            AddPrepared(path, crop);
            return;
        }
        effects.PrepareUpload(path, 1920, 1080, (prepared, scale) =>
        {
            if (prepared is null)
            {
                _error = Loc.T("huberror.yap_image_invalid");
                return;
            }
            AddPrepared(prepared, crop is { } c ? c * scale : null);
        });
    }

    private void AddPrepared(string path, Vector4? crop)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 0 && bytes.Length <= YapperLimits.MaxImageUploadBytes)
            {
                _prepared.Enqueue((path, bytes, crop));
                _error = null;
            }
            else
            {
                _error = Loc.T("huberror.yap_image_too_large");
            }
        }
        catch (Exception)
        {
            _error = Loc.T("huberror.yap_image_invalid");
        }
    }

    private void Submit()
    {
        _posting = true;
        _error = null;
        var mode = _mode;
        var target = _target;
        var text = _textField.Value(_text);
        var images = _images.Count == 0
            ? null
            : _images.ConvertAll(i => new AetherLove.Shared.Profile.PhotoUploadDto(
                Convert.ToBase64String(i.Bytes),
                (int)(i.Crop?.X ?? 0), (int)(i.Crop?.Y ?? 0), (int)(i.Crop?.Z ?? 0), (int)(i.Crop?.W ?? 0),
                false)).ToArray();
        var visibility = _followersOnly ? YapVisibility.FollowersOnly : YapVisibility.Everyone;

        _ = Task.Run(async () =>
        {
            try
            {
                YapDto result;
                if (mode == Mode.Edit && target is not null)
                {
                    result = await _host.EditYapAsync(target.Id, text).ConfigureAwait(false);
                }
                else
                {
                    var req = mode switch
                    {
                        Mode.Reply => new YapCreateDto(YapKind.Reply, text, target!.Id, null, visibility, _contentWarning, images),
                        Mode.Quote => new YapCreateDto(YapKind.Repost, text, null, target!.Id, visibility, _contentWarning, images),
                        _ => new YapCreateDto(YapKind.Post, text, null, null, visibility, _contentWarning, images,
                            _embedKind, _embedId),
                    };
                    result = await _host.CreateYapAsync(req).ConfigureAwait(false);
                }
                _store.Upsert(result);
                _posted(result);
            }
            catch (Exception ex)
            {
                _error = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _posting = false;
            }
        });
    }
}
