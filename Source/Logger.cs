using System.IO;
using System.Text;

#pragma warning disable IDE0051 // Remove unused private members

namespace LanguageServer
{
    public class Logger
    {
        const string LOG_PATH = @"D:\Program Files\BBCodeProject\LanguageServer\";

        static string? _fileName;
        static string FileName
        {
            get
            {
                _fileName ??= $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt";
                return _fileName;
            }
        }

        const int MaxKindLength = 5;
        const int TimePrefixLength = 10;
        const int PrefixLength = TimePrefixLength + 1 + MaxKindLength + 3;

        static long PrevTime = 0;
        static readonly string EmptyTimePrefix = new(' ', TimePrefixLength);

        static bool IsNewTime(DateTime time)
        {
            long t = (long)time.TimeOfDay.TotalSeconds;
            if (t == PrevTime) return false;
            PrevTime = t;
            return true;
        }
        static string GetTimePrefix(DateTime time)
        {
            if (IsNewTime(time))
            { return $"[{time:HH:mm:ss}]"; }
            else
            { return EmptyTimePrefix; }
        }

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

        static void GetPrefix(StringBuilder builder, string kind)
        {
            DateTime time = DateTime.Now;
            if (IsNewTime(time))
            {
                builder.Append('[');
                builder.Append(time.ToString("HH:mm:ss"));
                builder.Append(']');
            }
            else
            {
                builder.Append(' ', 10);
            }

            builder.Append(' ');
            builder.Append('[');
            builder.Append(kind);
            builder.Append(']');

            if (kind.Length < MaxKindLength)
            { builder.Append(' ', MaxKindLength - kind.Length); }
        }

        static string IndentMessage(string message, int indent)
        {
            string[] lines = message.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            { lines[i] = new string(' ', indent) + lines[i]; }
            return string.Join('\n', lines).Trim();
        }

        static string GetMessage(string kind, string message)
        {
            StringBuilder builder = new();
            GetPrefix(builder, kind);
            builder.Append(' ');
            builder.Append(IndentMessage(message, PrefixLength));
            builder.Append(Environment.NewLine);
            return builder.ToString();
        }

        public static void Error(string message)
        {
            try
            {
                if (Directory.Exists(LOG_PATH))
                { File.AppendAllText($"{LOG_PATH}{FileName}", GetMessage("ERROR", message)); }
            }
            catch (IOException)
            { }

            OmniSharpService.Instance?.Server?.Window.LogError(message);
        }

        public static void Warn(string message)
        {
            try
            {
                if (Directory.Exists(LOG_PATH))
                { File.AppendAllText($"{LOG_PATH}{FileName}", GetMessage("WARN", message)); }
            }
            catch (IOException)
            { }

            OmniSharpService.Instance?.Server?.Window.LogWarning(message);
        }

        public static void Info(string message)
        {
            try
            {
                if (Directory.Exists(LOG_PATH))
                { File.AppendAllText($"{LOG_PATH}{FileName}", GetMessage("INFO", message)); }
            }
            catch (IOException)
            { }

            OmniSharpService.Instance?.Server?.Window.LogInfo(message);
        }

        public static void Log(string message)
        {
            try
            {
                if (Directory.Exists(LOG_PATH))
                { File.AppendAllText($"{LOG_PATH}{FileName}", GetMessage("LOG", message)); }
            }
            catch (IOException)
            { }

            OmniSharpService.Instance?.Server?.Window.Log(message);
        }
    }
}
