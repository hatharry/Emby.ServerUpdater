param([switch]$InstallTask, [switch]$UninstallTask)
#requires –runasadministrator

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

    getlocation() {
        try{
            $appLocation = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\Emby\Parameters").Application
            $this.location = (Get-Item $appLocation).Directory.Parent.FullName
            $this.isCore = $true
        } catch {
            $appLocation = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\Emby").ImagePath.split("`"")[1]
            $this.location = (Get-Item $appLocation).Directory.Parent.FullName
            $this.isCore = $false
        }
    }

    getLatestRelease() {
        $releases = ConvertFrom-Json (Invoke-WebRequest "https://api.github.com/repos/mediabrowser/emby/releases" -UseBasicParsing)
        if ($this.localVersion.Revision -eq 0) {
            $this.release = ($releases | Where-Object {$_.prerelease -eq $false})[0]
        } else {
            $this.release = ($releases | Where-Object {$_.name -match "beta"})[0]
        }
    }

    getLocalVersion() {
        try {
            $this.localVersion = (Get-Item "$($this.location)\system\EmbyServer.dll").VersionInfo.FileVersion
        } catch {
            $this.localVersion = (Get-Item "$($this.location)\system\MediaBrowser.ServerApplication.exe").VersionInfo.FileVersion
        }
    }

    getAssetUrl() {
        if ($this.isCore) {
            if([Environment]::Is64BitOperatingSystem){
                $this.assetUrl = ($this.release.assets | Where-Object {$_.name -match "embyserver-win-x64"}).browser_download_url
            } else {
                $this.assetUrl = ($this.release.assets | Where-Object {$_.name -match "embyserver-win-x86"}).browser_download_url
            }
        } else {
            $this.assetUrl = ($this.release.assets | Where-Object {$_.name -match "emby.windows.zip"}).browser_download_url
        }
    }

    getUpdate() {
        if (Test-Path $this.location) {
            try {
                if ([version]$this.release.tag_name -gt $this.localVersion) {
                    if (-not (test-path "$($this.location)\updates")) { 
                        (mkdir "$($this.location)\updates")
                    }
                    Invoke-WebRequest $this.assetUrl -OutFile "$($this.location)\updates\MBserver.zip" -UseBasicParsing
                    $this.release.tag_name > "$($this.location)\updates\MBserver.zip.ver"
                }
            } catch {
                if (Test-Path "$($this.location)\updates") {
                    (Remove-Item "$($this.location)\updates" -Recurse)
                }
            }
        }
    }

    install() {
        if (Test-Path "$($this.location)\Updates\MBserver.zip") {
            Stop-Service Emby
            Wait-Process embyserver -ErrorAction SilentlyContinue
            Wait-Process MediaBrowser.ServerApplication -ErrorAction SilentlyContinue
            if (Test-Path "$($this.location)\System.old") {
                Remove-Item "$($this.location)\System.old" -Recurse
            }
            if (Test-Path "$($this.location)\System") {
                Move-Item "$($this.location)\System" "$($this.location)\System.old"
            }
            if (-not (Test-Path "$($this.location)\System")) {
                
                Expand-7Zip "$($this.location)\Updates\MBserver.zip" "$($this.location)"
            }
            if (Test-Path "$($this.location)\System") {
                Remove-Item "$($this.location)\updates" -Recurse
            }
            Start-Service Emby
        }
    }

    installTask($scriptName) {
        Write-Progress "Installing Task"
        Start-Sleep 1
        if (-not (Test-Path "$($this.location)\updater")) {
            mkdir "$($this.location)\updater"
        }
        Copy-Item "$PSScriptRoot\$scriptName" "$($this.location)\updater\WindowsServiceUpdater.ps1" -Force
        if (Get-Command New-ScheduledTaskAction -ErrorAction SilentlyContinue) {
            $action = New-ScheduledTaskAction -Execute "Powershell.exe" `
            -Argument "-ExecutionPolicy Bypass -file `"$($this.location)\updater\WindowsServiceUpdater.ps1`""
            $trigger =  New-ScheduledTaskTrigger -Daily -At 4am
            Register-ScheduledTask -Action $action -Trigger $trigger -TaskName "Emby Service Updater" -Description "Emby Service Updater" -User "SYSTEM" -Force
        } else {
            start-Process "schtasks.exe" -ArgumentList "/create", "/sc DAILY", "/TN `"Emby Service Updater`"",
            "/RU SYSTEM", "/TR" ,"Powershell.exe", "-ExecutionPolicy Bypass -file `"$($this.location)\updater\WindowsServiceUpdater.ps1`"",
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
    $Updater.getUpdate()
    $Updater.install()
}