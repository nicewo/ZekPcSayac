#Requires -Version 5.1
<#
.SYNOPSIS
    ZekPcSayac'i GitHub'daki son Release'e gunceller (yonetici veya SYSTEM gerekir).

.EXAMPLE
    # Elle guncelle:
    powershell -ExecutionPolicy Bypass -File "C:\Program Files\ZekPcSayac\scripts\update.ps1"

.EXAMPLE
    # Her gun 12:00'de ve bilgisayar acilinca otomatik guncelleme gorevi kur:
    powershell -ExecutionPolicy Bypass -File "C:\Program Files\ZekPcSayac\scripts\update.ps1" -RegisterDaily
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Program Files\ZekPcSayac',
    [string]$Repo = 'nicewo/ZekPcSayac',
    [string]$AssetName = 'ZekPcSayac-win-x64.zip',
    [switch]$RegisterDaily,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$logFile = Join-Path $InstallDir 'update.log'
function Log($m) {
    $l = '[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $m
    Write-Host $l
    try { Add-Content -Path $logFile -Value $l -Encoding UTF8 } catch { }
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Yonetici olarak calistirin.'
    exit 1
}

if ($RegisterDaily) {
    $self = Join-Path $InstallDir 'scripts\update.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$self`" -InstallDir `"$InstallDir`""
    $triggers = @(
        (New-ScheduledTaskTrigger -Daily -At '12:00'),
        (New-ScheduledTaskTrigger -AtStartup)
    )
    $principalObj = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
    Register-ScheduledTask -TaskPath '\ZekPcSayac\' -TaskName 'Updater' -Action $action -Trigger $triggers `
        -Principal $principalObj -Settings $settings -Force | Out-Null
    Log 'Otomatik guncelleme gorevi kuruldu: \ZekPcSayac\Updater (her gun 12:00 + acilista)'
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'ZekPcSayac-Updater' }

try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
} catch {
    Log "GitHub'a ulasilamadi: $($_.Exception.Message)"
    exit 0
}

$latest = $release.tag_name
$versionFile = Join-Path $InstallDir 'version.txt'
$current = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '' }

if (-not $Force -and $current -eq $latest) {
    Log "Guncel: $current"
    exit 0
}

$asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
if (-not $asset) { Log "Release $latest icinde $AssetName yok"; exit 1 }

$tmp = Join-Path $env:TEMP ('zekpcsayac-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$zip = Join-Path $tmp $AssetName
Log "Indiriliyor: $latest ($current -> $latest)"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers $headers -UseBasicParsing
$extract = Join-Path $tmp 'x'
Expand-Archive -Path $zip -DestinationPath $extract -Force
if (-not (Test-Path (Join-Path $extract 'ZekPcSayac.exe'))) { Log 'Zip icinde ZekPcSayac.exe yok, iptal'; exit 1 }

# Kopyalarken watchdog uygulamayi yeniden acmasin
$watchdog = Get-ScheduledTask -TaskPath '\ZekPcSayac\' -TaskName 'Watchdog' -ErrorAction SilentlyContinue
if ($watchdog) { Disable-ScheduledTask -InputObject $watchdog | Out-Null }

try {
    Get-Process -Name 'ZekPcSayac' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir | Out-Null }
    Copy-Item -Path (Join-Path $extract '*') -Destination $InstallDir -Recurse -Force
    Set-Content -Path $versionFile -Value $latest -NoNewline
    Log "Guncellendi: $latest"
}
finally {
    if ($watchdog) {
        Enable-ScheduledTask -InputObject $watchdog | Out-Null
        Start-ScheduledTask -InputObject $watchdog -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
