# Ultimate JSON Mod Manager (UJMM)

A specialized JSON and overlay mod manager for **Crimson Desert**.

UJMM imports JSON byte-patch mods, known JSON v3/FIELDS mods, RAW and Browser/UI overlay folders, ZIP/7Z/RAR archives, and ASI/DLL/INI runtime files. It validates selected mods against the live game files, applies byte-patch overlays without overwriting original archive contents, and installs RAW/Browser overlays through packed `0.paz`/`0.pamt` folders registered in `meta\0.papgt`.

## Features

- **Import support** for JSON/FIELDS files, ZIP/7Z/RAR archives, RAW folders, Browser/UI folders, and ASI/DLL/INI runtime files.
- **Folder-friendly overlay import** for parent folders or inner numbered folders such as `0036` and `files\0012`.
- **Loose Browser/UI packing**: loose UI files are packed into `0.paz` + `0.pamt` and registered in `meta\0.papgt`; compiled RAW folders copy/register as-is.
- **JSON v3/FIELDS support** for known Crimson Desert field-intent mods, including single-target and multi-target DMM-style layouts.
- **Refresh Mods** reloads manager-side changes after manually editing JSON or overlay files inside the app's `mods/` folder.
- **Per-mod preset rail** shows preset variants for the focused mod and remembers each preset's selection state.
- **Patch board with search and bulk toggles** previews selected patches and lets you enable/disable visible rows.
- **Check Match** validates selected byte-patch guards against a fresh archive extraction and verifies overlay source files are readable.
- **Apply Mods** appends byte-patch entries at 16-byte aligned offsets, redirects matching `0.pamt` records, and refreshes the Pearl Abyss CRC chain.
- **Backup / Restore** snapshots `meta\0.papgt`, `0008\0.pamt`, and `.paz` lengths so applied byte patches can be reverted.
- **ASI loader management** detects the Ultimate ASI Loader, lists installed `.asi` plugins and `.ini` sidecars, and toggles them per file.
- **Nexus integration** supports SSO, `nxm://` handling, and per-mod update sidecars when Nexus approves the app registration.
- **Crash reporting** saves sanitized diagnostics under `backups\crashes\` and can open a prefilled GitHub issue.
- **Theming** includes Gilded, Ember, Frost, Forest, and a persisted Custom palette.

## Install

1. Download the latest release from [Releases](https://github.com/0xNobodyYT/ultimate-json-mod-manager/releases) or the Crimson Desert page on Nexus Mods.
2. Extract the release ZIP if needed.
3. Run `Ultimate JSON Mod Manager.exe`.
4. Click **Detect** in the Install panel, or **Browse** and choose the Crimson Desert folder that contains `bin64\` and numbered archive folders like `0008\`.

Requires .NET Framework 4.7.2 or later.

### Linux / Wine Notes

- **Detect** checks common Linux Steam locations too: `~/.steam/steam`, `~/.steam/root`, `~/.local/share/Steam`, and the Flatpak Steam path under `~/.var/app/com.valvesoftware.Steam/`.
- UJMM also reads Steam `libraryfolders.vdf`, so extra Steam library drives can be detected.
- If the folder picker hides dot folders such as `.steam`, paste the full path into the game-folder box and click **Save**. Paths like `~/.steam/steam/steamapps/common/Crimson Desert` and `file:///home/...` are accepted.

## Use

### Adding Mods

- Drop or click the import zone for `.json`/FIELDS files, folders, or `.zip/.7z/.rar` archives.
- UJMM accepts RAW/Browser parent folders and inner numbered folders such as `0036` or `files\0012`.
- ASI/DLL/INI files route to `bin64\` after the game folder is set.
- Click **Refresh Mods** after manually editing files inside UJMM's `mods\` folder.

### Applying Mods

1. Tick the mod cards you want active.
2. Pick a preset for any mod that has variants.
3. Review the Patch Board.
4. Click **Check Match** to validate selected mods without changing the game.
5. Click **Apply Mods** to install the selected patches/overlays.
6. Click **Restore Backup** to revert byte-patch changes.

## Build

```cmd
build.cmd
```

Produces `Ultimate JSON Mod Manager.exe`. Requires Roslyn `csc.exe` from Visual Studio Build Tools or a full Visual Studio install. You can set the `CSC` environment variable to a Roslyn-capable compiler path.

Targets .NET Framework 4.x with C# 7.3 language features. Source is in [`src/Program.cs`](src/Program.cs).

## Tech

- WinForms, no XAML or third-party UI framework.
- Built-in Crimson Desert PAZ/PAMT parser.
- Built-in LZ4 block compressor/decompressor.
- Pearl Abyss checksum implementation for PAZ, PAMT, and PAPGT updates.
- Single-instance app with `nxm://` forwarding.
- Sanitized crash diagnostics.

## License

MIT - see [LICENSE](LICENSE).

## Support

If this tool helps you, [buy me a coffee](https://buymeacoffee.com/0xNobody).

## Bug Reports

Click **Report a Bug** in the app, or [open an issue manually](https://github.com/0xNobodyYT/ultimate-json-mod-manager/issues/new).
