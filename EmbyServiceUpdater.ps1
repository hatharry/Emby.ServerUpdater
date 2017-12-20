﻿param([switch]$InstallTask, [switch]$UninstallTask)
#requires –runasadministrator

Install-PackageProvider -Name NuGet -Force
Install-Module -Name 7Zip4Powershell -Force
Import-Module -name 7Zip4Powershell

Class EmbyServiceUpdater
{

    $location = [string]
    $releaseChannel = [string]
    $release = [object]
    $localVersion = [version]
    $remoteVersion = [version]
    $assetUrl = [string]
    $isCore = [bool]
    $serviceName = [string]

    getlocation() {
        $this.serviceName = (Get-Service | Where-Object {$_.name -match "emby"}).name
        if ($this.serviceName.Length -eq 0){
            throw "can not find emby service"
        }
        try{
            $appLocation = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$($this.serviceName)\Parameters").Application
            $this.location = (Get-Item $appLocation).Directory.Parent.FullName
            $this.isCore = $true
        } catch {
            $appLocation = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$($this.serviceName)").ImagePath.split("`"")[1]
            $this.location = (Get-Item $appLocation).Directory.Parent.FullName
            $this.isCore = $false
        }
    }

    getLatestRelease() {
        $releases = ConvertFrom-Json (Invoke-WebRequest "https://api.github.com/repos/mediabrowser/emby/releases" -UseBasicParsing)
        $preRelease = $this.localVersion.Revision -ne 0
        $this.release = ($releases | Where-Object {$_.prerelease -eq $preRelease})[0] 
    }

    getLocalVersion() {
        if ($this.isCore) {
            $this.localVersion = [Version](Get-Item "$($this.location)\system\EmbyServer.dll").VersionInfo.FileVersion
        } else {
            $this.localVersion = [Version](Get-Item "$($this.location)\system\MediaBrowser.ServerApplication.exe").VersionInfo.FileVersion
        }
    }

    getAssetUrl() {
        if ($this.isCore) {
            if([Environment]::Is64BitOperatingSystem){
                $this.assetUrl = ($this.release.assets | Where-Object {$_.name -match "embyserver-(win|windows)-x64"}).browser_download_url
            } else {
                $this.assetUrl = ($this.release.assets | Where-Object {$_.name -match "embyserver-(win|windows)-x86"}).browser_download_url
            }
        } else {
            $this.assetUrl = ($this.release.assets | Where-Object {$_.name -match "emby.windows.zip"}).browser_download_url
        }
    }

    getUpdate() {
        try {
            if ($this.release.tag_name -gt $this.localVersion) {
                if (-not (test-path "$($this.location)\updates")) { 
                    (mkdir "$($this.location)\updates")
                }
                Invoke-WebRequest $this.assetUrl -OutFile "$($this.location)\updates\MBserver.zip" -UseBasicParsing
                $this.release.tag_name > "$($this.location)\updates\MBserver.zip.ver"
            } else {
                Write-Progress "emby is up to date $($this.localVersion)"
                Start-Sleep 1
            }
       } catch {
            if (Test-Path "$($this.location)\updates") {
                (Remove-Item "$($this.location)\updates" -Recurse)
            }
        }
        
    }

    install() {
        if (Test-Path "$($this.location)\Updates\MBserver.zip") {
            Stop-Service $($this.serviceName)
            Wait-Process embyserver -ErrorAction SilentlyContinue
            Wait-Process MediaBrowser.ServerApplication -ErrorAction SilentlyContinue
            if (Test-Path "$($this.location)\System.old") {
                Remove-Item "$($this.location)\System.old" -Recurse
            }
            if (Test-Path "$($this.location)\System") {
                Move-Item "$($this.location)\System" "$($this.location)\System.old"
            }
            if (-not (Test-Path "$($this.location)\System")) {
                try {
                    Expand-7Zip "$($this.location)\Updates\MBserver.zip" "$($this.location)"
                } catch {
                    if (Test-Path "$($this.location)\System") {
                        Remove-Item "$($this.location)\System" -Recurse
                    }
                    if (Test-Path "$($this.location)\System.old") {
                        Move-Item "$($this.location)\System.old" "$($this.location)\System"
                    }
                }
            }
            if (Test-Path "$($this.location)\System") {
                Remove-Item "$($this.location)\updates" -Recurse
            }
            Start-Service $($this.serviceName)
        }
    }

    installTask($scriptName) {
        Write-Progress "Installing Task"
        Start-Sleep 1
        if (-not (Test-Path "$($this.location)\updater")) {
            mkdir "$($this.location)\updater"
        }
        Copy-Item "$PSScriptRoot\$scriptName" "$($this.location)\updater\EmbyServiceUpdater.ps1" -Force
        if (Get-Command New-ScheduledTaskAction -ErrorAction SilentlyContinue) {
            $action = New-ScheduledTaskAction -Execute "Powershell.exe" `
            -Argument "-ExecutionPolicy Bypass -file `"$($this.location)\updater\EmbyServiceUpdater.ps1`""
            $trigger =  New-ScheduledTaskTrigger -Daily -At 4am
            Register-ScheduledTask -Action $action -Trigger $trigger -TaskName "Emby Service Updater" -Description "Emby Service Updater" -User "SYSTEM" -Force
        } else {
            start-Process "schtasks.exe" -ArgumentList "/create", "/sc DAILY", "/TN `"Emby Service Updater`"",
            "/RU SYSTEM", "/TR" ,"Powershell.exe", "-ExecutionPolicy Bypass -file `"$($this.location)\updater\EmbyServiceUpdater.ps1`"",
            "/ST 04:00", "/F" -Wait
        }
    }

    uninstallTask() {
        Write-Progress "Uninstalling Task"
        Start-Sleep 1
        if (Get-Command Unregister-ScheduledTask -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName "Emby Service Updater" -Confirm:$false
        } else {
            start-Process "schtasks.exe" -ArgumentList "/Delete", "/TN `"Emby Service Updater`"", "/F" -Wait
        }
        if (Test-Path "$($this.location)\updater") {
            Remove-Item "$($this.location)\updater" -Recurse
        }
    }
}


$Updater = [EmbyServiceUpdater]::new()
$Updater.getlocation()
if ($InstallTask) {
    $Updater.installTask($MyInvocation.MyCommand.Name)
} elseif ($UninstallTask) {
    $Updater.uninstallTask()
} else {
    $Updater.getLocalVersion()
    $Updater.getLatestRelease()
    $Updater.getAssetUrl()
    $Updater.getUpdate()
    $Updater.install()
}
