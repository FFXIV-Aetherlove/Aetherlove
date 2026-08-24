using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling;

/// <summary>The pet app's answer to <see cref="IPetRenderer"/>: the party huddle's recipe (one runtime per
/// creature, dressed through the draft-look path, nook suppressed) behind a surface any app can hold.
/// The own creature is the app's single runtime, so it wears exactly what the floating pet wears.</summary>
internal sealed class PetRendererService(IAetherlingHost host, PetRuntime own) : IPetRenderer
{
    private sealed class Companion
    {
        public readonly PetRuntime Runtime = new();
        public string Folder = string.Empty;
        public string Look = string.Empty;
    }

    private readonly Dictionary<Guid, Companion> _companions = [];
    private readonly List<Guid> _stale = [];

    public void Draw(ImDrawListPtr dl, Guid key, Vector2 bottomCentre, float size,
        short stage, string palette, IReadOnlyList<string> accessories, bool reduceMotion)
    {
        if (!_companions.TryGetValue(key, out var companion))
        {
            companion = new Companion();
            companion.Runtime.SuppressNook = true;
            _companions[key] = companion;
        }
        var folder = PetState.FormFolderForStage(stage);
        if (companion.Folder != folder)
        {
            companion.Folder = folder;
            companion.Runtime.EnsureLoaded(host.AssetRoot, folder);
        }
        var look = $"{palette}|{string.Join(',', accessories)}";
        if (companion.Look != look)
        {
            companion.Look = look;
            companion.Runtime.ApplyDraftLook(palette, [.. accessories], string.Empty, []);
        }
        if (!companion.Runtime.Ready)
        {
            return;
        }
        companion.Runtime.Tick(reduceMotion);
        companion.Runtime.Draw(dl, host.Textures, bottomCentre, size, companion.Runtime.Pose);
    }

    public bool DrawOwn(ImDrawListPtr dl, Vector2 bottomCentre, float size, bool reduceMotion)
    {
        if (host.Snapshot is not { HatchedAtUtc: not null } core)
        {
            return false;
        }
        own.EnsureLoaded(host.AssetRoot, PetState.FormFolder(core));
        own.ApplyLook(core);
        if (!own.Ready)
        {
            return false;
        }
        own.Tick(reduceMotion);
        own.Draw(dl, host.Textures, bottomCentre, size, own.Pose);
        return true;
    }

    public void Forget(IReadOnlyCollection<Guid> keep)
    {
        _stale.Clear();
        foreach (var key in _companions.Keys)
        {
            if (!keep.Contains(key))
            {
                _stale.Add(key);
            }
        }
        foreach (var key in _stale)
        {
            _companions.Remove(key);
        }
    }
}
