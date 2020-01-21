using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServerApp
{
    public class LogWriter : IDisposable
    {
        private ConcurrentQueue<Log> logQueue;
        private string logDir;
        private string logFile;
        private int maxLogAge = 2; //seconds
        private int queueSize = 50;
        private DateTime LastFlushed = DateTime.Now;
        private readonly object oLocker = new object();
        private Thread thrWriter;
        private Thread thrWaker;
        private bool isTableFormat;
        private bool bStopWriting;
        private AutoResetEvent waitHandle;

        private static ManualResetEvent stopHandle = new ManualResetEvent(false);


        private static readonly LogWriter instance = new LogWriter(new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).FullName + "\\Logs\\GeneralLogFile", false);

        static LogWriter() { }
        private LogWriter() { }

        public static LogWriter Instance
        {
            get { return instance; }
        }

        public LogWriter(string FileName, bool isTable, bool isTimeStampInName = true)
        {
            FileInfo fi = new FileInfo(FileName);
            isTableFormat = isTable;
            logDir = fi.Directory.FullName;
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            logQueue = new ConcurrentQueue<Log>();
            string Ext;

            if (isTableFormat)
            {
                Ext = ".csv";
            }
            else
            {
                Ext = ".log";
            }
            if (fi.Extension != "")
            {
                if (isTimeStampInName)
                {
                    logFile = fi.Directory.FullName + "\\" + fi.Name.Replace(fi.Extension, "") + "_" + DateTime.Now.ToString("ddMMyyyy_HHmmss") + Ext;
                }
                else
                {
                    logFile = fi.Directory.FullName + "\\" + fi.Name.Replace(fi.Extension, "") + Ext;
                }
            }
            else
            {
                if (isTimeStampInName)
                {
                    logFile = fi.Directory.FullName + "\\" + fi.Name + "_" + DateTime.Now.ToString("ddMMyyyy_HHmmss") +
                              Ext;
                }
                else
                {
                    logFile = fi.Directory.FullName + "\\" + fi.Name + Ext;
                }
            }
            StartWritingTask();

            if (!isTableFormat)
            {
                WriteToLog("Started on " + Environment.MachineName + " at: " + DateTime.Now, true);
            }
        }


        private void StartWritingTask()
        {
            bStopWriting = false;
            waitHandle = new AutoResetEvent(false);

            thrWriter = new Thread(FlushLog);
            thrWriter.Name = new FileInfo(logFile).Name + "_Writer_Thread";
            thrWriter.IsBackground = true;
            thrWriter.Priority = ThreadPriority.BelowNormal;
            thrWriter.Start();

        }

        /// <summary>
        /// The method that writes to the log file
        /// </summary>
        /// <param name="message">The message to write to the log</param>
        /// <param name="isTimeTag">Whether to add time tag to the message</param>
        public void WriteToLog(string message, bool isTimeTag = true)
        {
            Log logEntry = new Log(message, isTimeTag);
            bool bIsLockTaken = false;

            logQueue.Enqueue(logEntry);

            FlushIfNeededWithLock(ref bIsLockTaken);

        }

        private void FlushIfNeededWithLock(ref bool bIsLockTaken)
        {
            try
            {
                bIsLockTaken = Monitor.TryEnter(oLocker, new TimeSpan(TimeSpan.TicksPerMillisecond / 2));

                // If we have reached the Queue Size then flush the Queue
                if (CheckIfFlushNeeded() && bIsLockTaken)
                {
                    waitHandle.Set();
                }
            }
            finally
            {
                if (bIsLockTaken)
                {
                    Monitor.Exit(oLocker);
                    bIsLockTaken = false;
                }
            }
        }

        private bool CheckIfFlushNeeded()
        {
            TimeSpan logAge = DateTime.Now - LastFlushed;
            if (logAge.TotalSeconds >= maxLogAge || logQueue.Count >= queueSize)
            {
                LastFlushed = DateTime.Now;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Flushes the Queue to the physical log file
        /// </summary>
        private void FlushLog()
        {
            var WaitHandlesArr = new WaitHandle[] { waitHandle, stopHandle };
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            FileStream fs;
            StreamWriter log;
            bool bIsLockTaken = false;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                while (!bStopWriting)
                {
                    WaitHandle.WaitAny(WaitHandlesArr, TimeSpan.FromMilliseconds(maxLogAge * 1000));

                    // This could be optimised to prevent opening and closing the file for each write
                    WriteWithLock(ref bIsLockTaken);
                }
                //postmortum logging
                if (bStopWriting)
                {
                    WriteWithLock(ref bIsLockTaken);
                }
            }
            finally
            {
                Thread.CurrentThread.Priority = ThreadPriority.Normal;
            }
        }

        private bool WriteWithLock(ref bool bIsLockTaken)
        {
            FileStream fs;
            StreamWriter log;
            Log entry;


            if (logQueue.Count == 0)
            {
                return bIsLockTaken;
            }

            bIsLockTaken = Monitor.TryEnter(oLocker, -1);
            if (!(new DirectoryInfo(new FileInfo(logFile).Directory.FullName)).Exists)
            {
                Directory.CreateDirectory(new FileInfo(logFile).Directory.FullName);
            }
            using (fs = File.Open(logFile, FileMode.Append, FileAccess.Write))
            {
                using (log = new StreamWriter(fs))
                {
                    try
                    {

                        while (logQueue.Count > 0)
                        {
                            if (logQueue.TryDequeue(out entry))
                            {
                                log.WriteLine((string)entry.ToString());
                            }
                        }
                    }
                    finally
                    {
                        if (bIsLockTaken)
                        {
                            Monitor.Exit(oLocker);
                            bIsLockTaken = false;
                        }
                    }
                }
            }
            return bIsLockTaken;
        }

        //TODO:check how to improve dispose
        public void Dispose()
        {
            // Shitty design: i release any waiting thread with first stophandle, and then the main with the seccond

            try
            {
                while (this.thrWriter.IsAlive)
                {
                    bStopWriting = true;
                    stopHandle.Set();
                }
                if (!isTableFormat)
                {
                    WriteToLog("Writing stopped" + DateTime.Now, true);
                }


                FlushLog();

                waitHandle.Close();
            }
            catch
            {

            }


        }

        /// <summary>
        /// A Log class to store the message and the Date and Time the log entry was created
        /// </summary>
        class Log
        {
            public string Message { get; set; }
            public string LogTime { get; set; }
            public string LogDate { get; set; }
            public bool TimeTag { get; set; }

            public Log(string message, bool TimeTag)
            {
                LogTime = DateTime.Now.ToString("HH:mm:ss.fff");
                LogDate = DateTime.Now.ToString("yyyy-MM-dd");
                Message = message;
                this.TimeTag = TimeTag;
            }

            public override string ToString()
            {
                if (this.TimeTag)
                {
                    return string.Format("{0}:\t{1}", this.LogTime, this.Message);
                }
                else
                {
                    return string.Format(this.Message);
                }
            }


        }
    }
}
