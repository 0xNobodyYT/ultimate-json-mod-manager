# Ultimate JSON Mod Manager (UJMM)

A specialized JSON byte-patch mod manager for **Crimson Desert**.

Imports `.json` patch definitions and `.asi/.dll/.ini` runtime hooks, validates the original-byte guards against the live game files, and applies overlay patches without modifying the original game archives. Companion tool to existing managers — focused on the JSON patch format used by the Crimson Desert modding community.

## Features

- **Drag-and-drop import** for `.json`, `.asi`, `.dll`, `.ini` files. Auto-routes by extension (JSON → `mods/`, runtime → `bin64/`).
- **Per-mod preset rail** — focus a mod on the left, the rail shows its preset variants (`0%`, `5%`, `25%`, …); click one to switch which preset's patches will apply for that mod. Each preset has its own selection state.
- **Multi-select mod cards** with custom checkboxes and instant patch preview.
- **Patch board with search + bulk toggle** — search bar filters every visible patch by name, target file, or mod name; **All On / All Off** buttons bulk-toggle the currently visible patches. Disable individual patches you don't want; selections persist across launches.
- **Patched-value preview** — the patch list shows the bytes each patch will write (`+0x51CDC → 04 00 00 00`), so you can see preset switches actually changing values, not just the same offsets.
- **Check Match** — runs the original-byte guards from each selected mod against your installed `0008/0.pamt` + `.paz` archives. Catches stale offsets, half-patched files, and wrong game versions before you apply anything.
- **Apply Mods** — appends the modded entries to the existing `.paz` archives at 16-byte aligned offsets, redirects the matching `0.pamt` `FileRecord` entries, and refreshes the Pearl Abyss CRC chain (per-paz CRC, PAMT HeaderCrc, papgt PamtCrc, papgt HeaderCrc) so the game launches cleanly with the new bytes. Original archive contents are never overwritten.
- **Backup / Restore** — `Create Backup` snapshots `meta/0.papgt`, `0008/0.pamt`, and the byte lengths of each `.paz`. `Restore Backup` truncates each `.paz` back to its recorded length and restores `pamt` + `papgt` from the backup. All backups live in `<gamePath>\_jmm_backups\`.
- **ASI loader management** — detects the Ultimate ASI Loader in `bin64`, lists installed `.asi` plugins (and matching `.ini` sidecars), enable/disable per file.
- **Nexus integration** (pending Nexus app registration):
  - One-click sign-in via the standard Nexus SSO flow — no API key paste, no password.
  - `nxm://` protocol handler — click *Mod Manager Download* on any Crimson Desert Nexus mod page and the file lands here automatically.
  - Per-mod update detection — sidecar files track installed version; one update check at startup flags new releases with a red "UPDATE" pill on the mod card. Manual refresh via the Nexus tab on demand.
- **Update notifications via Nexus mod page** — UJMM is distributed as a Nexus mod; subscribers get standard Nexus update notifications when a new version ships.
- **Auto crash capture + bug reports** — uncaught exceptions are sanitized (paths, tokens redacted), saved to `backups/crashes/`, and a dialog offers to open a prefilled GitHub issue with the full diagnostic dump.
- **Theming** — Gilded / Ember / Frost / Forest swatches plus a 5th **Custom** slot (color picker, full-palette derivation, persisted across launches).

## Install

1. Download the latest `Ultimate JSON Mod Manager.exe` from [Releases](https://github.com/0xNobodyYT/ultimate-json-mod-manager/releases) — or from the Crimson Desert page on Nexus Mods (recommended; you'll get update notifications).
2. Drop it in any folder you like.
3. Double-click to run. On first launch the app registers itself as the handler for `nxm://` URLs (no admin prompt — uses your user profile).
4. Click **Detect** in the Install panel — it'll find your Crimson Desert install via Steam paths. If it can't, click **Browse** and pick the folder that contains `bin64\` and `0008\`.

Requires .NET Framework 4.7.2 or later (preinstalled on Windows 10 21H2 and later, and on all Windows 11).

## Use

### Adding mods

- Drop a `.json` patch file (or a folder of them) onto the dashed drop zone in the Install panel.
- ASI/DLL/INI files dropped on the same zone get routed to `bin64/` (game folder must be set first).

### Selecting and applying

1. Tick the checkboxes on the mod cards you want active.
2. Pick a preset on the rail (e.g. `5%`, `25%`) for any mod that has presets.
3. The Patch Board shows every patch that will run, the offset it touches, and the bytes it writes.
4. Optionally narrow down: search the patch list, then use **All On** / **All Off** to bulk-toggle visible patches and tick individual ones you want.
5. Click **Check Match** to verify each patch's original bytes match what's currently in your installed game (no changes applied).
6. Click **Apply Mods** to install the overlay.
7. To revert, click **Restore Backup**.

### Right-click any mod card

- Uninstall / Disable
- Open the mod's folder
- Link to a Nexus mod page (paste URL or mod ID — enables update tracking)
- Open on Nexus
- Delete from manager

### Nexus sign-in

Click **Sign in with Nexus** on the Nexus tab. A browser window opens, you click Approve once, and the app remembers you. No API key paste, no password.

> Until Nexus Mods finalizes app registration, the sign-in button shows a friendly "coming soon" message instead of working. Mod browsing still works.

## Build

```cmd
build.cmd
```

Produces `Ultimate JSON Mod Manager.exe`. Requires Roslyn `csc.exe` (Visual Studio Build Tools or full VS install). Set the `CSC` environment variable to point at it if it's not on `PATH`.

Targets .NET Framework 4.x with C# 7.3 language features. Single-file source at [`src/Program.cs`](src/Program.cs).

## Tech

- **WinForms** (no XAML, no third-party UI libraries)
- **Custom-painted controls** — `RoundedPanel`, `GradientButton`, `Pill`, `BadgePill`, `BrandMark`, `ThemeSwatch`, `DotPanel`, `FlatCheck`, `DarkTabControl`, `BufferedScrollPanel`, `BufferedFlowPanel`
- **PAZ archive parser + LZ4 block decompressor** built in — handles the Crimson Desert archive format directly
- **Pearl Abyss checksum** — custom Bob Jenkins lookup3 variant (PA_MAGIC = 558_228_019) used to refresh the per-paz Crc, PAMT HeaderCrc, and papgt PamtCrc / HeaderCrc on every Apply so the engine accepts the modified files
- **TLS 1.2** enforced for all HTTPS calls
- **Single-instance** via named mutex; secondary instances forward `nxm://` URLs to the primary via `WM_COPYDATA`
- **Crash sanitizer** redacts user paths and Nexus key patterns from any data leaving the machine

## License

MIT — see [LICENSE](LICENSE).

## Support

If this tool helps you, [buy me a coffee](https://buymeacoffee.com/0xNobody) ☕

## Bug reports

Click **Report a Bug** in the app — it auto-collects sanitized diagnostics and opens a prefilled issue here.

Or [open an issue manually](https://github.com/0xNobodyYT/ultimate-json-mod-manager/issues/new).
