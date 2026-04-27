# PakEditor

A fork of [FModel](https://github.com/4sval/FModel) extended with an integrated asset editor and mod cooking pipeline for Unreal Engine games using IoStore (UE5) pak formats.

---

## What is this?

PakEditor lets you browse game archives with FModel's standard explorer, then directly edit `.uasset` files and cook them back into a loadable mod pak — all without leaving the application.

It bundles:
- **FModel** — Unreal Engine archive explorer (CUE4Parse-based)
- **Asset Editor** — hierarchical property tree powered by UAssetAPI, with live editing and write-back
- **UAssetGUI integration** — open any asset in UAssetGUI with one click; engine version and mappings set automatically via CLI args
- **retoc** — IoStore conversion (Legacy ↔ Zen), used for both staging assets and cooking mods
- **Cooking tab** — select edited assets, pack them to `.utoc`/`.ucas` with retoc

---

## Prerequisites

| Tool | Required | Notes |
|------|----------|-------|
| [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) | Debug builds only | Release build is self-contained |
| [UAssetGUI v1.0.4+](https://github.com/atenfyr/UAssetGUI/releases) | Optional | Required for "Open in UAssetGUI" button; must be v1.0.4+ for portable mode |
| retoc | Bundled | Included in `Retoc\retoc.exe` |

---

## Folder layout (after build)

```
PakEditor.exe              Main application (self-contained in Release)
UAssetGUI\
  UAssetGUI.exe            Asset editor GUI (v1.0.4+ required)
  Data\                    Created on first use — portable config & mappings
    config.json            Engine version preference
    Mappings\              Game mapping files (.usmap) copied here automatically
  LICENSE / NOTICE.md / README.md
Retoc\
  retoc.exe                IoStore converter (to-legacy / to-zen)
  LICENSE / README.md
EditedAssets\              Created on first use — your edited .uasset files live here
Cooked\                    Default output for cooked mod paks
licenses\                  All third-party license texts
README.md                  This file
LICENSE                    PakEditor-FM license (GPL-3)
NOTICE                     Third-party notices
```

---

## Building

Requirements: .NET 9 SDK, Visual Studio 2022 (or Rider), Git.

```powershell
# Clone with submodules
git clone --recurse-submodules https://github.com/YourUser/PakEditor-FM.git
cd PakEditor-FM

# Place UAssetGUI.exe (v1.0.4+) in the UAssetGUI\ folder
# Download from: https://github.com/atenfyr/UAssetGUI/releases

# Release build (self-contained, no runtime needed)
.\build-release.bat

# Debug build (requires .NET 9 runtime on target machine)
.\build-debug.bat

# Or use the PowerShell script directly
.\build.ps1 -Configuration Release
.\build.ps1 -Configuration Debug -NoPause
```

Output lands in `publish\release\` or `publish\debug\`.

---

## Setup

1. Run `PakEditor.exe`.
2. In **Settings → Game Directory**, point FModel at your game's `Content/Paks` folder.
3. Load your AES key if the game is encrypted.
4. Set the engine version to match your game (e.g. UE5.4).
5. Click **Load** — the archive tree will populate as normal FModel.

---

## Editing an asset

1. In the archive browser, right-click any `.uasset` → **Edit Asset**.
2. The Asset Editor opens. PakEditor:
   - Extracts the asset from the IoStore container via retoc to a temp staging folder.
   - Loads it with UAssetAPI and shows a hierarchical property tree.
3. Expand exports to view and edit property values inline.
4. Click **Save Changes** to write edits back to the staged file.

> Staged files live in `%TEMP%\PakEditor\Staged\`. They are cached — re-opening the same asset skips re-extraction.

---

## Opening in UAssetGUI

1. From the Asset Editor, click **Open in UAssetGUI**.
2. PakEditor will:
   - Copy the asset to `EditedAssets\<game-path>\` (never overwrites an existing file).
   - Copy your game's `.usmap` mappings file into `UAssetGUI\Data\Mappings\`.
   - Write the engine version to `UAssetGUI\Data\config.json` (portable mode).
   - Launch UAssetGUI with the file, engine version and mappings name as CLI arguments.
3. Edit freely in UAssetGUI. Your changes are saved directly to `EditedAssets\`.

> **Portable mode**: UAssetGUI stores all config in `UAssetGUI\Data\` rather than `%LocalAppData%`. This keeps everything self-contained next to the exe.

> If mappings don't appear in UAssetGUI's drop-down on first launch, close and reopen it once — this is a known UAssetGUI startup behaviour.

---

## Cooking a mod

The **Cooking** tab lets you pack any subset of your edited assets into an IoStore mod pak (`.utoc` + `.ucas`).

1. Click **↺ Refresh** — all `.uasset` files found in `EditedAssets\` appear in the list with their last-modified timestamp.
2. All assets are checked by default. Uncheck any you don't want included.
3. Set **Package Name** — add a `_P` suffix (e.g. `MyMod_P`) so the game's pak prioritisation loads it as a mod override.
4. Set **Output Folder** (default: `Cooked\` next to the exe).
5. Optionally tick **Delete edited assets after successful cook** to clean up `EditedAssets\` automatically.
6. Click **Cook Mod**. retoc converts the selected assets to IoStore format.

Output files:
```
Cooked\
  MyMod_P.utoc
  MyMod_P.ucas
```

Drop both files into your game's `Content/Paks/~mods/` folder (or equivalent).

### Toolbar buttons

| Button | Action |
|--------|--------|
| ↺ Refresh | Re-scan `EditedAssets\` |
| ✓ All | Check all assets |
| ✗ None | Uncheck all assets |
| 🗑 Delete | Permanently delete checked assets from `EditedAssets\` on disk |

---

## Frequently asked questions

**Q: The Cook button is greyed out.**  
A: At least one asset must be checked. Click **✓ All** or check individual boxes.

**Q: retoc failed / exit code non-zero.**  
A: Make sure `Retoc\retoc.exe` is present. Check that the engine version in FModel Settings matches your game. The full retoc error is shown in the status bar.

**Q: Asset Editor shows "retoc failed" during staging.**  
A: Verify your AES key is loaded and your Game Directory points to the correct Paks folder.

**Q: UAssetGUI shows "failed to maintain binary equality".**  
A: This is a UAssetAPI limitation for that specific asset type. The asset may still be editable — check the [UAssetAPI issues](https://github.com/atenfyr/UAssetAPI/issues) page.

**Q: Where are staged files stored?**  
A: `%TEMP%\PakEditor\Staged\` — delete this folder to force a fresh extract on next open.

**Q: Where are edited files stored?**  
A: `EditedAssets\` next to the exe, mirroring the game's virtual path (e.g. `EditedAssets\Game\Content\...`).

---

## Third-party components

| Component | License | Author |
|-----------|---------|--------|
| [FModel](https://github.com/4sval/FModel) | GPL-3.0 | 4sval & contributors |
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) | Apache-2.0 | FabianFG & contributors |
| [UAssetAPI](https://github.com/atenfyr/UAssetAPI) | MIT | atenfyr |
| [UAssetGUI](https://github.com/atenfyr/UAssetGUI) | MIT | atenfyr |
| [retoc](https://github.com/trumank/retoc) | MIT | trumank & Archengius |
| AdonisUI | MIT | benruehl |
| AvalonEdit | MIT | AvalonEdit contributors |
| Various (see `licenses\`) | Various | — |

Full license texts are in the `licenses\` folder of the distribution and in the `NOTICE` file.

---

## License

PakEditor-FM (the FModel fork and all PakEditor additions) is licensed under the **GNU General Public License v3.0**.  
See [LICENSE](LICENSE) for the full text.

The bundled third-party tools (UAssetGUI, retoc) retain their own licenses — see `UAssetGUI\LICENSE` and `Retoc\LICENSE`.
