using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Yapper;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>The Yapper handle-claim wizard: welcome, handle (live debounced availability), display name
/// + bio, content rating. Creating the profile hands the fresh DTO back to the shell.</summary>
internal sealed class OnboardingScreen
{
    private const int TotalSteps = 4;
    private const double CheckDebounceSeconds = 0.6;

    private readonly IYapperHost _host;
    private readonly Action<YapperMyProfileDto> _done;

    private readonly AetherLove.Emoji.EmojiPickerPopup _bioEmojiPicker = new();
    private readonly AetherLove.UI.SoftWrapInputField _bioField = new();

    private int _step;
    private string _handle = string.Empty;
    private string _displayName = string.Empty;
    private string _bio = string.Empty;
    private bool _isNsfw;
    private bool _nsfwEnabled;

    // Blur defaults ON for new profiles; existing users keep the server default (off).
    private bool _blurNsfw = true;

    private string _checkedHandle = string.Empty;
    private YapperHandleCheck? _checkResult;
    private bool _checking;
    private DateTime _handleEditedAt;
    private bool _creating;
    private string? _error;

    public OnboardingScreen(IYapperHost host, Action<YapperMyProfileDto> done)
    {
        _host = host;
        _done = done;
    }

    public void OnShow()
    {
        _step = 0;
        _error = null;
        _creating = false;
        var osName = _host.OsDisplayName;
        if (_displayName.Length == 0 && !string.IsNullOrWhiteSpace(osName))
        {
            _displayName = osName.Trim();
        }
        if (_handle.Length == 0)
        {
            _handle = HandleFromName(osName);
        }
        _bioField.Reset(_bio);
    }

    /// <summary>Derives a valid handle (lowercase, [a-z0-9_], 3-20) from the OS display name.</summary>
    private static string HandleFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }
        var chars = new System.Text.StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                chars.Append(c);
            }
            else if (c is ' ' or '-' or '\'')
            {
                chars.Append('_');
            }
        }
        var slug = chars.ToString().Trim('_');
        while (slug.Contains("__"))
        {
            slug = slug.Replace("__", "_");
        }
        if (slug.Length > YapperLimits.HandleMaxLength)
        {
            slug = slug[..YapperLimits.HandleMaxLength].TrimEnd('_');
        }
        return slug.Length >= YapperLimits.HandleMinLength ? slug : string.Empty;
    }

    public void Draw(OsAppContext ctx)
    {
        if (DrawProgress(_step, TotalSteps, _step > 0) && _step > 0)
        {
            _step--;
        }

        const float topH = 34f;
        const float navH = 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##yapObContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawHandle(ctx);
                        break;
                    case 2:
                        DrawProfile();
                        break;
                    default:
                        DrawRating();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - Px(54f)));
        var last = _step >= TotalSteps - 1;
        var label = last
            ? (_creating ? Loc.T("os.yapper_ob_creating") : Loc.T("os.yapper_ob_create"))
            : Loc.T("onboarding.next");
        if (DrawPrimaryButton(label, StepValid() && !_creating))
        {
            if (last)
            {
                CreateProfile();
            }
            else
            {
                _step++;
            }
        }
    }

    private bool StepValid() => _step switch
    {
        1 => _checkResult == YapperHandleCheck.Available && _checkedHandle == _handle.Trim(),
        2 => _displayName.Trim().Length > 0,
        _ => true,
    };

    private void DrawWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("yapper_tour_welcome", FontAwesomeIcon.CommentDots,
            Loc.T("os.yapper_ob_welcome_title"), Loc.T("os.yapper_ob_welcome_body"));
    }

    private void DrawHandle(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawHero("yapper_tour_handle", FontAwesomeIcon.At,
            Loc.T("os.yapper_ob_handle_title"), Loc.T("os.yapper_ob_handle_body"));

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var pad = Px(24f);
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - pad * 2f);
        var handle = _handle;
        if (ImGui.InputText("##yapHandle", ref handle, YapperLimits.HandleMaxLength))
        {
            _handle = handle;
            _handleEditedAt = DateTime.UtcNow;
            _checkResult = null;
        }

        var trimmed = _handle.Trim();
        if (trimmed.Length >= YapperLimits.HandleMinLength
            && trimmed != _checkedHandle
            && !_checking
            && (DateTime.UtcNow - _handleEditedAt).TotalSeconds >= CheckDebounceSeconds)
        {
            RunCheck(trimmed);
        }

        ImGui.SetCursorPosX(pad);
        var (text, color) = HandleStatus(trimmed);
        ImGui.TextColored(color, text);
    }

    private (string Text, Vector4 Color) HandleStatus(string trimmed)
    {
        var muted = new Vector4(1f, 1f, 1f, 0.45f);
        if (trimmed.Length == 0)
        {
            return (Loc.T("os.yapper_handle_hint"), muted);
        }
        if (trimmed.Length < YapperLimits.HandleMinLength)
        {
            return (Loc.T("os.yapper_handle_invalid"), muted);
        }
        if (_checking || _checkResult is null || _checkedHandle != trimmed)
        {
            return (Loc.T("os.yapper_handle_checking"), muted);
        }
        return _checkResult switch
        {
            YapperHandleCheck.Available => (Loc.T("os.yapper_handle_available"), new Vector4(0.35f, 0.85f, 0.45f, 1f)),
            YapperHandleCheck.Taken => (Loc.T("os.yapper_handle_taken"), new Vector4(0.95f, 0.45f, 0.40f, 1f)),
            YapperHandleCheck.Rejected => (Loc.T("os.yapper_handle_rejected"), new Vector4(0.95f, 0.45f, 0.40f, 1f)),
            _ => (Loc.T("os.yapper_handle_invalid"), new Vector4(0.95f, 0.45f, 0.40f, 1f)),
        };
    }

    private void RunCheck(string handle)
    {
        _checking = true;
        Task.Run(async () =>
        {
            try
            {
                var result = await _host.CheckHandleAsync(handle).ConfigureAwait(false);
                _checkedHandle = handle;
                _checkResult = result;
            }
            catch (Exception)
            {
                _checkResult = null;
            }
            finally
            {
                _checking = false;
            }
        });
    }

    private void DrawProfile()
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawHero("yapper_tour_profile", FontAwesomeIcon.IdBadge,
            Loc.T("os.yapper_ob_profile_title"), Loc.T("os.yapper_ob_profile_body"));

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var pad = Px(24f);
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.65f), Loc.T("os.yapper_display_name"));
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - pad * 2f);
        var name = _displayName;
        if (ImGui.InputText("##yapDisplayName", ref name, YapperLimits.DisplayNameMaxLength))
        {
            _displayName = name;
        }

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.65f), Loc.T("os.yapper_bio"));
        ImGui.SameLine(ImGui.GetWindowSize().X - pad - Px(26f));
        var emojiClicked = ComposeScreen.DrawEmojiButton("##yapObBioEmoji");
        _bioEmojiPicker.Draw();
        if (emojiClicked)
        {
            _bioEmojiPicker.Open(InsertBioEmoji);
        }
        ImGui.SetCursorPosX(pad);
        _bioField.Draw("##yapBio", ref _bio, YapperLimits.BioRawMaxLength,
            new Vector2(ImGui.GetWindowSize().X - pad * 2f, Px(90f)));
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.35f),
            $"{EmojiText.EffectiveLength(_bio)}/{YapperLimits.BioMaxLength}");
    }

    private void InsertBioEmoji(string name)
    {
        var add = $":{name}: ";
        if (EmojiText.EffectiveLength(_bio + add) <= YapperLimits.BioMaxLength)
        {
            _bio += add;
        }
    }

    private void DrawRating()
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawHero("yapper_tour_rating", FontAwesomeIcon.ShieldAlt,
            Loc.T("os.yapper_ob_rating_title"), Loc.T("os.yapper_ob_rating_body"));

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var pad = Px(24f);
        ImGui.SetCursorPosX(pad);
        if (DrawToggleSwitch("##yapObNsfwProfile", Loc.T("os.yapper_rating_my_nsfw"), _isNsfw))
        {
            _isNsfw = !_isNsfw;
        }
        ImGui.SetCursorPosX(pad);
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_rating_my_nsfw_sub"));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(pad);
        if (DrawToggleSwitch("##yapObNsfwView", Loc.T("os.yapper_rating_see_nsfw"), _nsfwEnabled))
        {
            _nsfwEnabled = !_nsfwEnabled;
        }
        ImGui.SetCursorPosX(pad);
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_rating_see_nsfw_sub"));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(pad);
        if (DrawToggleSwitch("##yapObBlurNsfw", Loc.T("os.yapper_blur_nsfw"), _blurNsfw))
        {
            _blurNsfw = !_blurNsfw;
        }
        ImGui.SetCursorPosX(pad);
        ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - pad);
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.45f), Loc.T("os.yapper_blur_nsfw_sub"));
        ImGui.PopTextWrapPos();

        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(ImGui.GetWindowSize().X - pad);
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.40f, 1f), error);
            ImGui.PopTextWrapPos();
        }
    }

    private void CreateProfile()
    {
        _creating = true;
        _error = null;
        Task.Run(async () =>
        {
            try
            {
                var bio = _bioField.Value(_bio).Trim();
                var me = await _host.CreateProfileAsync(
                    _handle.Trim(), _displayName.Trim(),
                    bio.Length == 0 ? null : bio,
                    _isNsfw, _nsfwEnabled).ConfigureAwait(false);
                if (_blurNsfw)
                {
                    await _host.SetBlurNsfwAsync(true).ConfigureAwait(false);
                    me = me with { BlurNsfw = true };
                }
                _done(me);
            }
            catch (Exception ex)
            {
                _error = AetherLove.Services.HubErrorText.Localize(ex);
            }
            finally
            {
                _creating = false;
            }
        });
    }
}
