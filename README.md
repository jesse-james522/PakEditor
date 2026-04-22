# UEpaker

A unified Unreal Engine 5.6 mod tool for Windows. Browse game assets like FModel, edit them with a fully-typed property tree like UAssetGUI, and pack a mod — all without touching a command line.

## The problem it solves

The current workflow is four separate tools: FModel to explore, retoc to unpack, UAssetGUI to edit, retoc again to repack. UEpaker collapses that into one app. From the user's perspective: browse, stage, edit, click **Build Mod**.

## Requirements

- Windows 10/11
- `oo2core_9_win64.dll` from your game's install directory — drop it next to the executable (Oodle cannot be redistributed)
- A `.usmap` mappings file for your target game (same as FModel)
- The game's AES key if paks are encrypted

## Features

- **Asset browser** — loads `.pak` / `.utoc` / `.ucas` IoStore containers via CUE4Parse. AES key entry, `.usmap` mappings loader, UE version selector.
- **Typed property editor** — per-export tree with Name Map, Import Map, and Export Map tabs. Typed editors for every property kind (Int, Float, Bool, Name, Enum, Struct, Array, Object, …). Modeled on UAssetGUI's layout.
- **Explicit staging** — right-click any asset → **Stage for editing**. The asset is exported from the Zen container to a local working folder as legacy `.uasset/.uexp`. Nothing hits the editor until you open it.
- **Build Mod** — dirty assets are packed into a `.utoc/.ucas/.pak` mod trio via the bundled `retoc` subprocess. retoc's output is shown in the app log panel.

## Architecture

| Module | Role | Library |
|---|---|---|
| `Viewer` | Asset browser, provider init | CUE4Parse |
| `AssetBridge` | Zen → legacy staging | CUE4Parse + UAssetAPI |
| `Editor` | Typed property tree, dirty tracking | UAssetAPI |
| `Packer` | retoc subprocess, mod output | — |

**Stack:** C# / .NET 8, WPF (Windows-only by design).

**retoc** is bundled as a pre-built binary under `tools/retoc/` (MIT license, Rust — not ported). It is invoked as a subprocess for the final pack step only; no pure-C# IoStore writer exists that is reliable enough to replace it.

## Building

```
git clone --recurse-submodules <repo-url>
```

Open `UEpaker.slnx` in Visual Studio 2022 or Rider and build. The first build will attempt to compile `CUE4Parse-Natives` via cmake — if cmake is not on your PATH the native acceleration layer is skipped gracefully and a warning is printed.

## Third-party libraries

| Library | License | Role |
|---|---|---|
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) | Apache-2.0 | Asset reading, Zen → legacy export |
| [UAssetAPI](https://github.com/atenfyr/UAssetAPI) | MIT | Asset editing |
| [retoc](https://github.com/trumank/retoc) | MIT | IoStore mod packaging |

Full license texts are in `licenses/`.

## What is not included

- `oo2core_9_win64.dll` — Oodle decompression library. Must be sourced from your game install. See [Oodle.NET](https://github.com/NotOfficer/Oodle.NET) for details.
- `retoc.exe` is included as a binary but is not tracked in git. Download the latest release from [trumank/retoc](https://github.com/trumank/retoc/releases) and place it at `tools/retoc/retoc.exe`.
