using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ServerApp
{
    public static class Settings
    {
        private static System.Configuration.Configuration config;

        static Settings()
        {
            KeepSettings();
        }
       

        public static int Port
        {
            get
            {
                int appid = int.Parse(config.AppSettings.Settings["ListeningPort"].Value);
                return appid;
            }
        }
        public static string LogFile
        {
            get
            {
                return config.AppSettings.Settings["LogFile"].Value;

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
        private static LogWriter log;
        private static UdpClient udpclient;
        static void Main(string[] args)
        {
            AutoResetEvent are = new AutoResetEvent(false);
            log=new LogWriter(Settings.LogFile,false,true);
            udpclient=new UdpClient(new IPEndPoint(Settings.OwnIp, Settings.Port) );
            IPEndPoint remote=new IPEndPoint(0,0);

            Console.WriteLine("Starting listening to clients. Close when done");

            udpclient.BeginReceive(new AsyncCallback(onMessageReceive), udpclient);

            while (true)
            {
                are.WaitOne(TimeSpan.FromSeconds(2));
                Console.WriteLine("Waiting for message from client");
            }
        }

        private static void onMessageReceive(IAsyncResult result)
        {
            UdpClient socket = result.AsyncState as UdpClient;
            IPEndPoint source = new IPEndPoint(0,0);

            if (socket != null)
            {
                byte[] by1Message = socket.EndReceive(result, ref source);
                ParseAndLogMessage(by1Message);
                socket.BeginReceive(new AsyncCallback(onMessageReceive), socket);
            }
            
            
        }

        private static void ParseAndLogMessage(byte[] by1Message)
        {
            Regex rgxClientNumber=new Regex(@"(?<=IAMNUMBER)\d+(?=#)");
            Regex rgxMessage = new Regex(@"(?<=#)0[xX][\da-fA-F]+");

            string sMessage = Encoding.ASCII.GetString(by1Message);
            if (sMessage.Contains("IAMNUMBER") && sMessage.Contains("FOOTER"))
            {
                int iClientNumber = int.Parse(rgxClientNumber.Match(sMessage).Value);
                string sCleanMessage = rgxMessage.Match(sMessage).Value;
                Console.WriteLine("Received answer from client number " + iClientNumber);
                log.WriteToLog("Received answer from client number "+iClientNumber+" : "+sCleanMessage);
            }

        }
    }



}
