# Frontend → Unity workflow

## Single source of truth

Design metadata lives in `src/widget-schema.json`. React components under `src/components/` render the preview, while the schema describes the part Unity must understand: component type, position, scale, telemetry wildcard, unit, limits and template.

## Export

```powershell
npm.cmd run export:unity
```

The exporter validates wildcard tokens such as `{SPD}` and limit modes (`hard`, `soft`, `none`), then writes Unity-compatible fields to `../GameData/ModularFlightPanel/PluginData/layout.json`.

## Unity mapping

`WidgetLayoutManager` reads the generated JSON. `NavballHUD` maps `WidgetType` to the native Unity widget implementation:

- `tape` → `TapeGaugeWidget`
- `ecam_dial` → `EcamDialGaugeWidget`
- `custom` → `CustomTokenTextWidget`
- `core` → built-in widgets

The game still owns runtime telemetry, drag persistence and input handling; React is the visual design surface and schema authoring surface.

## Deploy

Automated deployment options:

1. **One-click full sync (Recommended)**:
```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
```
This automatically runs React schema export, compiles the C# plugin in Release mode, and synchronizes all GameData assets (`layout.json`, DLL, PDB, AssetBundles, Themes) to the target KSP directory.

2. **Step-by-step export & build**:
```powershell
npm.cmd run export:unity
dotnet build ..\src\ModularFlightPanel\ModularFlightPanel.csproj --configuration Release
```
`export:unity` writes to both repository `PluginData/layout.json` and the target KSP directory if detected. `dotnet build` PostBuild step automatically copies the DLL, PDB, `layout.json` and themes to the KSP installation. Restart KSP to reload the plugin and layout.

