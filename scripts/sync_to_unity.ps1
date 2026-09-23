$src = "c:/Users/43701/Documents/github/KSP_naviball/src/ModularFlightPanel"
$dst = "c:/Users/43701/Documents/github/KSP_naviball/unity/Assets/ModularFlightPanel"

if (Test-Path $dst) {
    Remove-Item -Recurse -Force $dst
}

New-Item -ItemType Directory -Force -Path "$dst/Core" | Out-Null
New-Item -ItemType Directory -Force -Path "$dst/Config" | Out-Null
New-Item -ItemType Directory -Force -Path "$dst/UI/Widgets" | Out-Null

Copy-Item "$src/Core/IFlightTelemetry.cs" "$dst/Core/"
Copy-Item "$src/Core/FlightTelemetryContext.cs" "$dst/Core/"
Copy-Item "$src/Core/TelemetrySimulationEngine.cs" "$dst/Core/"
Copy-Item "$src/Core/INavBallVisualHook.cs" "$dst/Core/"
Copy-Item "$src/Core/AppPathHelper.cs" "$dst/Core/"
Copy-Item "$src/Core/NavballMarkerFactory.cs" "$dst/Core/"
Copy-Item "$src/Core/TelemetryTokenEngine.cs" "$dst/Core/"
Copy-Item "$src/Core/ExternalProbeRegistry.cs" "$dst/Core/"
Copy-Item "$src/Core/AssetLoader.cs" "$dst/Core/"

Copy-Item "$src/Config/ThemeConfig.cs" "$dst/Config/"
Copy-Item "$src/Config/ThemeManager.cs" "$dst/Config/"
Copy-Item "$src/Config/WidgetConfig.cs" "$dst/Config/"
Copy-Item "$src/Config/LayoutConfig.cs" "$dst/Config/"
Copy-Item "$src/Config/WidgetLayoutManager.cs" "$dst/Config/"
Copy-Item "$src/Config/LayoutShareHub.cs" "$dst/Config/"

Copy-Item "$src/UI/UIFactory.cs" "$dst/UI/"
Copy-Item "$src/UI/BaseFlightWidget.cs" "$dst/UI/"
Copy-Item "$src/UI/WidgetDragHandler.cs" "$dst/UI/"
Copy-Item "$src/UI/NavballHUD.cs" "$dst/UI/"

Get-ChildItem "$src/UI/Widgets" -Filter *.cs | ForEach-Object {
    Copy-Item $_.FullName "$dst/UI/Widgets/"
}

$count = (Get-ChildItem -Recurse $dst -Filter *.cs).Count
Write-Host "Sync successful! Total decoupled C# scripts in Unity: $count"
