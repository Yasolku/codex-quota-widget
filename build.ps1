param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "CodexQuotaWidget\CodexQuotaWidget.csproj"
$destination = Join-Path $PSScriptRoot "$Output\$Runtime"

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained false `
    --output $destination

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host "Published to $destination"
