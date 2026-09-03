using System.Text.Json;

namespace DSLetreros.Infrastructure.Logging;

/// <summary>
/// Logger rotado a archivo, sin dependencias de terceros y sin secretos (spec 21).
/// Escribe JSONL a `logs/dsletras-YYYYMMDD.jsonl`, una línea por evento, y rota por
/// fecha (un archivo por día). Redacta campos sensibles conocidos (contraseña,
/// token, secret) antes de escribir.
/// </summary>
public sealed class RollingFileLogger : ILogger
{
    private readonly string _dir;
    private readonly string _category;
    private static readonly object Gate = new();

    private static readonly string[] SensitiveKeys =
        { "password", "pass", "secret", "token", "authorization", "apikey", "api_key", "cookie", "checksum" };

    public RollingFileLogger(string category, string? baseDir = null)
    {
        _category = category;
        _dir = baseDir ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_dir);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Level = logLevel.ToString(),
            Category = _category,
            Message = Redact(formatter(state, exception)),
            Exception = exception?.ToString(),
            ExceptionType = exception?.GetType().FullName,
        };

        var line = JsonSerializer.Serialize(entry);
        lock (Gate)
        {
            var file = Path.Combine(_dir, $"dsletras-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            try { File.AppendAllText(file, line + Environment.NewLine); }
            catch { /* best-effort: logging nunca debe tumbar la app */ }
        }
    }

    private static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var result = message;
        foreach (var key in SensitiveKeys)
        {
            // redacta patrones clave=valor o "clave": "valor" (insensible a mayúsculas)
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $"(?i)([\"'&?]?{System.Text.RegularExpressions.Regex.Escape(key)}[\"'&]?)(\\s*[:=]\\s*)(\"[^\"]*\"|'[^']*'|\\S+)",
                "$1$2\"[REDACTADO]\"");
        }
        return result;
    }

    private sealed class LogEntry
    {
        public string Timestamp { get; init; } = string.Empty;
        public string Level { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string? Exception { get; init; }
        public string? ExceptionType { get; init; }
    }
}

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string? _baseDir;
    private readonly Dictionary<string, RollingFileLogger> _loggers = new();

    public RollingFileLoggerProvider(string? baseDir = null) => _baseDir = baseDir;

    public ILogger CreateLogger(string categoryName)
    {
        lock (_loggers)
        {
            if (!_loggers.TryGetValue(categoryName, out var logger))
                _loggers[categoryName] = logger = new RollingFileLogger(categoryName, _baseDir);
            return logger;
        }
    }

    public void Dispose() { lock (_loggers) _loggers.Clear(); }
}