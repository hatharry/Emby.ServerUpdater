# Emby.ServerUpdater
Updates Emby when running as a service. Runs from any account including the system account. Can be executed from a scheduled task.

Options include:

Emby.ServerUpdater.exe

(Default) Will download update and restart emby only if an update is available and if ffmpeg is not running.

Emby.ServerUpdater.exe -download

Will download update only if available.

Emby.ServerUpdater.exe -restart

 Will restart service only if ffmpeg is not running.

Emby.ServerUpdater.exe -restart -download

 Will restart service only if ffmpeg is not running and download update only if available.

Emby.ServerUpdater.exe -createtask

 Will copy files to emby folder, create a windows task to run at 4am and set service startup to auto.

Update level can be changed using the server manager's automatic updates.
