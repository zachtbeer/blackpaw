namespace Blackpaw.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static LogLevel _minLevel = LogLevel.Warning;
    private static readonly object _lock = new();

    public static LogLevel MinLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message) => Log(LogLevel.Error, message);

    public static void Warning(string message, Exception ex) => Log(LogLevel.Warning, $"{message}: {ex.Message}");
    public static void Error(string message, Exception ex) => Log(LogLevel.Error, $"{message}: {ex.Message}");

    public static void Log(LogLevel level, string message)
    {
        if (level < _minLevel)
        {
            return;
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpperInvariant().PadRight(7);
        var formatted = $"[{timestamp}] [{levelStr}] {message}";

        lock (_lock)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Debug => ConsoleColor.Gray,
                _ => originalColor
            };

            Console.Error.WriteLine(formatted);
            Console.ForegroundColor = originalColor;
        }
    }
}
