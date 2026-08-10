# Vendored: Managed Doom

Everything under `src/` is third-party code, copied unmodified except where noted below. Do not
reformat it to the house style and do not apply the repo's comment rules to it: keeping it close to
upstream is what makes a future re-sync readable.

| | |
|---|---|
| Upstream | https://github.com/sinshu/managed-doom |
| Author | Nobuaki Tanaka (sinshu) |
| Pinned commit | `9365696eb44326a3aab72c4bab217f7db8a87c96` (2025-11-24) |
| Licence | GPL-2.0-or-later, see `LICENSE_ManagedDoom.txt` |
| Underlying | Doom, Copyright (C) 1993-1996 id Software, Inc. |

## Licence compatibility

Every source header reads *"either version 2 of the License, or (at your option) any later
version"*, so this is GPLv2-**or-later**, not GPLv2-only. That upgrades to GPLv3 and is therefore
compatible with this repository's AGPL-3.0-or-later licence. The upstream README says "GPLv2"
loosely; the source headers govern.

## What was copied

`src/Doom`, `src/Video`, `src/Audio`, `src/UserInput`, plus the four root files
(`ApplicationInfo.cs`, `CommandLineArgs.cs`, `Config.cs`, `ConfigUtilities.cs`).

## What was dropped, and why

`src/Silk` (8 files) was the desktop frontend: it opened a GLFW window, uploaded frames through
TrippyGL and played audio through OpenAL. Dropping it removes every native dependency
(Silk.NET, GLFW, OpenAL Soft, TrippyGL, DrippyAL) so this project has none at all.

`Video/`, `Audio/` and `UserInput/` hold the backend *interfaces* (`IVideo`, `ISound`, `IMusic`,
`IUserInput`) and the platform-agnostic software renderer, which is why they are core rather than
frontend. AetherOS supplies its own implementations of those four interfaces in
`AetherOS.Apps.Doom/Backend/`, drawing to an ImGui texture and playing through NAudio.

## Re-syncing

```
git clone https://github.com/sinshu/managed-doom
```

Copy the four folders and four root files over `src/`, drop `src/Silk`, then re-apply anything
listed under "Local modifications" below and update the pinned commit in this file.

## Local modifications

- **`src/ConfigUtilities.cs`** — added `DataDirectoryOverride`, consulted by `GetExeDirectory()`.
  Upstream derives its config and savegame paths from `Process.MainModule`, which inside FFXIV is the
  game's own folder, so Doom would have written `managed-doom.cfg` and its saves next to
  `ffxiv_dx11.exe`. The host points this at the app's `IAppStorage` directory. Three call sites depend
  on it: `DoomGame` save/load and `SaveSlots`.

## Derived work living outside this project

`AetherOS.Apps.Doom/Backend/` carries three files derived from the dropped `src/Silk` frontend, each
with the upstream GPL header and a note saying so:

| Ours | Derived from | What changed |
|---|---|---|
| `NAudioDoomSound.cs` | `SilkSound.cs` | DMX lump decoding, channel allocation and priority rules are upstream's. OpenAL's 3D positioning is collapsed to a stereo pan; playback is NAudio. |
| `MeltySynthDoomMusic.cs` | `SilkMusic.cs` | The MUS and MIDI decoders are upstream's verbatim and are platform-agnostic. The OpenAL stream became a mixer input. |
| `PhoneUserInput.cs` | `SilkUserInput.cs` | Tic-command assembly is upstream's. Reads the phone's keyboard capability; all mouse paths removed. |

`ImGuiDoomVideo.cs` is ours, but note it reuses upstream's `Renderer` untouched and reproduces the
frontend's transposed texture coordinates (the software renderer fills its buffer column-major).
