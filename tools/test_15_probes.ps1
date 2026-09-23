# test_15_probes.ps1 - Comprehensive 15-Mod Reflection Verification Suite
$ErrorActionPreference = "Stop"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  ModularFlightPanel - 15 External Mod Probes Verification Suite " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$kspRoot = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
$kspManaged = "$kspRoot\KSP_x64_Data\Managed"
$gameData = "$kspRoot\GameData"
$gameDataNew = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData"

# 1. Preload Unity & KSP Core Assemblies
$coreAssemblies = @(
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.UI.dll",
    "UnityEngine.UIModule.dll",
    "Assembly-CSharp.dll"
)

foreach ($dll in $coreAssemblies) {
    $p = Join-Path $kspManaged $dll
    if (Test-Path $p) {
        try { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null } catch {}
    }
}

# 2. Preload Dependencies
$deps = @(
    "$gameData\000_KSPBurst\Plugins\Unity.Mathematics.dll",
    "$gameData\SolverEngines\Plugins\SolverEngines.dll",
    "$gameData\ROUtils\Plugins\ROUtils.dll",
    "$gameData\TestFlight\Plugins\TestFlightAPI.dll",
    "$gameData\Kerbalism\KerbalismBootstrap.dll"
)
foreach ($p in $deps) {
    if (Test-Path $p) { try { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null } catch {} }
}

# 3. Preload 15 Mod Assemblies with Exact Paths
$modPaths = @(
    "$gameData\FerramAerospaceResearch\Plugins\FerramAerospaceResearch.dll",
    "$gameData\KerbalEngineer\KerbalEngineer.dll",
    "$gameData\MechJeb2\Plugins\MechJeb2.dll",
    "$gameData\Principia\ksp_plugin_adapter.dll",
    "$gameData\RealAntennas\Plugins\RealAntennas.dll",
    "$gameDataNew\Kerbalism\Kerbalism.dll",
    "$gameData\Trajectories\Plugins\Trajectories.dll",
    "$gameData\NavyFish\Plugins\Docking Port Alignment Indicator\DockingPortAlignmentIndicator.dll",
    "$gameData\GPWS\Plugins\GPWS.dll",
    "$gameData\RealFuels\Plugins\RealFuels.dll",
    "$gameData\TestFlight\Plugins\TestFlightCore.dll",
    "$gameData\DynamicBatteryStorage\Plugins\DynamicBatteryStorage.dll",
    "$gameData\SystemHeat\Plugin\SystemHeat.dll",
    "$gameData\AtmosphereAutopilot\AtmosphereAutopilot.dll",
    "$gameData\RP-1\Plugins\RP0.dll"
)

Write-Host "`n[1/3] Loading 15 Mod Assemblies..." -ForegroundColor Yellow
$loadedMods = 0
foreach ($p in $modPaths) {
    if (Test-Path $p) {
        $filename = [System.IO.Path]::GetFileNameWithoutExtension($p)
        try {
            [System.Reflection.Assembly]::LoadFrom($p) | Out-Null
            Write-Host "  Loaded: $filename" -ForegroundColor Green
            $loadedMods++
        } catch {
            Write-Host "  Error loading $filename : $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        $filename = [System.IO.Path]::GetFileNameWithoutExtension($p)
        Write-Host "  Skipped: $filename (not found at $p)" -ForegroundColor DarkGray
    }
}

# 4. Load ModularFlightPanel.dll
Write-Host "`n[2/3] Loading ModularFlightPanel.dll..." -ForegroundColor Yellow
$mfpDll = "C:\Users\43701\Documents\github\KSP_naviball\src\ModularFlightPanel\bin\Release\ModularFlightPanel.dll"
$mfpAsm = [System.Reflection.Assembly]::LoadFrom($mfpDll)
Write-Host "  ModularFlightPanel loaded from $mfpDll" -ForegroundColor Green

# 5. TelemetryProbeManager.InitializeAll()
Write-Host "`n[3/3] Initializing TelemetryProbeManager..." -ForegroundColor Yellow
$mgrType = $mfpAsm.GetType("ModularFlightPanel.Core.Probes.TelemetryProbeManager")
$initMethod = $mgrType.GetMethod("InitializeAll", [System.Reflection.BindingFlags]"Public,Static")
$initMethod.Invoke($null, $null)

# 6. Verify 15 Probes
$probeNames = @(
    "FarProbe",
    "KerbalEngineerProbe",
    "MechJebProbe",
    "PrincipiaProbe",
    "RealAntennasProbe",
    "KerbalismProbe",
    "TrajectoriesProbe",
    "DockingAlignmentProbe",
    "GPWSProbe",
    "RealFuelsProbe",
    "TestFlightProbe",
    "DynamicBatteryStorageProbe",
    "SystemHeatProbe",
    "AtmosphereAutopilotProbe",
    "RP1AvionicsProbe"
)

Write-Host "`nProbe Availability & Traversal Statistics:" -ForegroundColor Cyan
Write-Host "-----------------------------------------------------------------"
$totalMembers = 0
$activeProbes = 0

foreach ($name in $probeNames) {
    $fullTypeName = "ModularFlightPanel.Core.Probes.$name"
    $t = $mfpAsm.GetType($fullTypeName)
    if ($t -ne $null) {
        $availProp = $t.GetProperty("IsAvailable", [System.Reflection.BindingFlags]"Public,Static")
        $isAvail = [bool]$availProp.GetValue($null, $null)
        
        $travProp = $t.GetProperty("Traverser", [System.Reflection.BindingFlags]"Public,Static")
        $trav = $travProp.GetValue($null, $null)
        $tag = ""
        $discProp = $trav.GetType().GetProperty("DiscoveredCount")
        $count = [int]$discProp.GetValue($trav, $null)

        $totalMembers += $count
        if ($isAvail) {
            $activeProbes++
            Write-Host ("  {0,-28} : AVAILABLE ( {1,4} API members )" -f $name, $count) -ForegroundColor Green
        } else {
            Write-Host ("  {0,-28} : Standby / Not Present" -f $name) -ForegroundColor Gray
        }
    } else {
        Write-Host "  Error: Type $fullTypeName not found in assembly!" -ForegroundColor Red
    }
}

Write-Host "-----------------------------------------------------------------"
Write-Host ("Total Active Probes: {0} / {1}" -f $activeProbes, $probeNames.Count) -ForegroundColor Yellow
Write-Host ("Total Traversed Public API Members: {0}" -f $totalMembers) -ForegroundColor Yellow
Write-Host "=================================================================`n"
