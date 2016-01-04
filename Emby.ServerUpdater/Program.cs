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
                string PackageName = "MBserver";
                Version highversion = GetServerVersion();
                string targetFilename = null;
                string sourceUrl = null;
                WebClient Client = new WebClient();
                var json = Client.DownloadString("http://www.mb3admin.com/admin/service/package/retrieveAll?name=" + PackageName);
                dynamic packages = JsonConvert.DeserializeObject(json);
                foreach (dynamic package in packages[0].versions)
                {

                    Version version = new Version(package.versionStr.ToString());

                    if (package.classification == GetUpdateLevel() && version >= highversion)
                    {
                        highversion = version;
                        targetFilename = package.targetFilename;
                        sourceUrl = package.sourceUrl;
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
                return XML[0].InnerText;
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
            if (Directory.Exists(GetServerProgramDataPath()))
            {
                Console.WriteLine("Creating Task");
                if (Directory.GetCurrentDirectory() != GetServerProgramDataPath() + "\\Updater")
                {
                    Directory.CreateDirectory(GetServerProgramDataPath() + "\\Updater\\");
                    File.Copy(Directory.GetCurrentDirectory() + "\\Emby.ServerUpdater.exe", GetServerProgramDataPath() + "\\Updater" + "\\Emby.ServerUpdater.exe", true);
                    File.Copy(Directory.GetCurrentDirectory() + "\\Newtonsoft.Json.dll", GetServerProgramDataPath() + "\\Updater" + "\\Newtonsoft.Json.dll", true);
                
            }
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