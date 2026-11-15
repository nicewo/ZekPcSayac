#Requires -Version 5.1
<#
.SYNOPSIS
    ZekPcSayac Watchdog gorevini Windows Gorev Zamanlayicisi'ne kurar (yonetici gerekir).

.DESCRIPTION
    Gorev: \ZekPcSayac\Watchdog
    Tetikleyiciler: kullanici oturum acinca (AtLogOn) + her 1 dakikada bir suresiz tekrar.
    Calisma: -UserName kullanicisi, Interactive, Limited (yukseltilmemis) yetki.

.PARAMETER UserName
    Gorevin calisacagi kullanici (ornek: DOMAIN\kullanici veya PC\kullanici). Zorunlu.

.PARAMETER InstallDir
    Verilirse, script klasorunun ustundeki publish ciktisi bu klasore kopyalanir.

.PARAMETER ExePath
    Gorevin calistiracagi exe. Varsayilan: InstallDir\ZekPcSayac.exe; InstallDir
    verilmemisse ..\ZekPcSayac.exe (script klasorune gore).

.EXAMPLE
    .\install-task.ps1 -UserName "PC\zekeriyas" -InstallDir "C:\ZekPcSayac"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UserName,

    [string]$InstallDir,

    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

# --- Yonetici kontrolu ---
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Bu script yonetici olarak calistirilmalidir. PowerShell''i "Yonetici olarak calistir" ile acin.'
    exit 1
}

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir  = [IO.Path]::GetFullPath((Join-Path $scriptDir '..'))

# --- InstallDir verildiyse publish ciktisini kopyala ---
if ($InstallDir) {
    $InstallDir = [IO.Path]::GetFullPath($InstallDir)

    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    Write-Host "Publish ciktisi kopyalaniyor: $sourceDir -> $InstallDir"
    Get-ChildItem -LiteralPath $sourceDir -Force |
        Where-Object { $_.Name -ne 'scripts' -and $_.Name -ne '.git' } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $InstallDir -Recurse -Force
        }
}

# --- ExePath belirleme ---
if (-not $ExePath) {
    if ($InstallDir) {
        $ExePath = Join-Path $InstallDir 'ZekPcSayac.exe'
    }
    else {
        $ExePath = Join-Path $scriptDir '..\ZekPcSayac.exe'
    }
}
$ExePath = [IO.Path]::GetFullPath($ExePath)

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    Write-Error "Calistirilacak exe bulunamadi: $ExePath"
    exit 1
}

# --- Gorev tanimi ---
# AtLogOn tetikleyicisi (kullanici oturum acinca)
$logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $UserName

# Her 1 dakikada bir suresiz tekrar eden tetikleyici
$repeatTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Minutes 1) `
    -RepetitionDuration ([TimeSpan]::MaxValue)

$action = New-ScheduledTaskAction -Execute $ExePath

$principalObj = New-ScheduledTaskPrincipal -UserId $UserName `
    -LogonType Interactive -RunLevel Limited

$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskPath '\ZekPcSayac\' -TaskName 'Watchdog' `
    -Action $action `
    -Trigger @($logonTrigger, $repeatTrigger) `
    -Principal $principalObj `
    -Settings $settings `
    -Force | Out-Null

Write-Host 'Gorev kuruldu: \ZekPcSayac\Watchdog'
Write-Host "Calistirilacak exe: $ExePath"
Write-Host "Kullanici: $UserName"
exit 0
