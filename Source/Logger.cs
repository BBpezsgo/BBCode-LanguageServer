namespace LanguageServer;

static class Logger
{
    public static void Error(string message) => OmniSharpService.Instance?.Server?.Window.LogError(message);
    public static void Warn(string message) => OmniSharpService.Instance?.Server?.Window.LogWarning(message);
    public static void Info(string message) => OmniSharpService.Instance?.Server?.Window.LogInfo(message);
    public static void Log(string message) => OmniSharpService.Instance?.Server?.Window.Log(message);
}
