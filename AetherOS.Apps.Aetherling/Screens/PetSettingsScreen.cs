using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>Where it is allowed to stand, and whether it is allowed to make a noise.</summary>
internal sealed class PetSettingsScreen(IAetherlingHost host)
{
    /// <summary>Raised when anything here changes, so the app can persist it.</summary>
    public event Action? SettingsChanged;

    public event Action? RecentreRequested;

    /// <summary>Raised when a sound setting settles, so the app can persist it. Separate from
    /// <see cref="SettingsChanged"/> because the volume moves continuously while it is dragged and only
    /// the value it is let go on is worth writing.</summary>
    public event Action? SoundsChanged;

    public bool FloatingEnabled { get; set; }

    public bool FloatingLocked { get; set; }

    private int _size = FloatingPet.DefaultSizeIndex;

    public int FloatingSize
    {
        get => _size;
        set => _size = value;
    }

    public void Draw(OsAppContext ctx, AetherlingDto core, Action onBack)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        Look.Backdrop(dl, ctx.Theme, origin, size);

        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var pad = Px(18f);
        var y = PetPageUi.Header(ctx, dl, origin, name,
            string.Format(ctx.Localize("os.aetherling_menu_settings"), name), onBack);

        dl.AddText(new Vector2(origin.X + pad, y), Look.U32(Look.Crystal, 0.85f),
            ctx.Localize("os.aetherling_status_outside"));
        y += Px(26f);

        if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_status_show_outside"),
                FloatingEnabled))
        {
            FloatingEnabled = !FloatingEnabled;
            SettingsChanged?.Invoke();
        }
        y += Px(42f);

        if (FloatingEnabled)
        {
            dl.AddText(new Vector2(origin.X + pad, y + Px(4f)), Look.U32(Look.Whisper, 0.85f),
                ctx.Localize("os.aetherling_size_label"));
            y += Px(26f);
            if (PetSizePicker.Draw(ctx, dl, new Vector2(origin.X + pad, y), size.X - (pad * 2f), ref _size))
            {
                SettingsChanged?.Invoke();
            }
            y += Px(48f);

            if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_status_lock"), FloatingLocked))
            {
                FloatingLocked = !FloatingLocked;
                SettingsChanged?.Invoke();
            }
            y += Px(42f);

            if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_status_recentre"), null))
            {
                RecentreRequested?.Invoke();
            }
            y += Px(42f);
        }

        // Its voice. Nothing to do with the floating window, so it sits outside that section rather than
        // disappearing with it.
        y += Px(8f);
        dl.AddText(new Vector2(origin.X + pad, y), Look.U32(Look.Crystal, 0.85f),
            ctx.Localize("os.aetherling_sound_section"));
        y += Px(26f);

        // The crystal's loop carries on through the growing forms, so a pet that is not yet grown has music
        // playing on a page whose only other switch is out on the pre-hatch screens. This is where somebody
        // looks for it.
        if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_sound_music"), !host.BgmMuted))
        {
            host.BgmMuted = !host.BgmMuted;
            SoundsChanged?.Invoke();
        }
        y += Px(42f);

        if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_sound_noises"), !host.SoundsMuted))
        {
            host.SoundsMuted = !host.SoundsMuted;
            SoundsChanged?.Invoke();
        }
        y += Px(42f);

        if (host.SoundsMuted)
        {
            return;
        }
        var volume = host.SoundVolume;
        if (PetPageUi.Slider(dl, origin, size, y, ctx.Localize("os.aetherling_sound_volume"), ref volume,
                out var settled))
        {
            host.SoundVolume = volume;
        }
        if (settled)
        {
            SoundsChanged?.Invoke();
            // One chirp at the level just chosen, because a volume nobody hears is a number.
            host.PlayChirp();
        }
    }
}
