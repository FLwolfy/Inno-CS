using System.Diagnostics;

namespace Inno.Core.Logging;

public static class Log
{
    private const string C_DEFAULT_CATEGORY = "Unknown";

    [Conditional("DEBUG")]
    public static void Debug(string message, params object[]? args)
        => Write(LogLevel.Debug, message, args);
    
    public static void Info(string message, params object[]? args)
        => Write(LogLevel.Info, message, args);

    public static void Warn(string message, params object[]? args)
        => Write(LogLevel.Warn, message, args);

    public static void Error(string message, params object[]? args)
        => Write(LogLevel.Error, message, args);

    public static void Fatal(string message, params object[]? args)
        => Write(LogLevel.Fatal, message, args);

    private static void Write(LogLevel level, string message, params object[]? args)
    {
        string msg = (args == null || args.Length == 0) ? message : string.Format(message, args);

        var frame = new StackTrace(skipFrames: 2, fNeedFileInfo: true).GetFrame(0);
        var method = frame?.GetMethod();
        var file = frame?.GetFileName() ?? C_DEFAULT_CATEGORY;
        var line = frame?.GetFileLineNumber() ?? 0;

        string category = method?.DeclaringType?.Name ?? Path.GetFileNameWithoutExtension(file);

        LogManager.Dispatch(new LogEntry(level, category, msg, file, line));
    }
}