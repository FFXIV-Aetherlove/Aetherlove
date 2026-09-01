using System;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>One server-info-bar entry that exists only while it has something to say.
///
/// <para>A registered entry reserves its slot whatever its text is, and third-party bars (Umbra and
/// friends) draw that slot as an empty square. Hiding it through <see cref="IDtrBarEntry.Shown"/> is
/// not enough either: an entry registered and never written to keeps whatever Dalamud created it with,
/// which is how the plugin used to leave two blank squares up there for anyone who had switched Groove
/// and Timers off. So the entry itself is created on the first thing worth showing and REMOVED the
/// moment there is nothing, which is what a player who turned the bar off asked for.</para></summary>
internal sealed class DtrTextEntry(IDtrBar bar, string title, Action onClick)
{
    private IDtrBarEntry? _entry;
    private string _text = string.Empty;

    /// <summary>Shows <paramref name="text"/>, or removes the entry when it is null or empty.</summary>
    public void Set(string? text)
    {
        if (text is not { Length: > 0 })
        {
            Remove();
            return;
        }
        if (_entry is null)
        {
            _entry = bar.Get(title);
            _entry.OnClick = _ => onClick();
            _text = string.Empty;
        }
        if (text == _text)
        {
            return;
        }
        _text = text;
        _entry.Shown = true;
        _entry.Text = new SeStringBuilder().AddText(text).Build();
    }

    public void Remove()
    {
        _entry?.Remove();
        _entry = null;
        _text = string.Empty;
    }
}
