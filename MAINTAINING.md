# Maintaining UJMM

UJMM is currently a compact WinForms app with almost all logic in `src/Program.cs`. That keeps releases simple, but it also means new maintainers need a map. This file is that map.

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
| App constants/startup | `Program`, `ManagerForm` constructor | Version, Nexus settings, folder setup, config load, UI bootstrap |
| UI layout | `BuildUi`, `BuildTopBar`, `BuildInstallPanel`, `BuildWorkspacePanel`, `BuildInspectorPanel` | Main WinForms layout |
| Settings | `ShowSettingsDialog`, `BuildPathCard`, `BuildThemeSwatches` | Game folder and theme controls |
| Import | `ImportPaths`, `CollectImportCandidates`, `ExtractArchiveForImport` | Drag/drop, click import, archive extraction, package detection |
| RAW/Browser detection | `IsRawOverlayDirectory`, `IsLooseRawOverlayRoot`, `IsBrowserModDirectory` | Decides whether a folder is an overlay package |
| Mod loading | `JsonMod.Load`, `JsonMod.LoadOverlayDirectory`, `LoadFieldFormat` | Parses JSON v2, JSON v3/FIELDS, RAW, Browser/UI |
| Preview | `FocusMod`, `RefreshPatchList`, `PatchChange` | Builds visible patch rows and preset rail |
| Validation | `RunValidation`, `ValidateChangeAgainstLiveBytes` | Checks original-byte guards against current game archives |
| Apply | `ApplyOverlayStub`, `ApplyBytePatchMods`, `InstallOverlayMods` | Applies byte patches and overlay packages |
| Loose overlay packing | `BuildLooseOverlayPackage`, `BuildLooseOverlayPamt`, `BuildLooseOverlayFileBytes` | Packs loose UI/RAW files into game-readable PAZ/PAMT |
| XML patching | `ApplyXmlMergeDocument`, `ApplyXmlPatchDocument` | Materializes `.merge` and `.patch` files before packing |
| Archive IO | `ArchiveExtractor`, `PamtParser`, `Lz4BlockCompress` | Reads Crimson Desert archives and writes overlay payloads |
| Checksums | `PaChecksum` | Updates PAZ/PAMT/PAPGT checksum fields |
| Nexus | `NexusClient`, `NexusSsoDialog`, `HandleNxmUrl` | SSO, API calls, NXM downloads, update checks |
| Crash reports | `CrashReporter`, `BugReportDialog` | Local crash JSON and prefilled GitHub issue URLs |

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

After a Crimson Desert update, users should verify Steam files or otherwise restore a clean game state, then create a fresh backup before applying mods again.

## Release Checklist

1. Update `AssemblyVersion`, `AssemblyFileVersion`, and `Program.AppVersion`.
2. Run `build.cmd`.
3. Smoke test import, Settings, Apply/Check UI, and Nexus tab layout.
4. Create a clean release ZIP containing only `Ultimate JSON Mod Manager.exe`.
5. Commit source/docs changes.
6. Push `main`.
7. Tag the version, for example `v1.3.5`.
8. Publish a GitHub release and attach the clean ZIP.
9. Upload the same ZIP to the Nexus files page.

## Future Refactor Order

The safest split is by low-coupling code first:

1. Move custom controls (`RoundedPanel`, `Pill`, `GradientButton`, `FlatCheck`, etc.) to `Controls/`.
2. Move Nexus classes to `Nexus/`.
3. Move crash reporting to `Diagnostics/`.
4. Move archive parsing/checksum/LZ4 code to `Archive/`.
5. Move mod parsers (`JsonMod`, `PatchChange`) to `Mods/`.
6. Move apply/restore code to `Install/`.
7. Only then split `ManagerForm` UI builders into partial classes.

Do not start by splitting `ManagerForm`; it touches nearly every state field and is the easiest place to introduce regressions.
