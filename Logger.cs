using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BBCodeLanguageServer
{
    internal class Logger
    {
        static ILogger Instance;
        
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

        public static void Error(string message) => Instance?.Send(MessageType.Error, message);
        public static void Warn(string message) => Instance?.Send(MessageType.Warning, message);
        public static void Info(string message) => Instance?.Send(MessageType.Info, message);
        public static void Log(string message) => Instance?.Send(MessageType.Log, message);

        internal static void Setup(ILogger logger) => Instance = logger;
    }

    internal interface ILogger
    {
        void Send(MessageType type, string message);
    }
}
