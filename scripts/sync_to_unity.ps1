$repoRoot = Resolve-Path "$PSScriptRoot/.."
Write-Host "Syncing src to unity via HeadlessValidator mirror engine..."
dotnet run --project "$repoRoot/tools/HeadlessValidator/HeadlessValidator.csproj" -- --mirror-fix

