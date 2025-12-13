param(
    [string]$ReleaseDir = "build\windows\x64\runner\Release",
    [string]$OutDir = "dist",
    [string]$Name = "PocketFence-Portable-1.0.0.zip"
)

Write-Output "Packaging portable ZIP from Release folder..."

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
$fullRelease = Join-Path $repoRoot $ReleaseDir

if (-not (Test-Path $fullRelease)) {
    Write-Error "Release directory not found: $fullRelease"
    exit 2
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$destZip = Join-Path $OutDir $Name

if (Test-Path $destZip) { Remove-Item $destZip -Force }

Write-Output "Compressing $fullRelease -> $destZip"
Compress-Archive -Path (Join-Path $fullRelease '*') -DestinationPath $destZip -Force

if (-not (Test-Path $destZip)) {
    Write-Error "Failed to create zip: $destZip"
    exit 3
}

Write-Output "Created portable ZIP: $destZip"
exit 0
