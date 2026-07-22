# AetherLove client source

The public source mirror of the AetherLove Dalamud plugin: the plugin itself, its client
libraries (AetherLove.Shared, AetherLove.AppKit, AetherLove.Core), the AetherOS platform
(AetherOS.Sdk, AetherOS.Shell) and every AetherOS app. Published as one squashed snapshot
per release from the private development monorepo. The server is a separate network
service; it is not part of the plugin binary and is not included here.

## Architecture

AetherLove is no longer a single plugin window. It ships an entire phone operating
system, "AetherOS", inside one Dalamud plugin: a home screen hosting many apps, of which
the dating app is just one. The plugin project itself is a thin host that boots the OS
shell, wires up Dalamud services, and registers the apps.

- AetherOS.Sdk is the platform contract: the IAetherApp interface every app implements,
  the IOsShell surface (app registry, notifications, badges, intent delivery),
  OsAppContext (the per-frame draw context carrying theme, scale and localization), and
  IAppCapabilities (camera, image picking, textures, share sheet, per-app storage).
- AetherOS.Shell is the OS runtime: the paged home screen with dock, folders and
  widgets, the status bar, the notification shade, the share sheet, wallpapers, and the
  app open/close transitions.
- Each app is its own project (AetherOS.Apps.*). Apps never reference each other or the
  plugin. Simple apps (Clock, Weather, Photos, Camera, Calendar) reference only the Sdk;
  richer ones add the shared client libraries.
- AetherLove.AppKit is the shared UI kit (theming, localization, shared widgets),
  AetherLove.Core is the client service layer (server connection, auth and session, E2E
  crypto, chat caches), and AetherLove.Shared holds the wire DTOs.

Apps talk to each other Android-style instead of calling each other directly: a sender
fires an intent (a type string plus a small JSON payload; see AetherOS.Sdk/OsIntents.cs)
at the shell, which delivers it to the target app and brings it to the foreground.
Content sharing runs through a generic share sheet: a source offers a typed ShareItem
and any app that declares that type can be picked as the target. When an app needs
something only the host plugin can provide (game data, server calls), it declares a
small host interface in its own project and the plugin supplies the implementation via
dependency injection. The seam between shell and apps is deliberately
serialization-shaped (string ids, JSON payloads, no app-to-app project references) so an
app could later be promoted to a standalone plugin over real IPC without a redesign.

## Building

Requirements: the .NET 10 SDK and a local Dalamud dev installation (set up by XIVLauncher;
the Dalamud.NET.Sdk build resolves it automatically).

    git clone --recursive <repo-url>
    dotnet build AetherLove.Plugin/AetherLovePlugin.csproj -c Release -p:Platform=x64

The compiled plugin lands in AetherLove.Plugin/bin/x64/Release/.

## License

AGPL-3.0-or-later. See the LICENSE file.
