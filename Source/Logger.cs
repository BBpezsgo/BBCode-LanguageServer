using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using System;
using System.IO;

namespace ProgrammingLanguage.LanguageServer
{
    public class Logger
    {
        static ILogger Instance;

        static string _fileName;
        static string FileName
        {
            get
            {
                if (_fileName == null)
                { _fileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt"; }
                return _fileName;
            }
        }
        const string LOG_PATH = @"D:\Program Files\BBCodeProject\LanguageServer\";

        /*
        Proxy proxy;

        public void Attach(Connection connection)
        {
            if (connection == null)
            {
                proxy = null;
            }
            else
            {
                proxy = new Proxy(connection);
            }
        }
        void Send(MessageType type, string message)
        {
            this.proxy?.Window.LogMessage(new LogMessageParams
            {
                type = type,
                message = message
            });
        }
        */

        public static void Error(string message)
        {
            if (Directory.Exists(LOG_PATH))
            { File.AppendAllText($"{LOG_PATH}{FileName}", $"[{DateTime.Now:HH:mm:ss}] [ERROR] {message}"); }

            Instance?.Send(MessageType.Error, message);
        }

        public static void Warn(string message)
        {
            if (Directory.Exists(LOG_PATH))
            { File.AppendAllText($"{LOG_PATH}{FileName}", $"[{DateTime.Now:HH:mm:ss}] [WARN] {message}"); }

            Instance?.Send(MessageType.Warning, message);
        }

        public static void Info(string message)
        {
            if (Directory.Exists(LOG_PATH))
            { File.AppendAllText($"{LOG_PATH}{FileName}", $"[{DateTime.Now:HH:mm:ss}] [INFO] {message}"); }

            Instance?.Send(MessageType.Info, message);
        }

        public static void Log(string message)
        {
            if (Directory.Exists(LOG_PATH))
            { File.AppendAllText($"{LOG_PATH}{FileName}", $"[{DateTime.Now:HH:mm:ss}] [LOG] {message}"); }

            Instance?.Send(MessageType.Log, message);
        }

        internal static void Setup(ILogger logger)
        {
            Instance = logger;
        }
    }

    internal interface ILogger
    {
        void Send(MessageType type, string message);
    }
}
