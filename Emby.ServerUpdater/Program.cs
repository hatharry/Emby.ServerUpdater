using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using System.IO;
using Microsoft.Win32;
using System.Xml;
using System.ServiceProcess;
using System.Diagnostics;

namespace Emby.ServerUpdater
{
    class Program
    {
        private Version localVersion;
        private Version highestVersion;
        private string sourceUrl;
        private string targetFilename = "Mbserver.zip";
        private string sourceFilename = "emby.windows.zip";
        private string programDataPath;
        private string updateLevel;
        private ServiceController embyService;
        private string embyServiceName = "Emby";
        private Process cmd;
        private WebClient wClient;

        public Program() {
            getServerProgramDataPath();
            getServerVersion();
            getUpdateLevel();
            getRemoteVersion();

        }

        private void getRemoteVersion()
        {
            highestVersion = new Version();
            wClient = new WebClient();
            wClient.Headers.Add("user-agent", "Emby / 3.0");
            string json = wClient.DownloadString("https://api.github.com/repos/mediabrowser/emby/releases");
            dynamic packages = JsonConvert.DeserializeObject(json);
            foreach (dynamic package in packages)
            {
                Version version = new Version(package.tag_name.ToString());
                if ((package.name.ToString().EndsWith(updateLevel, StringComparison.OrdinalIgnoreCase) || (!(bool)package.prerelease && updateLevel == "Release")) && version >= highestVersion)
                {
                    foreach (dynamic asset in package.assets)
                    {
                        if (asset.name == sourceFilename)
                        {
                            sourceUrl = asset.browser_download_url;
                            highestVersion = version;
                        }
                    }   
                }
            }
            Console.WriteLine(string.Format("sourceUrl is {0}", sourceUrl));
            Console.WriteLine(string.Format("highestVersion is {0}", highestVersion));
        }

        private void getServerProgramDataPath()
        {
            if (Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\Emby", "ImagePath", null) != null)
            {
                programDataPath = Path.GetDirectoryName(Path.GetDirectoryName((Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\Emby", "ImagePath", null).ToString()).Replace("\"", "").Split(null).First()));
                Console.WriteLine(string.Format("programDataPath is {0}", programDataPath));
            }
        }

        private void getServerVersion()
        {
            if (Directory.Exists(programDataPath))
            {
                localVersion = new Version(FileVersionInfo.GetVersionInfo(programDataPath + "\\system\\MediaBrowser.ServerApplication.exe").ProductVersion);
                Console.WriteLine(string.Format("localVersion is {0}", localVersion));
            }
        }

        private void getUpdateLevel()
        {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(programDataPath + "\\config\\System.xml");
                XmlNodeList XML = xmlDoc.GetElementsByTagName("SystemUpdateLevel");
                if (XML[0].InnerText == "Release")
                {
                    updateLevel = "Release";
                    Console.WriteLine(string.Format("updateLevel is {0}", updateLevel));
                }
                else if (XML[0].InnerText == "Beta")
                {
                    updateLevel = "-beta";
                    Console.WriteLine(string.Format("updateLevel is {0}", updateLevel));
                }
                else if (XML[0].InnerText == "Dev")
                {
                    updateLevel = "-dev";
                    Console.WriteLine(string.Format("updateLevel is {0}", updateLevel));
                }
        }

        public bool downloadPackage()
        {
            if (localVersion < highestVersion)
            {
                try
                {
                    Console.WriteLine("Downloading Package");
                    Directory.CreateDirectory(programDataPath + "\\Updates\\");
                    wClient = new WebClient();
                    wClient.Headers.Add("user-agent", "Emby / 3.0");
                    wClient.DownloadFile(sourceUrl, programDataPath + "\\Updates\\" + targetFilename);
                    File.WriteAllText(programDataPath + "\\Updates\\" + targetFilename + ".ver", highestVersion.ToString());
                    return true;
                }
                catch
                {
                    Directory.Delete(programDataPath + "\\Updates\\", true);
                    return false;
                }
            }
            return false;
        }

        public void stopService()
        {
            embyService = new ServiceController(embyServiceName);
            if (embyService != null && embyService.Status.Equals(ServiceControllerStatus.Running) && Process.GetProcessesByName("ffmpeg").Length == 0)
            {
                Console.WriteLine("Stopping Service");
                embyService.Stop();
                while (Process.GetProcessesByName("MediaBrowser.ServerApplication").Length != 0)
                {
                    Console.WriteLine("Waiting for process to close");
                    continue;
                }
            }
        }

        public void startService()
        {
            embyService = new ServiceController(embyServiceName);
            if (embyService != null && embyService.Status.Equals(ServiceControllerStatus.Stopped) && Process.GetProcessesByName("ffmpeg").Length == 0)
            {
                try
                {
                    Console.WriteLine("Starting Service");
                    embyService.Start();
                }
                catch
                {
                    Console.WriteLine("Updating Emby");
                }
            }
        }

        public void restartService() {
            stopService();
            startService();
        }

        public void createTask()
        {
            if (Directory.Exists(programDataPath))
            {
                Console.WriteLine("Creating Task");
                if (AppDomain.CurrentDomain.BaseDirectory != programDataPath + "\\Updater")
                {
                    Directory.CreateDirectory(programDataPath + "\\Updater\\");
                    File.Copy(AppDomain.CurrentDomain.BaseDirectory + "\\Emby.ServerUpdater.exe", programDataPath + "\\Updater" + "\\Emby.ServerUpdater.exe", true);
                    File.Copy(AppDomain.CurrentDomain.BaseDirectory + "\\Newtonsoft.Json.dll", programDataPath + "\\Updater" + "\\Newtonsoft.Json.dll", true);
                }
                cmd = new Process();
                cmd.StartInfo.FileName = "c:\\windows\\system32\\schtasks.exe";
                cmd.StartInfo.Arguments = "/create /sc DAILY /TN \"Emby Service Updater\" /RU SYSTEM /TR " + programDataPath + "\\Updater" + "\\Emby.ServerUpdater.exe" + " /ST 04:00 /F";
                cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                cmd.Start();  
            }
        }

        public void serviceToAuto()
        {
            Console.WriteLine("Emby Auto Startup");
            cmd = new Process();
            cmd.StartInfo.FileName = "c:\\windows\\system32\\sc.exe";
            cmd.StartInfo.Arguments = "config " + embyServiceName + " start=auto";
            cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            cmd.Start();
        }

        static void Main(string[] args)
        {
            Program program = new Program();
            if (args.Contains("-download"))
            {
                program.downloadPackage();
            }
            if (args.Contains("-restart"))
            {
                program.restartService();
            }
            if (args.Contains("-createtask"))
            {
                program.createTask();
                program.serviceToAuto();
            }
            if(args.Length == 0 && program.downloadPackage())
            {
                program.restartService();
            }
        }
    }
}