using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>A word-wrapping InputTextMultiline (with working Ctrl+V). The bound string holds the
/// DISPLAY text: the field reflows it on every edit by swapping spaces and newlines (length-preserving,
/// so the cursor never jumps), while tracking which newlines the user actually typed. <see cref="Value"/>
/// returns the text with the wrap-inserted breaks stripped back to spaces, so submitted content never
/// carries composer-width line breaks.</summary>
internal sealed class SoftWrapInputField
{
    private static SoftWrapInputField? _active;
    private static float _width;

    private string _last = string.Empty;
    private HashSet<int> _hard = [];

    /// <summary>Adopts freshly-assigned text (open/edit); every existing newline counts as user-typed.</summary>
    public void Reset(string text)
    {
        _last = text;
        _hard = [];
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _hard.Add(i);
            }
        }
    }

    /// <summary>The submit-ready text: wrap-inserted newlines become spaces, user-typed ones survive.</summary>
    public string Value(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n' && !_hard.Contains(i))
            {
                chars[i] = ' ';
            }
        }
        return new string(chars);
    }

    public bool Draw(string label, ref string text, int maxLength, Vector2 size)
    {
        if (text != _last)
        {
            // External change (emoji append, programmatic set): re-sync outside the widget.
            text = Sync(text);
        }
        _width = size.X - ImGui.GetStyle().FramePadding.X * 2f - ImGui.GetStyle().ScrollbarSize;
        _active = this;
        var changed = ImGui.InputTextMultiline(label, ref text, maxLength, size,
            ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackEdit, Callback);
        _active = null;
        SharedUiHelpers.ArmPasteIfIgnored(changed);
        return changed;
    }

    private static unsafe int Callback(ImGuiInputTextCallbackDataPtr data)
    {
        try
        {
            var field = _active;
            if (field is null)
            {
                return 0;
            }
            ImGuiInputTextCallbackData* p = data;
            if (p->EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                if (!SharedUiHelpers.TryConsumeArmedPaste(p))
                {
                    return 0;
                }
            }
            if (p->BufTextLen <= 0 || _width <= 0f)
            {
                field._last = string.Empty;
                field._hard.Clear();
                return 0;
            }
            var current = Encoding.UTF8.GetString(p->Buf, p->BufTextLen);
            var wrapped = field.Sync(current);
            if (wrapped != current)
            {
                var bytes = Encoding.UTF8.GetBytes(wrapped);
                if (bytes.Length == p->BufTextLen)
                {
                    for (var i = 0; i < bytes.Length; i++)
                    {
                        p->Buf[i] = bytes[i];
                    }
                    p->BufDirty = 1;
                }
            }
        }
        catch
        {
            // A managed exception must not cross into the native ImGui call.
        }
        return 0;
    }

    /// <summary>Diffs the buffer against the last-seen text to keep the hard-break set aligned (newlines
    /// inside the edited region are user-typed), then reflows and remembers the result.</summary>
    private string Sync(string current)
    {
        var old = _last;
        var prefix = 0;
        var maxPrefix = Math.Min(old.Length, current.Length);
        while (prefix < maxPrefix && old[prefix] == current[prefix])
        {
            prefix++;
        }
        var suffix = 0;
        while (suffix < maxPrefix - prefix
            && old[old.Length - 1 - suffix] == current[current.Length - 1 - suffix])
        {
            suffix++;
        }
        var delta = current.Length - old.Length;

        var hard = new HashSet<int>();
        foreach (var h in _hard)
        {
            if (h < prefix)
            {
                hard.Add(h);
            }
            else if (h >= old.Length - suffix)
            {
                hard.Add(h + delta);
            }
        }
        for (var i = prefix; i < current.Length - suffix; i++)
        {
            if (current[i] == '\n')
            {
                hard.Add(i);
            }
        }

        var chars = current.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n' && !hard.Contains(i))
            {
                chars[i] = ' ';
            }
        }
        Reflow(chars, _width);

        _hard = hard;
        _last = new string(chars);
        return _last;
    }

    /// <summary>Greedy length-preserving wrap: an overlong line swaps its last space for a newline;
    /// long unbroken words are left alone (the widget scrolls them).</summary>
    private static void Reflow(char[] chars, float width)
    {
        var lineStart = 0;
        var lastSpace = -1;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n')
            {
                lineStart = i + 1;
                lastSpace = -1;
                continue;
            }
            if (chars[i] == ' ')
            {
                lastSpace = i;
            }
            var line = new string(chars, lineStart, i - lineStart + 1);
            if (ImGui.CalcTextSize(line).X > width && lastSpace > lineStart)
            {
                chars[lastSpace] = '\n';
                lineStart = lastSpace + 1;
                lastSpace = -1;
            }
        }
    }
}
