param(
    [string]$File = "",
    [string]$Code = "",
    [switch]$NoAscii = $false,
    [switch]$Render = $false,
    [string]$Widget = "",
    [string]$Screen = "blue",
    [string]$RenderMode = "",
    [string]$Frame = "surface",
    [string]$Preset = "",
    [string]$Theme = "",
    [string]$ArtifactDir = "C:\Users\43701\.gemini\antigravity\brain\5948f05d-6f88-4b19-9459-e069c771a8d0",
    [string]$OutputName = "unity_headless_render.png"
)

$ErrorActionPreference = "Stop"

# ==============================================================================================
# 环境自愈（不要删！）
# 某些终端 / 沙箱 / 计划任务会话会丢失 Windows 关键环境变量（ProgramFiles、ProgramData…），
# 导致 dotnet / NuGet 静默失败：脚本只打一行 banner 就 exit 1，看不到任何错误原因 ——
# 这种"哑失败"会让 CI 门禁形同虚设（改了没生效也看不出来）。
# 因此在调用 dotnet 之前先把缺失的变量补齐，并把 dotnet 的解析结果显式校验。
# ==============================================================================================
$envDefaults = [ordered]@{
    'ProgramFiles'      = 'C:\Program Files'
    'ProgramFiles(x86)' = 'C:\Program Files (x86)'
    'ProgramData'       = 'C:\ProgramData'
    'APPDATA'           = (Join-Path $env:USERPROFILE 'AppData\Roaming')
    'LOCALAPPDATA'      = (Join-Path $env:USERPROFILE 'AppData\Local')
}
foreach ($name in $envDefaults.Keys) {
    if ([string]::IsNullOrEmpty([System.Environment]::GetEnvironmentVariable($name))) {
        [System.Environment]::SetEnvironmentVariable($name, $envDefaults[$name])
    }
}

function Resolve-DotNetExe {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd -and (Test-Path $cmd.Source)) { return $cmd.Source }

    foreach ($candidate in @(
            (Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe'),
            (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'))) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }
    return $null
}

$dotnetExe = Resolve-DotNetExe
if (-not $dotnetExe) {
    Write-Error "Cannot locate the dotnet CLI. Install the .NET SDK, or add it to PATH. (环境变量与探测路径均已尝试)"
    exit 1
}

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
    if (![string]::IsNullOrEmpty($Frame)) {
        Write-Host ">>> Targeting reference frame: $Frame" -ForegroundColor Yellow
        $argList += @("-frame", $Frame)
    }
    if ([string]::IsNullOrEmpty($Preset) -and ![string]::IsNullOrEmpty($File)) {
        $Preset = $File
    }
    if (![string]::IsNullOrEmpty($Preset)) {
        Write-Host ">>> Targeting preset: $Preset" -ForegroundColor Yellow
        $argList += @("-preset", $Preset)
    }
    if (![string]::IsNullOrEmpty($Theme)) {
        Write-Host ">>> Targeting theme: $Theme" -ForegroundColor Yellow
        $argList += @("-theme", $Theme)
    }
    if (![string]::IsNullOrEmpty($ArtifactDir)) {
        $argList += @("-artifactDir", $ArtifactDir)
    }
    if (![string]::IsNullOrEmpty($OutputName)) {
        $argList += @("-outputName", $OutputName)
    }

    $process = Start-Process -FilePath $unityExe -ArgumentList $argList -PassThru
    $timeoutTicks = 180 # 90 seconds timeout
    $ticks = 0
    while (-not $process.HasExited -and $ticks -lt $timeoutTicks) {
        Start-Sleep -Milliseconds 500
        $ticks++
    }

    if (-not $process.HasExited) {
        Write-Warning "Unity process timed out after 90s, terminating..."
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        exit 1
    }

    $unityExitCode = $process.ExitCode
    if ($unityExitCode -ne 0) {
        Write-Error "Unity Headless UI Render failed with exit code $unityExitCode. Check $logPath"
        exit $unityExitCode
    }

    if (![string]::IsNullOrEmpty($Widget)) {
        $safeName = $Widget.Replace(".", "_")
        $outPng = Join-Path $PSScriptRoot "GameData\ModularFlightPanel\PluginData\isolated_$safeName.png"
        Write-Host ">>> Native Unity UI Render Completed! Isolated single-widget preview saved to: $outPng" -ForegroundColor Green
    } else {
        $outPng = Join-Path $PSScriptRoot "GameData\ModularFlightPanel\PluginData\$OutputName"
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
& $dotnetExe run --project $validatorProj -- $cliArgs
$finalExit = if ($LASTEXITCODE -ne $null) { $LASTEXITCODE } else { 0 }
exit $finalExit
