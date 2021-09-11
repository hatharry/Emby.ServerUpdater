param([switch]$InstallTask, [switch]$UninstallTask, [switch]$UpdateScript, [string]$ApiKey, [string]$ServerUrl)
#requires -runasadministrator
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Class EmbyServiceUpdater {

    [string]$location
    [object]$release
    [string]$assetUrl
    [string]$serviceName
    [string]$serverUrl
    [string]$apiKey
    [object]$serverInfo

    EmbyServiceUpdater([string]$serverUrl, [string]$apiKey) {
        $this.serverUrl = $serverUrl
        $this.apiKey = $apiKey
        $this.GetLocation()
    }

    GetServiceName() {
        $this.serviceName = (Get-Service | Where-Object { $_.name -match "emby" } | Select-Object -first 1).name
        if ($this.serviceName.Length -eq 0) {
            throw "can not find emby service"
        }
    }

    GetServerInfo() {
        $this.serverInfo = Invoke-RestMethod "$($this.serverUrl)/emby/System/Info?api_key=$($this.apiKey)"
    }

    GetLatestRelease() {
        $releases = Invoke-RestMethod "https://api.github.com/repos/mediabrowser/Emby.Releases/releases" -UseBasicParsing
        $preRelease = $this.serverInfo.SystemUpdateLevel -ne "Release"
        $this.release = ($releases | Where-Object { $_.prerelease -eq $preRelease } | Select-Object -first 1) 
    }

    GetLocation() {
        $this.location = (Get-Item (Get-Process embyserver | Select-Object -first 1).Path).Directory.Parent.FullName
    }

    GetAssetUrl() {
        if ([Environment]::Is64BitOperatingSystem) {
            $this.assetUrl = ($this.release.assets | Where-Object { $_.name -match "embyserver-win-x64" } | Select-Object -first 1).browser_download_url
        }
        else {
            $this.assetUrl = ($this.release.assets | Where-Object { $_.name -match "embyserver-win-x86" } | Select-Object -first 1).browser_download_url
        }
    }

    GetUpdate() {
        $this.GetServiceName()
        $this.GetServerInfo()
        $this.GetLatestRelease()
        try {
            if ([Version]$this.release.tag_name -gt [Version]$this.serverInfo.Version) {
                if (-not (test-path "$($this.location)\updates")) { 
                    (mkdir "$($this.location)\updates")
                }
                $this.GetAssetUrl()
                Invoke-WebRequest $this.assetUrl -OutFile "$($this.location)\updates\MBserver.zip" -UseBasicParsing
                $this.release.tag_name > "$($this.location)\updates\MBserver.zip.ver"
            }
            else {
                Write-Progress "Emby is up to date $($this.serverInfo.Version)"
                Start-Sleep 1
                if ($this.serverInfo.HasPendingRestart) {
                    Write-Progress "Restarting emby"
                    Start-Sleep 1
                    Restart-Service $($this.serviceName)
                }
            }
        }
        catch {
            if (Test-Path "$($this.location)\updates") {
                (Remove-Item "$($this.location)\updates" -Recurse)
            }
        }
        
    }

    Install() {
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
                    Install-PackageProvider -Name NuGet -Force
                    Install-Module -Name 7Zip4Powershell -Force
                    Import-Module -name 7Zip4Powershell
                    Expand-7Zip "$($this.location)\Updates\MBserver.zip" "$($this.location)"
                }
                catch {
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

    InstallTask($scriptName) {
        Write-Progress "Installing Task"
        Start-Sleep 1
        if (-not (Test-Path "$($this.location)\updater")) {
            mkdir "$($this.location)\updater"
        }
        Copy-Item "$PSScriptRoot\$scriptName" "$($this.location)\updater\EmbyServiceUpdater.ps1" -Force
        if (Get-Command New-ScheduledTaskAction -ErrorAction SilentlyContinue) {
            $action = New-ScheduledTaskAction -Execute "Powershell.exe" `
                -Argument "-ExecutionPolicy Bypass -file `"$($this.location)\updater\EmbyServiceUpdater.ps1`" -ApiKey $($this.apiKey) -ServerUrl $($this.serverUrl)"
            $trigger = New-ScheduledTaskTrigger -Daily -At 4am
            Register-ScheduledTask -Action $action -Trigger $trigger -TaskName "Emby Service Updater" -Description "Emby Service Updater" -User "SYSTEM" -Force
        }
        else {
            start-Process "schtasks.exe" -ArgumentList "/create", "/sc DAILY", "/TN `"Emby Service Updater`"",
            "/RU SYSTEM", "/TR" , "Powershell.exe", "-ExecutionPolicy Bypass -file `"$($this.location)\updater\EmbyServiceUpdater.ps1`" -ApiKey $($this.apiKey) -ServerUrl $($this.serverUrl)",
            "/ST 04:00", "/F" -Wait
        }
    }

    UninstallTask() {
        Write-Progress "Uninstalling Task"
        Start-Sleep 1
        if (Get-Command Unregister-ScheduledTask -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName "Emby Service Updater" -Confirm:$false
        }
        else {
            start-Process "schtasks.exe" -ArgumentList "/Delete", "/TN `"Emby Service Updater`"", "/F" -Wait
        }
        if (Test-Path "$($this.location)\updater") {
            Remove-Item "$($this.location)\updater" -Recurse
        }
    }
    
    UpdateScript() {
        $gitStr = [string](Invoke-RestMethod "https://raw.githubusercontent.com/hatharry/Emby.ServerUpdater/master/EmbyServiceUpdater.ps1" -UseBasicParsing)
        $fileStr = [string](Get-Content "$($this.location)\updater\EmbyServiceUpdater.ps1" -Raw)
        if ($fileStr.GetHashCode() -ne $gitStr.GetHashCode()) {
            Write-Progress "Script Updated"
            Start-Sleep 1
            $gitStr | Set-Content "$($this.location)\updater\EmbyServiceUpdater.ps1" -NoNewline
        }
    }
}


$Updater = [EmbyServiceUpdater]::new($ServerUrl, $ApiKey)
if ($installTask) {
    $Updater.InstallTask($MyInvocation.MyCommand.Name)
}
elseif ($uninstallTask) {
    $Updater.UninstallTask()
}
elseif ($UpdateScript) {
    $Updater.UpdateScript()
}
else {
    $Updater.GetUpdate()
    $Updater.Install()
}
