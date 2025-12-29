using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Inno.Core.Logging;

public static class Log
{
    private const string C_DEFAULT_CATEGORY = "Unknown";
    private const string C_LOG_ASSEMBLY_KEY = "Inno.AssemblyGroup";

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Debug(string message, params object[]? args)
        => Write(LogLevel.Debug, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Info(string message, params object[]? args)
        => Write(LogLevel.Info, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Warn(string message, params object[]? args)
        => Write(LogLevel.Warn, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Error(string message, params object[]? args)
        => Write(LogLevel.Error, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fatal(string message, params object[]? args)
        => Write(LogLevel.Fatal, message, args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Write(LogLevel level, string message, params object[]? args)
    {
        string msg = (args == null || args.Length == 0) ? message : string.Format(message, args);

        var frame = new StackTrace(skipFrames: 2, fNeedFileInfo: true).GetFrame(0);
        var method = frame?.GetMethod();
        var callerType = method?.DeclaringType;

        var file = frame?.GetFileName() ?? C_DEFAULT_CATEGORY;
        var line = frame?.GetFileLineNumber() ?? 0;

        string category = callerType?.Name ?? Path.GetFileNameWithoutExtension(file);
        LogSource source = callerType == null ? LogSource.None : ParseLogSourceFromAssembly(callerType.Assembly);
        
        LogManager.Dispatch(new LogEntry(level, source, category, msg, file, line));
    }
    
    private static LogSource ParseLogSourceFromAssembly(Assembly asm)
    {
        foreach (var meta in asm.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (meta.Key == C_LOG_ASSEMBLY_KEY && Enum.TryParse<LogSource>(meta.Value, out var source))
            {
                return source;
            }
        }

        return LogSource.None;
    }
}