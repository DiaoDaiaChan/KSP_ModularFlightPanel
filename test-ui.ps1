param(
    [string]$File = "",
    [string]$Code = "",
    [switch]$NoAscii = $false,
    [switch]$Render = $false,
    [string]$Widget = "",
    [string]$Screen = "blue",
    [string]$RenderMode = ""
)

$ErrorActionPreference = "Stop"

if ($Render) {
    Write-Host ">>> Triggering Native Unity 2019 Batchmode Headless UI Render (Backdrop Screen: $Screen)..." -ForegroundColor Magenta
    $unityExe = "C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe"
    if (!(Test-Path $unityExe)) {
        Write-Error "Cannot locate Unity editor at: $unityExe"
        exit 1
    }

    $projectPath = Join-Path $PSScriptRoot "unity"
    $logPath = Join-Path $PSScriptRoot "unity_render.log"

    $argList = @("-batchmode", "-quit", "-projectPath", $projectPath, "-executeMethod", "ModularFlightPanel.Editor.HeadlessUIRenderer.RenderHeadlessPreview", "-screen", $Screen, "-logFile", $logPath)
    if (![string]::IsNullOrEmpty($Widget)) {
        Write-Host ">>> Targeting single widget isolation: $Widget" -ForegroundColor Yellow
        $argList += @("-targetWidget", $Widget)
    }
    if (![string]::IsNullOrEmpty($RenderMode)) {
        Write-Host ">>> Targeting render mode: $RenderMode" -ForegroundColor Yellow
        $argList += @("-renderMode", $RenderMode)
    }

    $process = Start-Process -FilePath $unityExe -ArgumentList $argList -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Write-Error "Unity Headless UI Render failed with exit code $($process.ExitCode). Check $logPath"
        exit $process.ExitCode
    }

    if (![string]::IsNullOrEmpty($Widget)) {
        $safeName = $Widget.Replace(".", "_")
        $outPng = Join-Path $PSScriptRoot "GameData\ModularFlightPanel\PluginData\isolated_$safeName.png"
        Write-Host ">>> Native Unity UI Render Completed! Isolated single-widget preview saved to: $outPng" -ForegroundColor Green
    } else {
        $outPng = Join-Path $PSScriptRoot "GameData\ModularFlightPanel\PluginData\unity_headless_render.png"
        Write-Host ">>> Native Unity UI Render Completed! 1080P preview saved to: $outPng" -ForegroundColor Green
    }
    exit 0
}

$validatorProj = Join-Path $PSScriptRoot "tools\HeadlessValidator\HeadlessValidator.csproj"
if (!(Test-Path $validatorProj)) {
    Write-Error "Cannot find HeadlessValidator project at: $validatorProj"
    exit 1
}

$cliArgs = @()
if (![string]::IsNullOrEmpty($File)) {
    $cliArgs += "--file"
    $cliArgs += $File
}
if (![string]::IsNullOrEmpty($Code)) {
    $cliArgs += "--code"
    $cliArgs += $Code
}
if ($NoAscii) {
    $cliArgs += "--no-ascii"
}

Write-Host ">>> Executing ModularFlightPanel Headless Validator & Simulation Suite..." -ForegroundColor Cyan
& dotnet run --project $validatorProj -- $cliArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
