using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>Everything about where it is allowed to stand. All of it is about the world outside the phone,
/// which is the only place any of these choices mean anything.</summary>
internal sealed class PetSettingsScreen
{
    /// <summary>Raised when anything here changes, so the app can persist it.</summary>
    public event Action? SettingsChanged;

    public event Action? RecentreRequested;

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
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void));

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

        if (!FloatingEnabled)
        {
            return;
        }

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
    }
}
