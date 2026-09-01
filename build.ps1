param(
    [ValidateSet("build", "register", "unregister", "clean", "all")]
    [string]$Target = "build",
    [switch]$Win11Menu
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$packageName = "GpuLauncher.ContextMenu"

function Stop-RunningHandlers {
    Get-Process -Name GpuContextMenuHandler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name GpuLauncher -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

function Invoke-Build {
    Stop-RunningHandlers

    & $csc /nologo /target:winexe /platform:x64 `
        /out:"$root\GpuLauncher\GpuLauncher.exe" `
        "$root\GpuLauncher\GpuLauncher.cs"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: GpuLauncher.exe" }

    & $csc /nologo /target:winexe /platform:x64 `
        /out:"$root\GpuContextMenu\GpuContextMenuHandler.exe" `
        "$root\GpuContextMenu\GpuContextMenuHandler.cs"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: GpuContextMenuHandler.exe" }

    Write-Output "Build OK."
}

function Register-ClassicMenu {
    $launcherPath = "$root\GpuLauncher\GpuLauncher.exe"
    $verbKey = "HKCU:\Software\Classes\exefile\shell\RunWithNvidiaGpu"
    $cmdKey = "$verbKey\command"

    New-Item -Path $verbKey -Force | Out-Null
    Set-Item -Path $verbKey -Value "Run with NVIDIA GPU"
    New-Item -Path $cmdKey -Force | Out-Null
    Set-Item -Path $cmdKey -Value "`"$launcherPath`" nvidia `"%1`""

    Write-Output "Classic menu entry registered. Shows under 'Show more options' for .exe files and shortcuts."
}

function Unregister-ClassicMenu {
    Remove-Item -Path "HKCU:\Software\Classes\exefile\shell\RunWithNvidiaGpu" -Recurse -Force -ErrorAction SilentlyContinue
}

function Register-Win11Menu {
    if (-not $Win11Menu) {
        Write-Output "Skipped: pass -Win11Menu to also register the Windows 11 top-level context menu entry."
        return
    }

    $devMode = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    if ($devMode -ne 1) {
        throw "Developer Mode is off. Enable it then retry."
    }

    Get-AppxPackage -Name $packageName | Remove-AppxPackage -ErrorAction SilentlyContinue
    Add-AppxPackage -Path "$root\GpuContextMenu\AppxManifest.xml" -Register -ExternalLocation "$root\GpuContextMenu" -ForceUpdateFromAnyVersion
    Write-Output "Windows 11 top-level menu entry registered."
}

function Unregister-Win11Menu {
    Get-AppxPackage -Name $packageName | Remove-AppxPackage -ErrorAction SilentlyContinue
}

function Invoke-Register {
    Register-ClassicMenu
    Register-Win11Menu
}

function Invoke-Unregister {
    Stop-RunningHandlers
    Unregister-ClassicMenu
    Unregister-Win11Menu
    Write-Output "Unregistered."
}

function Invoke-Clean {
    Stop-RunningHandlers
    Remove-Item "$root\GpuLauncher\GpuLauncher.exe" -ErrorAction SilentlyContinue
    Remove-Item "$root\GpuContextMenu\GpuContextMenuHandler.exe" -ErrorAction SilentlyContinue
    Write-Output "Cleaned."
}

switch ($Target) {
    "build" { Invoke-Build }
    "register" { Invoke-Register }
    "unregister" { Invoke-Unregister }
    "clean" { Invoke-Clean }
    "all" { Invoke-Build; Invoke-Register }
}
