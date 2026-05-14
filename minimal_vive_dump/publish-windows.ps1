param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $projectDir "minimal_vive_dump.csproj"
$repoRoot = Split-Path -Parent $projectDir
$releaseRoot = Join-Path $repoRoot "releases"
$publishDir = Join-Path $releaseRoot "vive-pose-dump-$Runtime"
$zipPath = "$publishDir.zip"

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "Published folder: $publishDir"
Write-Host "Published zip:    $zipPath"