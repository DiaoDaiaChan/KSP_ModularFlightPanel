[CmdletBinding()]
param(
    [string]$KspRoot = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program",
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoDeploy
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$pluginProject = Join-Path $repoRoot "src\ModularFlightPanel\ModularFlightPanel.csproj"
$validatorProject = Join-Path $repoRoot "tools\HeadlessValidator\HeadlessValidator.csproj"
$validatorDll = Join-Path $repoRoot "tools\HeadlessValidator\bin\$Configuration\net6.0\HeadlessValidator.dll"
$unityProject = Join-Path $repoRoot "unity"
$unityLog = Join-Path $env:TEMP "mfp-assetbundle-build.log"
$gameDataSource = Join-Path $repoRoot "GameData\ModularFlightPanel"
$pluginOutput = Join-Path $repoRoot "src\ModularFlightPanel\bin\$Configuration\ModularFlightPanel.dll"
$pluginPdb = Join-Path $repoRoot "src\ModularFlightPanel\bin\$Configuration\ModularFlightPanel.pdb"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Step
    )

    Write-Host "`n[$Step] $Executable $($Arguments -join ' ')" -ForegroundColor Cyan
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

try {
    $dotnetCommand = Get-Command "dotnet.exe" -CommandType Application -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $KspRoot -PathType Container)) {
        throw "KSP root does not exist: $KspRoot"
    }
    foreach ($requiredPath in @(
        (Join-Path $KspRoot "KSP_x64_Data\Managed\Assembly-CSharp.dll"),
        (Join-Path $KspRoot "GameData"),
        $pluginProject,
        $validatorProject,
        $UnityExe
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Required build input was not found: $requiredPath"
        }
    }

    if (-not $NoDeploy -and (Get-Process "KSP_x64" -ErrorAction SilentlyContinue)) {
        throw "Close KSP_x64 before deployment so the plugin DLL can be replaced safely."
    }

    Write-Host "Target KSP: $KspRoot" -ForegroundColor Green

    Invoke-Checked $dotnetCommand.Source @(
        "build", $validatorProject, "--configuration", $Configuration,
        "-p:KSPRoot=$KspRoot"
    ) "Build validator"

    Invoke-Checked $dotnetCommand.Source @($validatorDll, "--mirror-check") "Source and Unity mirror check"
    Invoke-Checked $dotnetCommand.Source @($validatorDll, "--no-ascii") "Headless validation suite"

    Invoke-Checked $dotnetCommand.Source @(
        "build", $pluginProject, "--configuration", $Configuration,
        "-p:KSPRoot=$KspRoot", "-p:SkipPostBuildCopy=true"
    ) "Build plugin DLL"

    if (-not (Test-Path -LiteralPath $pluginOutput)) {
        throw "Plugin build succeeded but output DLL is missing: $pluginOutput"
    }

    $buildStarted = Get-Date
    if (Test-Path -LiteralPath $unityLog) { Remove-Item -LiteralPath $unityLog -Force }
    $unityArguments = "-batchmode -nographics -quit -projectPath `"$unityProject`" -executeMethod ModularFlightPanel.Editor.BuildAssetBundles.BuildWorkspaceAssetBundles -logFile `"$unityLog`""
    Write-Host "`n[Build Unity assets] $UnityExe $unityArguments" -ForegroundColor Cyan
    $unityProcess = Start-Process -FilePath $UnityExe -ArgumentList $unityArguments -WorkingDirectory $unityProject -PassThru -Wait -WindowStyle Hidden
    $unityOutput = if (Test-Path -LiteralPath $unityLog) { Get-Content -LiteralPath $unityLog -Raw } else { "" }
    if ($unityProcess.ExitCode -ne 0 -or
        $unityOutput -notmatch "AssetBundle build finished successfully!" -or
        $unityOutput -match "has not been activated with a valid License|license is invalid|Shader error|error CS\d+") {
        Get-Content -LiteralPath $unityLog -Tail 80 -ErrorAction SilentlyContinue
        throw "Unity asset build did not complete successfully. See log: $unityLog"
    }

    $bundleSource = Join-Path $gameDataSource "AssetBundles"
    $bundleFile = Join-Path $bundleSource "modularflightpanel.ksp"
    if (-not (Test-Path -LiteralPath $bundleFile) -or (Get-Item $bundleFile).LastWriteTime -lt $buildStarted) {
        throw "Unity did not produce a fresh shader bundle: $bundleFile"
    }
    Write-Host "Unity shader bundle built: $bundleFile" -ForegroundColor Green

    if ($NoDeploy) {
        Write-Host "`nBuild and checks finished. -NoDeploy was set, so no DLL or assets were copied." -ForegroundColor Yellow
        exit 0
    }

    $repoPluginDir = Join-Path $gameDataSource "Plugins"
    $targetRoot = Join-Path $KspRoot "GameData\ModularFlightPanel"
    $targetPluginDir = Join-Path $targetRoot "Plugins"
    New-Item -ItemType Directory -Path $repoPluginDir,$targetPluginDir -Force | Out-Null
    Copy-Item -LiteralPath $pluginOutput -Destination (Join-Path $repoPluginDir "ModularFlightPanel.dll") -Force
    Copy-Item -LiteralPath $pluginOutput -Destination (Join-Path $targetPluginDir "ModularFlightPanel.dll") -Force
    if (Test-Path -LiteralPath $pluginPdb) {
        Copy-Item -LiteralPath $pluginPdb -Destination (Join-Path $repoPluginDir "ModularFlightPanel.pdb") -Force
        Copy-Item -LiteralPath $pluginPdb -Destination (Join-Path $targetPluginDir "ModularFlightPanel.pdb") -Force
    }

    # Sync shipped assets while preserving live player layouts, themes, and vessel overrides.
    foreach ($sourceFile in Get-ChildItem -LiteralPath $gameDataSource -File -Recurse) {
        $relativePath = $sourceFile.FullName.Substring($gameDataSource.Length).TrimStart('\', '/')
        $isProtected = $relativePath -like "PluginData\layout*.json" -or
                       $relativePath -like "PluginData\theme_settings*.json" -or
                       $relativePath -like "PluginData\Vessels\*"
        if ($isProtected) { continue }

        $targetFile = Join-Path $targetRoot $relativePath
        $targetDirectory = Split-Path -Parent $targetFile
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $targetFile -Force
    }

    Write-Host "`nDeployment complete. DLL and ModularFlightPanel assets were copied to:" -ForegroundColor Green
    Write-Host $targetRoot -ForegroundColor Green
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
