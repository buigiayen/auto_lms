using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LotusLMSClient.Helpers
{
    public static class AppLog
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly object LockObj = new object();
        private static readonly List<LogEntry> _history = new List<LogEntry>();
        private const int MaxHistory = 500;

        public static event EventHandler<LogEntry> OnLogAdded;

        public static IReadOnlyList<LogEntry> History => _history;

        static AppLog()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }
            }
            catch { }
        }

        public static void Info(string message, int itemIid = 0, int userIid = 0) => Log("INFO", message, itemIid, userIid);
        public static void Error(string message, int itemIid = 0, int userIid = 0) => Log("ERROR", message, itemIid, userIid);
        public static void Warning(string message, int itemIid = 0, int userIid = 0) => Log("WARN", message, itemIid, userIid);

        private static void Log(string level, string message, int itemIid, int userIid)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                ItemIid = itemIid,
                UserIid = userIid
            };

            lock (LockObj)
            {
                _history.Add(entry);
                if (_history.Count > MaxHistory) _history.RemoveAt(0);
            }

            WriteToFile(entry);
            
            // Raise event on UI thread if needed (the handler should handle marshalling)
            OnLogAdded?.Invoke(null, entry);
        }

        private static void WriteToFile(LogEntry entry)
        {
            try
            {
                string fileName;
                if (entry.ItemIid > 0 && entry.UserIid > 0)
                {
                    fileName = $"{entry.ItemIid}-{entry.UserIid}-{entry.Timestamp:dd-MM-yyyy}.log";
                }
                else
                {
                    fileName = $"app_{entry.Timestamp:dd-MM-yyyy}.log";
                }

                string path = Path.Combine(LogDirectory, fileName);
                string line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}";

                lock (LockObj)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static string[] ListAvailableLogs()
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return new string[0];
                return Directory.GetFiles(LogDirectory, "*.log");
            }
            catch { return new string[0]; }
        }

        public static string GetLogDirectory() => LogDirectory;

        public static string GetLogFilePath(int itemIid, int userIid)
        {
            string fileName = $"{itemIid}-{userIid}-{DateTime.Now:dd-MM-yyyy}.log";
            return Path.Combine(LogDirectory, fileName);
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public int ItemIid { get; set; }
        public int UserIid { get; set; }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] {Message}";
        }
    }
}
