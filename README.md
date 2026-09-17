# Modern UI Side Panel — Jellyfin Plugin (v12 ready)

Keeps the **modern Jellyfin UI styling** (glassmorphism, accent colors, rounded pills, CSS variables)
but restores a **persistent legacy-style left side panel** and **hides the top bar header** on desktop.

Works on Jellyfin **12.x (net10)**.
Supports the **modern v12 MUI shell**
(`MuiDrawer` / `MuiAppBar` / `ResponsiveDrawer`). Mobile keeps stock behaviour
(top bar restored, drawer becomes an overlay).

## What it does

- Hides `.skinHeader` / `.headerTop` / `.sectionTabs` on desktop (`HideTopBar`)
- Forces `.mainDrawer` to be a fixed sidebar (`width = SidebarWidth`, blur, modern pill links)
- Shifts `.mainAnimatedPages` / `.skinBody` right so content never slides under the panel
- Moves Search / Cast / Sync / User buttons into a `.msp-actions` footer inside the drawer
  so they stay reachable when the header is gone (`RelocateHeaderButtons`)
- If `.mainDrawer` no longer exists (future v12 layout), builds a fallback
  `<nav id="msp-sidepanel">` from scratch with Home / Favorites / Movies / TV / Music / Live TV / Search / Settings / Dashboard
- SPA-aware: `MutationObserver` + `hashchange` + `resize` + `fullscreenchange`,
  active-link highlighting, fullscreen video hides the panel
- Configurable from Dashboard → Plugins → Modern UI Side Panel

## How it injects

No fork of `jellyfin-web` needed. Two-tier strategy (in-memory preferred, disk fallback):

1. **Preferred — FileTransformation (non-destructive, multi-plugin safe).**
   If the companion plugin `Jellyfin.Plugin.FileTransformation` is installed,
   `Plugin.TryRegisterFileTransformation()` registers `TransformCallback.Transform`
   for `index\.html` via reflection (no hard reference). Every `index.html` request
   gets the `<link>` + `<script>` block injected in memory. Install FileTransformation
   from its repo catalog for the cleanest setup on 10.11/12.
2. **Fallback — index.html disk patch (always available).**
   `Plugin.cs` detects the web root (`IApplicationPaths.WebPath` + docker fallbacks),
   `IndexPatcher.cs` patches `web/index.html` (idempotent `<!-- ModernSidePanel BEGIN/END -->`
   block, `.modernsidepanel.bak` backup, stale `.gz/.br` cleanup).
3. `Api/SidePanelController.cs` serves `sidepanel.js` / `sidepanel.css` (embedded resources)
   and dynamic `config.js` (`window.ModernSidePanelConfig` from `PluginConfiguration.cs`).
4. Disabling the plugin removes the disk block (restores from backup when available).
   The in-memory transform is a no-op when disabled (client early-returns).

## Build

Requires .NET 10 SDK — verified with 10.0.401

```powershell
dotnet build -c Release
# DLL:
#  bin/Release/net10.0/Jellyfin.Plugin.ModernSidePanel.dll  -> Jellyfin 12.x  (targetAbi 12.0.0.0)
```

> Jellyfin 12 note: remove third-party plugins before upgrading (per 12.0 release notes),
> then reinstall the net10.0 build. Dashboard Custom CSS was removed in 10.11+ and
> Custom Menu Links are absent in Modern mode — this plugin does not depend on either.

## Install (manual)

1. Copy `Jellyfin.Plugin.ModernSidePanel.dll` to your Jellyfin `plugins/ModernSidePanel/` folder:
   - Linux docker: `/config/plugins/ModernSidePanel/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\ModernSidePanel\`
2. Restart Jellyfin Server — check the log for `[ModernSidePanel] Patch result: True`
3. Hard-refresh the web client (`Ctrl+Shift+R`). The top bar should be gone on desktop,
   replaced by a persistent left panel.
4. Tune under Dashboard → Plugins → Modern UI Side Panel.

## Uninstall

1. Uncheck *Enable plugin* → Save (removes the `index.html` block), **or** just remove the plugin.
2. If `index.html` ever looks stale, restore manually:
   ```bash
   cp /usr/share/jellyfin/web/index.html.modernsidepanel.bak /usr/share/jellyfin/web/index.html
   ```
3. Restart Jellyfin, hard-refresh the browser.

## Files

```
Plugin.cs                      BasePlugin + web-root detection + patch triggers
PluginConfiguration.cs         Dashboard options (also exposed as window.ModernSidePanelConfig)
IndexPatcher.cs                Idempotent index.html BEGIN/END patching + backup
Api/SidePanelController.cs     Serves sidepanel.js / sidepanel.css / config.js
Configuration/configPage.html  Dashboard settings page
Web/sidepanel.js               SPA-aware sidebar restore + top-bar removal + fallback nav
Web/sidepanel.css              Modern glass sidebar styling + responsive + fullscreen guards
```

## Customizing

- Width / blur / breakpoint: Dashboard settings (live via `config.js`, no rebuild).
- Colors: the CSS uses `var(--theme-primary-color, #00a4dc)` so it follows your
  Jellyfin theme automatically. Override `--msp-accent` / `--msp-drawer-bg` in
  *Dashboard → General → Custom CSS* to re-skin without forking.
- Force overlay mode on a kiosk: set `PersistentSidebar = false`.

## License

MIT
