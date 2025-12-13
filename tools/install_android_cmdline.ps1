<#
Installs Android command-line tools into the Android SDK and accepts licenses.
Run as normal user (doesn't require Admin). Usage:
  powershell -ExecutionPolicy Bypass -File .\tools\install_android_cmdline.ps1
#>

Write-Host "Detecting Android SDK location..."
$sdk = $env:ANDROID_SDK_ROOT
if ([string]::IsNullOrEmpty($sdk)) { $sdk = $env:ANDROID_HOME }
if ([string]::IsNullOrEmpty($sdk)) { $sdk = Join-Path $env:LOCALAPPDATA "Android\Sdk" }
Write-Host "Using SDK path: $sdk"

if (-not (Test-Path $sdk)) {
    Write-Host "SDK path does not exist. Creating: $sdk"
    New-Item -ItemType Directory -Path $sdk -Force | Out-Null
}

$zipUrl = 'https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip'
$zipDest = Join-Path $env:TEMP 'commandlinetools-win-latest.zip'
$extractTemp = Join-Path $env:TEMP 'cmdline_extract'
if (Test-Path $extractTemp) { Remove-Item -Recurse -Force $extractTemp }
New-Item -ItemType Directory -Path $extractTemp | Out-Null

Write-Host "Downloading command-line tools from $zipUrl to $zipDest"
try {
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipDest -UseBasicParsing -ErrorAction Stop
} catch {
    Write-Error "Failed to download command-line tools: $_"
    exit 1
}

Write-Host "Extracting..."
try {
    Expand-Archive -Path $zipDest -DestinationPath $extractTemp -Force
} catch {
    Write-Error "Failed to extract archive: $_"
    exit 1
}

# The zip contains a folder named "cmdline-tools" with subfolder 'bin' etc.
$src = Join-Path $extractTemp 'cmdline-tools'
$dest = Join-Path $sdk 'cmdline-tools\latest'
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Path $dest | Out-Null

Write-Host "Moving files to $dest"
try {
    Get-ChildItem -Path $src | ForEach-Object { Move-Item -Path $_.FullName -Destination $dest }
} catch {
    Write-Error "Failed to move extracted files: $_"
    exit 1
}

$sdkmanager = Join-Path $dest 'bin\sdkmanager.bat'
if (-not (Test-Path $sdkmanager)) {
    Write-Error "sdkmanager not found at expected location: $sdkmanager"
    exit 1
}

Write-Host "Installing platform-tools and accepting licenses (may prompt)..."
try {
    # Ensure platform-tools installed to satisfy some tooling
    & "$sdkmanager" "platform-tools" "platforms;android-33" --sdk_root="$sdk"
} catch {
    Write-Warning "Failed to install platform-tools or platforms automatically: $_"
}

# Accept licenses by piping 'y' responses
try {
    cmd /c "echo y|""$sdkmanager"" --licenses --sdk_root="$sdk""
} catch {
    Write-Warning "Automatic license acceptance may have failed; try running '$sdkmanager --licenses' manually."
}

Write-Host "Cleaning up..."
Remove-Item -Force $zipDest
Remove-Item -Recurse -Force $extractTemp

Write-Host "Done. Run 'flutter doctor' to verify."