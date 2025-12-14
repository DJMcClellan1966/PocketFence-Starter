<#
PowerShell script to enable Developer Mode and install Visual Studio Build Tools
Usage (run as Admin):
  .\tools\setup_windows_build.ps1            # download & install build tools + enable Developer Mode
  .\tools\setup_windows_build.ps1 -RunFlutter # also attempts `flutter build windows` at end
#>
[CmdletBinding()]
param(
    [switch]$RunFlutter
)
function Test-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Error "This script must be run as Administrator. Right-click PowerShell and choose 'Run as administrator'."
        exit 1
    }
}

function Enable-DeveloperMode {
    Write-Host "Enabling Developer Mode (symlink support)..."
    $regPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    try {
        Set-ItemProperty -Path $regPath -Name 'AllowDevelopmentWithoutDevLicense' -Value 1 -Type DWord -Force
        Write-Host "Developer Mode registry key set. Some changes may require sign-out or reboot to take full effect."
    } catch {
        Write-Warning "Failed to write registry key: $_"
    }
}

function Install-BuildTools {
    param(
        [string]$Url = 'https://aka.ms/vs/17/release/vs_BuildTools.exe',
        [string]$Dest = "$env:TEMP\vs_BuildTools.exe"
    )
    Write-Host "Downloading Visual Studio Build Tools from: $Url"
    if (-not (Test-Path $Dest)) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing -ErrorAction Stop
        } catch {
            Write-Error "Failed to download Visual Studio Build Tools: $_"
            return $false
        }
    } else {
        Write-Host "Using cached installer: $Dest"
    }

    $workloads = @(
        'Microsoft.VisualStudio.Workload.VCTools' # C++ build tools
    )
    $components = @(
        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64',
        'Microsoft.VisualStudio.Component.VC.CMake.Project',
        'Microsoft.VisualStudio.Component.Windows10SDK.19041' # Windows 10 SDK
    )

    $addArgs = @()
    foreach ($w in $workloads) { $addArgs += "--add"; $addArgs += $w }
    foreach ($c in $components) { $addArgs += "--add"; $addArgs += $c }

    $installerArgs = @('--quiet','--wait','--norestart','--includeRecommended') + $addArgs + @('--log', "$env:TEMP\vs_buildtools_install.log")
    Write-Host "Running build tools installer (this may take a while)..."
    try {
        $p = Start-Process -FilePath $Dest -ArgumentList $installerArgs -Wait -PassThru -NoNewWindow
        if ($p.ExitCode -ne 0) {
            Write-Warning "Installer exited with code $($p.ExitCode). Check $env:TEMP\vs_buildtools_install.log for details."
            return $false
        }
    } catch {
        Write-Error "Failed to run installer: $_"
        return $false
    }
    Write-Host "Visual Studio Build Tools installation completed (or was already present)."
    return $true
}

function Invoke-FlutterBuild {
    Write-Host "Running 'flutter doctor' then 'flutter build windows'..."
    try {
        & flutter doctor
        & flutter build windows
    } catch {
        Write-Warning "Flutter build failed or Flutter not found in PATH: $_"
    }
}

# --- main
Test-Administrator
Enable-DeveloperMode
$installed = Install-BuildTools
if (-not $installed) {
    Write-Warning "Build Tools installation failed or requires user interaction. Check the log in $env:TEMP\vs_buildtools_install.log"
} else {
    Write-Host "Build tools installed."
}

Write-Host "Finished setup. If you installed or modified Visual Studio components, a reboot may be required."

if ($RunFlutter) {
    Invoke-FlutterBuild
}

Write-Host "Script complete."
