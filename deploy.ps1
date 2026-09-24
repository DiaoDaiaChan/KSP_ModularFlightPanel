param(
    [string]$TargetKSP = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $TargetKSP)) {
    Write-Error "Target KSP directory does not exist: $TargetKSP"
    exit 1
}

# 1. 纯 UGUI 模式 (已彻底解耦，不调用任何 React 前端脚本)
Write-Host "[1/3] Target platform: Pure Unity UGUI Flight Panel (Decoupled Engine)" -ForegroundColor Cyan

# 2. 编译 C# 项目并自动输出
$csproj = Join-Path $PSScriptRoot "src\ModularFlightPanel\ModularFlightPanel.csproj"
if (Test-Path $csproj) {
    Write-Host "[2/3] Building ModularFlightPanel plugin (Release)..." -ForegroundColor Cyan
    & dotnet build $csproj --configuration Release
}

# 3. 完整同步 GameData 资产到游戏目录
$source = Join-Path $PSScriptRoot "GameData\ModularFlightPanel"
$dest = Join-Path $TargetKSP "GameData\ModularFlightPanel"

Write-Host "[3/3] Syncing ModularFlightPanel to: $dest" -ForegroundColor Cyan

if (!(Test-Path $dest)) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
}

# 针对可能被运行中 KSP 锁定的 DLL 文件采用原子更名替换 (Windows NTFS Atomic Swap)
$targetDll = Join-Path $dest "Plugins\ModularFlightPanel.dll"
$sourceDll = Join-Path $source "Plugins\ModularFlightPanel.dll"

if (Test-Path $sourceDll) {
    $targetPluginDir = Join-Path $dest "Plugins"
    if (!(Test-Path $targetPluginDir)) { New-Item -ItemType Directory -Path $targetPluginDir -Force | Out-Null }
    
    if (Test-Path $targetDll) {
        try {
            Copy-Item -Path $sourceDll -Destination $targetDll -Force -ErrorAction Stop
        } catch {
            Write-Warning "ModularFlightPanel.dll is locked by running KSP. Performing atomic rename swap..."
            $oldDll = Join-Path $targetPluginDir "ModularFlightPanel.dll.old"
            if (Test-Path $oldDll) { Remove-Item $oldDll -Force -ErrorAction SilentlyContinue }
            Rename-Item -Path $targetDll -NewName "ModularFlightPanel.dll.old" -Force
            Copy-Item -Path $sourceDll -Destination $targetDll -Force
            Write-Host "Atomic DLL swap completed successfully!" -ForegroundColor Green
        }
    } else {
        Copy-Item -Path $sourceDll -Destination $targetDll -Force
    }
}

# 同步其余所有资产 (配置、预设、材质等)
Copy-Item -Path "$source\*" -Destination $dest -Recurse -Force -Exclude "ModularFlightPanel.dll"

Write-Host "Deployment completed successfully! ModularFlightPanel UGUI assets & DLL synced to KSP." -ForegroundColor Green
exit 0
