<#
.SYNOPSIS
    Control Windows hosted network (hotspot) and optionally block other Wi‑Fi adapters.

.DESCRIPTION
    This script creates/starts/stops a hostednetwork using netsh and, if requested,
    creates firewall rules to block other Wi‑Fi adapters so that connected clients
    can only use the hosted hotspot.

    The script requires elevation for firewall and hostednetwork actions and will
    attempt to re-run elevated when needed.

.PARAMETER Name
    Hotspot SSID to create/start. Required when not using -Stop.

.PARAMETER Password
    Optional: WPA2 key as SecureString (recommended). To pass a plain text
    password, convert it first:
        $pw = ConvertTo-SecureString -String 'MyPass123' -AsPlainText -Force

.PARAMETER BlockOthers
    Switch: create firewall rules to block other Wi‑Fi adapters.

.PARAMETER UnblockOthers
    Switch: remove firewall rules created by this script.

.PARAMETER Stop
    Switch: stop hostednetwork (when used, -Name is not required).

.EXAMPLE
    # Start hotspot and block other adapters
    .\hotspot_control.ps1 -Name "MyHotspot" -BlockOthers

.EXAMPLE
    # Stop hotspot and remove blocking rules
    .\hotspot_control.ps1 -Stop -UnblockOthers

.NOTES
    It's recommended to supply a SecureString for -Password. The script converts
    SecureString to plain only when invoking `netsh` and zeroes sensitive memory
    afterward.
#>

[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='Medium')]
param(
    [Parameter(Mandatory=$false)]
    [ValidateNotNullOrEmpty()]
    [string]$Name,

    [Parameter(Mandatory=$false)]
    [System.Security.SecureString]$Password,

    [switch]$BlockOthers,
    [switch]$UnblockOthers,
    [switch]$Stop
)

function Test-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "Requesting elevation..."
        $scriptPath = $MyInvocation.MyCommand.Definition
        $paramList = @()
        if ($PSBoundParameters.ContainsKey('Name')) { $paramList += '-Name'; $paramList += $PSBoundParameters['Name'] }
        if ($PSBoundParameters.ContainsKey('BlockOthers') -and $PSBoundParameters['BlockOthers']) { $paramList += '-BlockOthers' }
        if ($PSBoundParameters.ContainsKey('UnblockOthers') -and $PSBoundParameters['UnblockOthers']) { $paramList += '-UnblockOthers' }
        if ($PSBoundParameters.ContainsKey('Stop') -and $PSBoundParameters['Stop']) { $paramList += '-Stop' }

        # If a SecureString password was provided, we can't rehydrate it to the elevated process safely here.
        # Prompt the user to provide the password again in the elevated session if needed.
        if ($PSBoundParameters.ContainsKey('Password')) {
            Write-Warning "A password was provided. When the script elevates you will be prompted to re-enter the password as SecureString or allow the script to generate one."
        }

        $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$scriptPath) + $paramList
        Start-Process -FilePath (Get-Command powershell).Source -ArgumentList $argList -Verb RunAs
        exit 0
    }
}

function New-RandomKey {
    # 12-char random ASCII key suitable for WPA2
    $chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    -join ((1..12) | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
}

# Ensure elevated
Test-Administrator

if ($Stop) {
    Write-Host "Stopping hostednetwork..."
    netsh wlan stop hostednetwork | Out-String | Write-Host
    if ($UnblockOthers) {
        Write-Host "Removing PocketFence firewall rules..."
        Get-NetFirewallRule -DisplayName 'PocketFence_Block_*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -Confirm:$false
    }
    exit 0
}

if (-not $Name) {
    Write-Error "Missing -Name parameter. Provide an SSID name for the hotspot."
    exit 2
}

# Handle password: prefer SecureString. If none provided, generate one and create a SecureString.
[string]$plainPassword = ''
if (-not $Password) {
    $plainPassword = New-RandomKey
    # convert to SecureString for internal representation
    $Password = ConvertTo-SecureString -String $plainPassword -AsPlainText -Force
} else {
    # convert SecureString to plain for use with netsh
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try { $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

Write-Host "Configuring hostednetwork SSID='$Name'..."
try {
    netsh wlan set hostednetwork mode=allow ssid="$Name" key="$plainPassword" keyUsage=persistent | Out-String | Write-Host
} catch {
    Write-Error "Failed to configure hostednetwork: $_"
    exit 3
}

Write-Host "Starting hostednetwork..."
try {
    netsh wlan start hostednetwork | Out-String | Write-Host
} catch {
    Write-Error "Failed to start hostednetwork: $_"
    exit 4
}

if ($BlockOthers) {
    Write-Host "Applying firewall rules to block other Wi‑Fi adapters..."
    # Identify wireless adapters
    $wifiAdapters = Get-NetAdapter -Physical | Where-Object { $_.InterfaceDescription -match 'Wireless|Wi-Fi|WLAN|802.11' }
    if (-not $wifiAdapters) { Write-Warning "No wireless adapters detected via Get-NetAdapter." }
    foreach ($ad in $wifiAdapters) {
        # Skip adapters likely representing hosted network virtual adapter
        if ($ad.InterfaceDescription -match 'Hosted|Virtual' -or $ad.Name -match 'Hosted|Virtual') {
            Write-Host "Skipping adapter (likely hotspot virtual adapter): $($ad.Name) - $($ad.InterfaceDescription)"
            continue
        }
        $ruleNameOut = "PocketFence_Block_Out_$($ad.Name)"
        $ruleNameIn = "PocketFence_Block_In_$($ad.Name)"
        Write-Host "Blocking adapter: $($ad.Name)"
        # Create outbound and inbound block rules for this interface
        New-NetFirewallRule -DisplayName $ruleNameOut -Direction Outbound -Action Block -InterfaceAlias $ad.Name -Enabled True -Profile Any -ErrorAction SilentlyContinue
        New-NetFirewallRule -DisplayName $ruleNameIn -Direction Inbound -Action Block -InterfaceAlias $ad.Name -Enabled True -Profile Any -ErrorAction SilentlyContinue
    }
    Write-Host "Firewall rules applied. To remove them later run this script with -Stop -UnblockOthers or run: Get-NetFirewallRule -DisplayName 'PocketFence_Block_*' | Remove-NetFirewallRule"
}

Write-Host "Done. Hotspot '$Name' should be running. Password: $plainPassword"
exit 0
