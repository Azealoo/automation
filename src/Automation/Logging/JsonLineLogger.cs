using System.Text.Json;

namespace Automation.Logging;

public interface ILogger
{
    void Log(string level, string @event, object? payload = null);
    void Info(string @event, object? payload = null);
    void Warn(string @event, object? payload = null);
    void Error(string @event, object? payload = null);
}

public abstract class LoggerBase : ILogger
{
    public abstract void Log(string level, string @event, object? payload = null);
    public void Info(string @event, object? payload = null) => Log("info", @event, payload);
    public void Warn(string @event, object? payload = null) => Log("warn", @event, payload);
    public void Error(string @event, object? payload = null) => Log("error", @event, payload);
}

public sealed class JsonLineLogger : LoggerBase, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;

    public JsonLineLogger(string path, Func<DateTimeOffset>? clock = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public override void Log(string level, string @event, object? payload = null)
    {
        var record = new Dictionary<string, object?>
        {
            ["ts"] = _clock().ToString("O"),
            ["level"] = level,
            ["event"] = @event,
        };
        if (payload is not null) record["data"] = payload;
        var line = JsonSerializer.Serialize(record);
        lock (_gate)
        {
            _writer.WriteLine(line);
            Console.WriteLine(line);
        }
    }

    public void Dispose() => _writer.Dispose();
}

public sealed class ConsoleLogger : LoggerBase
{
    private readonly object _gate = new();
    public override void Log(string level, string @event, object? payload = null)
    {
        var line = payload is null
            ? $"[{level}] {@event}"
            : $"[{level}] {@event} {JsonSerializer.Serialize(payload)}";
        lock (_gate) Console.WriteLine(line);
    }
}
