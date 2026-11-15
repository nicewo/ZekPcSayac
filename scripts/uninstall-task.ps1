#Requires -Version 5.1
<#
.SYNOPSIS
    ZekPcSayac Watchdog gorevini Windows Gorev Zamanlayicisi'nden siler (yonetici gerekir).

.EXAMPLE
    .\uninstall-task.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# --- Yonetici kontrolu ---
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Bu script yonetici olarak calistirilmalidir. PowerShell''i "Yonetici olarak calistir" ile acin.'
    exit 1
}

$task = Get-ScheduledTask -TaskPath '\ZekPcSayac\' -TaskName 'Watchdog' -ErrorAction SilentlyContinue
if (-not $task) {
    Write-Host 'Gorev zaten yok: \ZekPcSayac\Watchdog'
    exit 0
}

Unregister-ScheduledTask -TaskPath '\ZekPcSayac\' -TaskName 'Watchdog' -Confirm:$false
Write-Host 'Gorev silindi: \ZekPcSayac\Watchdog'
exit 0
