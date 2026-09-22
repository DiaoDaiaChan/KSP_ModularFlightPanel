param(
    [string]$TargetKSP = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod"
)

$source = Join-Path $PSScriptRoot "GameData\ModularFlightPanel"
$dest = Join-Path $TargetKSP "GameData\ModularFlightPanel"

if (!(Test-Path $TargetKSP)) {
    Write-Error "Target KSP directory does not exist: $TargetKSP"
    exit 1
}

Write-Host "Syncing ModularFlightPanel to: $dest" -ForegroundColor Cyan

if (!(Test-Path $dest)) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
}

Copy-Item -Path "$source\*" -Destination $dest -Recurse -Force

Write-Host "Deployment completed successfully!" -ForegroundColor Green
