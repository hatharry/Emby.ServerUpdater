using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
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
        static public Tuple<Version, string, string> GetVersion()
        {
            try {
                Version highversion = GetServerVersion();
                string targetFilename = "Mbserver.zip";
                string sourceFilename = "emby.windows.zip";
                string sourceUrl = null;
                WebClient Client = new WebClient();
                Client.Headers.Add("user-agent", "Emby / 3.0");
                var json = Client.DownloadString("https://api.github.com/repos/mediabrowser/emby/releases");
                dynamic packages = JsonConvert.DeserializeObject(json);
                foreach (dynamic package in packages)
                {
                    Version version = new Version(package.tag_name.ToString());
                    if (package.target_commitish == GetUpdateLevel() && version >= highversion)
                    {
                        foreach (dynamic asset in package.assets)
                        {
                            if (asset.name == sourceFilename)
                            {
                                sourceUrl = asset.browser_download_url;
                                highversion = version;
                            }
                        }   
                    }
                }
                return Tuple.Create(highversion, sourceUrl, targetFilename);
            }
            catch
            {
                return null;
            }
        }
        public static string GetServerProgramDataPath()
        {
            if (Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\Emby", "ImagePath", null) != null)
            {
                return Path.GetDirectoryName(Path.GetDirectoryName(((string)Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\Emby", "ImagePath", null)).Replace("\"", "").Split(null).First()));
            }
            else if (Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\MediaBrowser", "ImagePath", null) != null)
            {
                return Path.GetDirectoryName(Path.GetDirectoryName(((string)Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\MediaBrowser", "ImagePath", null)).Replace("\"", "").Split(null).First()));
            }
            return null;
        }
        public static Version GetServerVersion()
        {
            if (Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\Emby", "ImagePath", null) != null)
            {
                Version serverver = new Version(FileVersionInfo.GetVersionInfo(((string)Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\Emby", "ImagePath", null)).Replace("\"", "").Split(null).First()).ProductVersion);
                return serverver;
            }
            else if (Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\MediaBrowser", "ImagePath", null) != null)
            {
                Version serverver = new Version(FileVersionInfo.GetVersionInfo(((string)Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\services\\MediaBrowser", "ImagePath", null)).Replace("\"", "").Split(null).First()).ProductVersion);
                return serverver;
            }
            return null;
        }
        public static string GetUpdateLevel()
        {
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(GetServerProgramDataPath() + "\\config\\System.xml");
                XmlNodeList XML = xmlDoc.GetElementsByTagName("SystemUpdateLevel");
                if (XML[0].InnerText != "Release")
                {
                    return XML[0].InnerText.ToLower();
                }
                else
                {
                    return "master";
                }
            }
            catch
            {
                return null;
            }
        }
        public static void DownloadPackage()
        {
            try
            {
                Console.WriteLine("Downloading Package");
                Directory.CreateDirectory(GetServerProgramDataPath() + "\\Updates\\");
                WebClient Client = new WebClient();
                Client.Headers.Add("user-agent", "Emby / 3.0");
                Client.DownloadFile(GetVersion().Item2, GetServerProgramDataPath() + "\\Updates\\" + GetVersion().Item3);
                File.WriteAllText(GetServerProgramDataPath() + "\\Updates\\" + GetVersion().Item3 + ".ver", GetVersion().Item1.ToString());
            }
            catch
            {
                Console.WriteLine("Download Failed");
            }
        }
        public static void StopService()
        {
            string[] ServiceNames = { "MediaBrowser", "Emby" };
            foreach (string ServiceName in ServiceNames)
            {
                ServiceController service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName);
                if (service != null && service.Status.Equals(ServiceControllerStatus.Running) && Process.GetProcessesByName("ffmpeg").Length == 0)
                {
                    Console.WriteLine("Stopping Service");
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped);
                }
            }
        }
        public static void StartService()
        {
            string[] ServiceNames = { "MediaBrowser", "Emby" };
            foreach (string ServiceName in ServiceNames)
            {
                ServiceController service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName);
                if (service != null && service.Status.Equals(ServiceControllerStatus.Stopped) && Process.GetProcessesByName("ffmpeg").Length == 0)
                {
                    Console.WriteLine("Starting Service");
                    try
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                    catch
                    {
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                }
            }
        }
        public static void CreateTask()
        {
            Console.WriteLine(GetServerProgramDataPath());
            if (Directory.Exists(GetServerProgramDataPath()))
            {
                Console.WriteLine("Creating Task");
                if (Directory.GetCurrentDirectory() != GetServerProgramDataPath() + "\\Updater")
                {
                    Directory.CreateDirectory(GetServerProgramDataPath() + "\\Updater\\");
                }
                File.Copy(AppDomain.CurrentDomain.BaseDirectory + "\\Emby.ServerUpdater.exe", GetServerProgramDataPath() + "\\Updater" + "\\Emby.ServerUpdater.exe", true);
                File.Copy(AppDomain.CurrentDomain.BaseDirectory + "\\Newtonsoft.Json.dll", GetServerProgramDataPath() + "\\Updater" + "\\Newtonsoft.Json.dll", true);
                Process cmd = new Process();
                cmd.StartInfo.FileName = "c:\\windows\\system32\\schtasks.exe";
                cmd.StartInfo.Arguments = "/create /sc DAILY /TN \"Emby Service Updater\" /RU SYSTEM /TR " + GetServerProgramDataPath() + "\\Updater" + "\\Emby.ServerUpdater.exe" + " /ST 04:00 /F";
                cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                cmd.Start();  
            }
        }
        public static void ServiceToAuto()
        {
            string[] ServiceNames = { "MediaBrowser", "Emby" };
            foreach (string ServiceName in ServiceNames)
            {
                ServiceController service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName);
                if (service != null)
                {
                    Console.WriteLine(ServiceName + " Auto Startup");
                    Process cmd = new Process();
                    cmd.StartInfo.FileName = "c:\\windows\\system32\\sc.exe";
                    cmd.StartInfo.Arguments = "config " + ServiceName + " start=auto";
                    cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    cmd.Start();
                }
            }
        }
static void Main(string[] args)
        {
            if (args.Contains("-download") && GetVersion() != null && GetServerVersion() < GetVersion().Item1)
            {
                DownloadPackage();
            }
            else if (args.Contains("-restart") && GetServerVersion() != null)
            {
                StopService();
                StartService();
            }
            if (args.Contains("-createtask"))
            {
                CreateTask();
                ServiceToAuto();
            }
            else if(GetServerVersion() != null && GetVersion() != null && GetServerVersion() < GetVersion().Item1)
            {
                DownloadPackage();
                StopService();
                StartService();
            }
        }
    }
}