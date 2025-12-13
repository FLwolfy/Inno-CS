using System.Collections.Concurrent;

namespace Inno.Core.Logging;

public static class LogManager
{
    private static readonly List<ILogSink> SINKS = new();
    private static readonly ConcurrentQueue<LogEntry> QUEUE = new();
    private static Thread m_workerThread = null!;
    private static bool m_running = true;

    public static void Initialize()
    {
        m_workerThread = new Thread(ProcessQueue) { IsBackground = true };
        m_workerThread.Start();
    }

    public static void RegisterSink(ILogSink sink)
    {
        SINKS.Add(sink);
    }

    public static void Dispatch(LogEntry entry)
    {
        QUEUE.Enqueue(entry);
    }

    private static void ProcessQueue()
    {
        while (m_running)
        {
            while (QUEUE.TryDequeue(out var entry))
            {
                foreach (var sink in SINKS)
                {
                    try
                    {
                        sink.Receive(entry);
                    }
                    catch
                    {
                        /* Dismiss Single Sink Exception */
                    }
                }
            }
            Thread.Sleep(10);
        }
    }

    public static void Shutdown()
    {
        m_running = false;
        m_workerThread.Join();

        // flush remaining entries
        while (QUEUE.TryDequeue(out var entry))
        {
            foreach (var sink in SINKS)
            {
                try
                {
                    sink.Receive(entry);
                }
                catch
                {
                    /* Dismiss Single Sink Exception */
                }
            }
        }
    }
}