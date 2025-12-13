param(
    [string]$ReleaseDir = "build\windows\x64\runner\Release",
    [string]$IssFile = "tools\installer\pocketfence_installer.iss",
    [string]$OutDir = "dist"
)

Write-Output "Building installer for PocketFence..."

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
# repoRoot is the parent of the tools directory (assumes script is in repo/tools)
$repoRoot = Split-Path -Parent $scriptDir
$fullIss = Join-Path $repoRoot $IssFile
$fullRelease = Join-Path $repoRoot $ReleaseDir

if (-not (Test-Path $fullRelease)) {
    Write-Error "Release directory not found: $fullRelease"
    exit 2
}

# Locate ISCC (Inno Setup Compiler)
$isccPaths = @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe")
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Output "ISCC.exe not found. Please install Inno Setup (https://jrsoftware.org)."
    Write-Output "You can install via winget: winget install --id JRSoftware.InnoSetup -e"
    exit 3
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# Prepare an ISS file copy with ReleaseDir resolved
$tempIss = Join-Path $OutDir "pocketfence_installer_resolved.iss"
$issContent = Get-Content $fullIss -Raw
$resolved = $issContent -replace "\{#ReleaseDir\}", ($fullRelease -replace "\\","\\\\")
# Replace signer path token with absolute path to dist\signer.cer (escaped for ISS)
$signerPath = Join-Path $repoRoot "dist\signer.cer"
$escapedSigner = $signerPath -replace "\\","\\\\"
$resolved = $resolved -replace "\{#SignerPath\}", $escapedSigner
Set-Content -Path $tempIss -Value $resolved -Encoding UTF8

Write-Output "Compiling installer using ISCC: $iscc"
& "$iscc" "$tempIss" "/O$OutDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Output "Installer created in $OutDir"
exit 0
