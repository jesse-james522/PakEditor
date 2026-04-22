# UEpaker — EditorModel

A unified Unreal Engine 5 mod tool: browse game assets (like FModel), edit them (UAssetGUI-style property tree), and repack into an IoStore mod — all in one app.

## Stack
- **CUE4Parse** — reads `.pak` / `.utoc` / `.ucas` (UE5.6 Zen)
- **UAssetAPI** — edits legacy cooked `.uasset` / `.uexp`
- **retoc** (bundled, `tools/retoc/`) — packs the staged asset folder into an IoStore mod

## Workflow
1. Load game paks (AES key + `.usmap` mappings + UE version)
2. Browse the asset tree, right-click → **Stage for editing**
3. Edit properties in the typed property tree
4. **Build Mod** — invokes retoc on the staged folder, produces the mod output

## License
See `licenses/` for third-party library licenses.
