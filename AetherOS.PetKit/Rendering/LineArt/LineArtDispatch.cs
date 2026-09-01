using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.PetKit.Rendering.LineArt;

/// <summary>The drawn-shell registry and its per-shell dispatch, the ONE place that knows how far
/// the conversion has got. A shell missing from the table never dispatches to a drawn body, which
/// is how the hatchlings stay sheets; the prototype once shipped a shell drawn-but-unreachable
/// because a draw branch existed with no row here. Adding a shell is one new file in this folder
/// plus one row in each switch. Id 10 is the default arm, so an unrecognised drawn shell falls
/// back to the trueform rather than to nothing.</summary>
public static class LineArtDispatch
{
    private static readonly (string Prefix, int Id)[] Shells =
    [
        ("jelly", 1),
        ("crab", 2),
        ("puffer", 3),
        ("nautilus", 4),
        ("serpent", 5),
        ("moth", 6),
        ("lantern", 7),
        ("spintop", 8),
        ("pennant", 9),
        ("wisp", 10),
        ("muffle", 11),
        ("chime", 12),
        ("grumble", 13),
        ("smoulder", 14),
    ];

    /// <summary>Which drawn shell a skin key maps to, or 0 for one that is still a sheet.</summary>
    public static int ShellFor(string skin)
    {
        foreach (var (prefix, id) in Shells)
        {
            if (skin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return 0;
    }

    public static LineShell.Material StuffFor(int shell) => shell switch
    {
        1 => JellyLineArt.Stuff,
        2 => CrabLineArt.Stuff,
        3 => PufferLineArt.Stuff,
        4 => NautilusLineArt.Stuff,
        5 => SerpentLineArt.Stuff,
        6 => MothLineArt.Stuff,
        7 => LanternLineArt.Stuff,
        8 => SpintopLineArt.Stuff,
        9 => PennantLineArt.Stuff,
        11 => MuffleLineArt.Stuff,
        12 => ChimeLineArt.Stuff,
        13 => GrumbleLineArt.Stuff,
        14 => SmoulderLineArt.Stuff,
        _ => WispLineArt.Stuff,
    };

    public static LineShell.Channels PoseAt(int shell, int prev, int cell, int next, int after, float phase) =>
        shell switch
        {
            1 => JellyLineArt.PoseAt(prev, cell, next, after, phase),
            2 => CrabLineArt.PoseAt(prev, cell, next, after, phase),
            3 => PufferLineArt.PoseAt(prev, cell, next, after, phase),
            4 => NautilusLineArt.PoseAt(prev, cell, next, after, phase),
            5 => SerpentLineArt.PoseAt(prev, cell, next, after, phase),
            6 => MothLineArt.PoseAt(prev, cell, next, after, phase),
            7 => LanternLineArt.PoseAt(prev, cell, next, after, phase),
            8 => SpintopLineArt.PoseAt(prev, cell, next, after, phase),
            9 => PennantLineArt.PoseAt(prev, cell, next, after, phase),
            11 => MuffleLineArt.PoseAt(prev, cell, next, after, phase),
            12 => ChimeLineArt.PoseAt(prev, cell, next, after, phase),
            13 => GrumbleLineArt.PoseAt(prev, cell, next, after, phase),
            14 => SmoulderLineArt.PoseAt(prev, cell, next, after, phase),
            _ => WispLineArt.PoseAt(prev, cell, next, after, phase),
        };

    /// <summary>The shell's own face at this moment of its clip, before anything outside the
    /// pose table has had its say. The runtime takes it from here and may override the lid or
    /// raise the blush; the shell never learns that happened.</summary>
    public static (LineShell.EyeState Eye, float Blush) FaceAt(int shell, int cell, int next, float phase) =>
        shell switch
        {
            1 => JellyLineArt.FaceAt(cell, next, phase),
            2 => CrabLineArt.FaceAt(cell, next, phase),
            3 => PufferLineArt.FaceAt(cell, next, phase),
            4 => NautilusLineArt.FaceAt(cell, next, phase),
            5 => SerpentLineArt.FaceAt(cell, next, phase),
            6 => MothLineArt.FaceAt(cell, next, phase),
            7 => LanternLineArt.FaceAt(cell, next, phase),
            8 => SpintopLineArt.FaceAt(cell, next, phase),
            9 => PennantLineArt.FaceAt(cell, next, phase),
            11 => MuffleLineArt.FaceAt(cell, next, phase),
            12 => ChimeLineArt.FaceAt(cell, next, phase),
            13 => GrumbleLineArt.FaceAt(cell, next, phase),
            14 => SmoulderLineArt.FaceAt(cell, next, phase),
            _ => WispLineArt.FaceAt(cell, next, phase),
        };

    public static LineShell.Channels WithAmbient(int shell, LineShell.Channels target, int[] frames, float beat, out bool clipDrives, out float driven) =>
        shell switch
        {
            1 => JellyLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            2 => CrabLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            3 => PufferLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            4 => NautilusLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            5 => SerpentLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            6 => MothLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            7 => LanternLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            8 => SpintopLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            9 => PennantLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            11 => MuffleLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            12 => ChimeLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            13 => GrumbleLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            14 => SmoulderLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
            _ => WispLineArt.WithAmbient(target, frames, beat, out clipDrives, out driven),
        };

    /// <summary>A worn pin: the manifest's rest position moved by the shell's transform for that
    /// pin's kind. Every shell transform is the identity at neutral, so nothing moves from where
    /// it was tuned; it simply travels with the body from there.</summary>
    public static Vector2 Pin(int shell, string name, Vector2 rest, LineShell.Channels ch)
    {
        var kind = LineShell.KindOf(name);
        return shell switch
        {
            1 => JellyLineArt.Pin(name, kind, rest, ch),
            2 => CrabLineArt.Pin(name, kind, rest, ch),
            3 => PufferLineArt.Pin(name, kind, rest, ch),
            4 => NautilusLineArt.Pin(name, kind, rest, ch),
            5 => SerpentLineArt.Pin(name, kind, rest, ch),
            6 => MothLineArt.Pin(name, kind, rest, ch),
            7 => LanternLineArt.Pin(name, kind, rest, ch),
            8 => SpintopLineArt.Pin(name, kind, rest, ch),
            9 => PennantLineArt.Pin(name, kind, rest, ch),
            11 => MuffleLineArt.Pin(name, kind, rest, ch),
            12 => ChimeLineArt.Pin(name, kind, rest, ch),
            13 => GrumbleLineArt.Pin(name, kind, rest, ch),
            14 => SmoulderLineArt.Pin(name, kind, rest, ch),
            _ => WispLineArt.Pin(name, kind, rest, ch),
        };
    }

    public static void Draw(
        int shell, LineCanvas canvas, ImDrawListPtr dl, Vector2 bottomCentre, float displaySize,
        LineShell.Channels body, LineShell.Channels trim, LineShell.EyeState eye, float blush,
        Vector4 bodyCol, Vector4 accentCol, Vector4 eyeCol, Vector4 ink, Vector2 outer, bool flip)
    {
        switch (shell)
        {
            case 1:
                JellyLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 2:
                CrabLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 3:
                PufferLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 4:
                NautilusLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 5:
                SerpentLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 6:
                MothLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 7:
                LanternLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 8:
                SpintopLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 9:
                PennantLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 11:
                MuffleLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 12:
                ChimeLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 13:
                GrumbleLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            case 14:
                SmoulderLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
            default:
                WispLineArt.Draw(canvas, dl, bottomCentre, displaySize, body, trim, eye, blush,
                    bodyCol, accentCol, eyeCol, ink, outer, flip);
                break;
        }
    }
}
