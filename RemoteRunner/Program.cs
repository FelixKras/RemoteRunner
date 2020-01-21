using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteRunner
{
    public static class Settings
    {
        private static System.Configuration.Configuration config;

        static Settings()
        {
            KeepSettings();
        }

        public static IPEndPoint ServerIpe
        {
            get
            {
                IPAddress ipadr = IPAddress.Parse(config.AppSettings.Settings["ServerIP"].Value);
                int port = int.Parse(config.AppSettings.Settings["Port"].Value);
                IPEndPoint ipe = new IPEndPoint(ipadr, port);
                return ipe;
            }
        }
        public static IPAddress OwnIp
        {
            get
            {
                IPAddress ipadr;
                IPAddress.TryParse(config.AppSettings.Settings["OwnIP"].Value, out ipadr);
                return ipadr;
            }
        }
        public static int AppID
        {
            get
            {
                int appid = int.Parse(config.AppSettings.Settings["APPID"].Value);
                return appid;
            }
        }
        public static string ExeName
        {
            get
            {
                return config.AppSettings.Settings["ExeName"].Value;
                }
        }

        public static string Arguments
        {
            get
            {
                return config.AppSettings.Settings["Arguments"].Value;
            }
        }

        static void KeepSettings()
        {
            string appPath = Assembly.GetEntryAssembly().Location;
            FileInfo fi = new FileInfo(appPath);
            if (fi.DirectoryName != null)
            {
                string sAppFullPath = System.IO.Path.Combine(fi.DirectoryName, fi.Name + ".config");
                ExeConfigurationFileMap configFileMap = new ExeConfigurationFileMap();
                configFileMap.ExeConfigFilename = sAppFullPath;
                config = ConfigurationManager.OpenMappedExeConfiguration(configFileMap, ConfigurationUserLevel.None);
            }
        }
    }

    class Program
    {

        static void Main(string[] args)
        {
            AutoResetEvent areWait = new AutoResetEvent(false);
            string result = String.Empty;
            ExeHandler.ExecuteRemote(Settings.ExeName, Settings.Arguments, ref result, true, false);
            Comm.SendResult(result);
        }



    }

    static class ExeHandler
    {
        public static bool ExecuteRemote(string s_FileName, string i_Arguments, ref string Result, bool i_RedirectStandardOutput,
            bool i_RedirectStandardError, bool i_UseShellExecute = false)
        {
            bool bRes = false;
            Process process = new Process();

            process.StartInfo = new ProcessStartInfo()
            {
                FileName = s_FileName,
                Arguments = i_Arguments,
                RedirectStandardOutput = i_RedirectStandardOutput,
                RedirectStandardError = i_RedirectStandardError,
                UseShellExecute = i_UseShellExecute
            };

            try
            {
                bool succeeded = process.Start();
                process.WaitForExit();

                Result = process.StandardOutput.ReadToEnd();
                if (!succeeded)
                {
                    bRes = false;
                }
                else
                {
                    bRes = true;
                }
            }
            catch (Exception ex)
            {
                bRes = false;
            }

            return bRes;
        }
    }

    static class Comm
    {
        private static UdpClient udpclient;

        public static bool SendResult(string sResult)
        {
            bool bRes = false;
            try
            {
                if (udpclient == null)
                {
                    if (Settings.OwnIp != null)
                    {
                        udpclient = new UdpClient(new IPEndPoint(Settings.OwnIp, 0));
                    }
                    else
                    {
                        udpclient = new UdpClient();
                    }


                }

                string HEADER = "IAMNUMBER" + Settings.AppID + "#";
                string FOOTER = "#FOOTER";

                byte[] by1msg = Encoding.ASCII.GetBytes(HEADER + sResult + FOOTER);
                udpclient.Send(by1msg, by1msg.Length, Settings.ServerIpe);

                bRes = true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
                bRes = false;
            }

            return bRes;
        }

    }
}
