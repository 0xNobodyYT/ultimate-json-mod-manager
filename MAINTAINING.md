# Maintaining UJMM

UJMM is a compact WinForms app, but the old single-file implementation has been split into focused source files under `src/`. The app still uses one `ManagerForm` type for shared UI state; the form is now organized with partial classes so maintainers can work on one area without scrolling through the whole program.

## High-Level Flow

1. Startup creates the app folders, loads `config.json`, detects the Crimson Desert folder, builds the UI, loads mods, and refreshes ASI state.
2. Import copies supported mods into `mods/`.
3. `LoadMods()` converts files/folders in `mods/` into `JsonMod` objects.
4. The install list controls which mods are active and which preset/group is selected.
5. The Patch Board previews byte patches and overlay files.
6. `Check Match` validates selected byte-patch guards and overlay readability without changing game files.
7. `Apply` either writes byte-patch overlays into the game archives or installs RAW/Browser overlay folders by creating/registering `0.paz` and `0.pamt`.
8. `Restore` uses `_jmm_backups` to restore `meta\0.papgt`, `0008\0.pamt`, and recorded PAZ lengths.

## Source Map

All line numbers drift, so search by method/class name.

| Area | Where to Look | What It Does |
| --- | --- | --- |
| Area | Where to Look | What It Does |
| --- | --- | --- |
| App constants/startup | `Program.cs` | Version, Nexus constants, app startup, shared form fields, constructor |
| Configuration | `ManagerForm.Configuration.cs` | `config.json`, saved theme, string/list/dictionary config helpers |
| UI layout | `ManagerForm.Layout.cs` | Top bar, install panel, patch board, ASI tab, inspector, theming |
| Mod library and cards | `ManagerForm.ModLibrary.cs` | `LoadMods`, ordering, preset rail, patch-board rows, card selection/deletion |
| Import | `ManagerForm.Import.cs` | Drag/drop, click import, archive extraction, RAW/Browser package detection, ASI file management |
| Nexus workflow | `ManagerForm.Nexus.cs`, `NexusIntegration.cs` | SSO, API calls, NXM downloads, feed cards, app/mod update checks |
| Validation and JSON v3 fields | `ManagerForm.Validation.cs` | Check Match, live-byte validation, FIELDS resolution, raw field-slice matching |
| Loose overlay packing | `ManagerForm.OverlayPackaging.cs` | Packs loose RAW/Browser files into generated `0.paz`/`0.pamt`, XML merge/patch handling |
| Apply/restore patching | `ManagerForm.PazPatch.cs` | Byte-patch apply, PAZ append, loose-file fallback, restore of appended data |
| Backup/game detection | `ManagerForm.BackupAndDetection.cs` | Backup roots, restore guard, Steam/Linux game-folder detection |
| Models and parsers | `ModModels.cs` | `JsonMod`, `PatchChange`, metadata parsing, JSON v2/v3/RAW/Browser loading |
| Archive IO | `ArchiveExtractor.cs` | PAMT parsing, PAZ extraction, LZ4, Pearl Abyss checksum |
| Custom controls | `UiControls.cs`, `Theme.cs` | Painted panels, pills, buttons, checks, tabs, theme palettes |
| Crash reports | `CrashReporting.cs` | Local crash JSON and prefilled GitHub issue URLs |

## Supported Import Shapes

| Shape | Example | Result |
| --- | --- | --- |
| JSON v2 byte patch | `Resource Costs.json` | Loaded as patch changes |
| JSON v3/FIELDS | `format: 3` with `target` or `targets` | Loaded as field-intent preview rows |
| Archive envelope | `.zip`, `.7z`, `.rar` | Extracted to a short temp folder, then recursively classified |
| Compiled RAW overlay | `0036\0.pamt` + `0.paz` | Copied into the next free game overlay slot |
| Loose RAW root | `mod.json` + `character/...` or `ui/...` | Packed into `0.paz`/`0.pamt`, then registered |
| Browser/UI package | `manifest.json` + `files\0012\...` | Packed/registered like an overlay |
| Runtime hooks | `.asi`, `.dll`, `.ini` | Copied to `bin64` and tracked under `mods\_asi` |

ZIP files use .NET's built-in ZIP reader. `.7z` and `.rar` require a local 7-Zip executable, which UJMM searches for in standard install locations.

## Game File Safety Model

UJMM avoids overwriting original game data.

- Byte patches append new bytes to PAZ files, then point PAMT records at those new bytes.
- Loose overlay folders are packed into new overlay slots instead of changing stock archives.
- `meta\0.papgt` is updated so the game knows about installed overlay slots.
- Backups record the pre-apply `0.papgt`, `0008\0.pamt`, and PAZ byte lengths.
- `restore_guard.json` records the exact post-apply state UJMM expects to restore from. If Steam/Microsoft Store changes the game files after mods were applied, restore is blocked instead of writing stale backups over updated files.

After a Crimson Desert update, users should verify Steam files or otherwise restore a clean game state, then create a fresh backup before applying mods again.

## Release Checklist

1. Update `AssemblyVersion`, `AssemblyFileVersion`, and `Program.AppVersion`.
2. Run `build.cmd`.
3. Smoke test import, Settings, Apply/Check UI, and Nexus tab layout.
4. Create a clean release ZIP containing only `Ultimate JSON Mod Manager.exe`.
5. Commit source/docs changes.
6. Push `main`.
7. Tag the version, for example `vX.Y.Z`.
8. Publish a GitHub release and attach the clean ZIP.
9. Upload the same ZIP to the Nexus files page.

## Future Refactor Order

The first split is done. The safest next steps are:

1. Move `ArchiveExtractor.cs` into an `Archive/` folder and split parser/checksum/compression types.
2. Move `ModModels.cs` into a `Mods/` folder and split JSON byte patches, v3/FIELDS parsing, and RAW/Browser metadata.
3. Move `NexusIntegration.cs` into a `Nexus/` folder and separate API client, dialogs, and protocol handling.
4. Gradually replace shared `ManagerForm` state with small service classes only after behavior is covered by smoke tests.
5. Keep each follow-up refactor mechanical and build after every slice.
