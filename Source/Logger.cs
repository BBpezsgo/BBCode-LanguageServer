using System.Diagnostics;
using LanguageCore;

namespace LanguageServer;

public class Logger : ILogger
{
    public static readonly Logger Instance = new();

    static string FormatMessage(object? message) => message switch
    {
        Exception ex => $"{ex.GetType().Name} {ex.Message}\n{ex.StackTrace}\n\n{FormatMessage(ex.InnerException)}".TrimEnd(),
        null => string.Empty,
        _ => message.ToString() ?? string.Empty,
    };

    public static void Error(string message) => OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)1, Message = message });
    public static void Warn(string message) => OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)2, Message = message });
    public static void Info(string message) => OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)3, Message = message });
    public static void Debug(string message) => OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)5, Message = message });
    public static void Trace(string message) => OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)6, Message = message });

    public static void Error(object? message) => Error(FormatMessage(message));
    public static void Warn(object? message) => Warn(FormatMessage(message));
    public static void Info(object? message) => Info(FormatMessage(message));
    public static void Debug(object? message) => Debug(FormatMessage(message));
    public static void Trace(object? message) => Trace(FormatMessage(message));

    public void Log(LogType level, string message)
    {
        switch (level)
        {
            case LogType.Normal:
                OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)3, Message = message });
                break;
            case LogType.Warning:
                OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)2, Message = message });
                break;
            case LogType.Error:
                OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)1, Message = message });
                break;
            case LogType.Debug:
                OmniSharpService.Instance?.Server?.Window.SendNotification(new LogMessageParams { Type = (MessageType)5, Message = message });
                break;
            default:
                throw new UnreachableException();
        }
    }
    public IDisposableProgress<float> Progress(LogType level) => VoidProgress<float>.Instance;
    public IDisposableProgress<string> Label(LogType level) => VoidProgress<string>.Instance;
}
