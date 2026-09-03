using System.Text.Json;

namespace DSLetreros.Infrastructure.Logging;

/// <summary>
/// Logger rotado a archivo, sin dependencias de terceros y sin secretos (spec 21).
/// Escribe JSONL a `logs/dsletras-YYYYMMDD.jsonl`, una línea por evento, y rota por
/// fecha (archivo por día) y por tamaño. Redacta campos sensibles conocidos
/// (contraseña, token, secret, authorization, cookie, api_key) tanto en el mensaje
/// como en las excepciones y el estado estructurado.
/// </summary>
public sealed class RollingFileLogger : ILogger
{
    private readonly string _dir;
    private readonly string _category;
    private static readonly object Gate = new();

    /// <summary>Tamaño máximo (bytes) de un archivo de log antes de rotar.</summary>
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    /// <summary>Número de archivos de respaldo (rotación por tamaño) que se conservan.</summary>
    private const int MaxRetainedFiles = 5;

    internal static readonly string[] SensitiveKeys =
    {
        "password", "pass", "secret", "token", "authorization", "apikey",
        "api_key", "cookie", "checksum"
    };

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
            // Redacta la excepción sin volcar ToString() crudo: sólo tipo + mensaje redactado.
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception == null ? null : Redact(exception.Message),
            State = Redact(state),
        };

        var line = JsonSerializer.Serialize(entry);
        lock (Gate)
        {
            var file = CurrentFile();
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length >= MaxFileSizeBytes)
                    Rotate();
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch { /* best-effort: logging nunca debe tumbar la app */ }
        }
    }

    /// <summary>Archivo activo del día.</summary>
    private string CurrentFile() =>
        Path.Combine(_dir, $"dsletras-{DateTime.UtcNow:yyyyMMdd}.jsonl");

    /// <summary>Rotación por fecha + tamaño: conserva hasta MaxRetainedFiles respaldos.</summary>
    private void Rotate()
    {
        // Rota el archivo del día a `dsletras-<fecha>.<n>.jsonl`, descendiendo los antiguos.
        var current = CurrentFile();
        if (!File.Exists(current)) return;

        for (int i = MaxRetainedFiles; i >= 1; i--)
        {
            var target = $"{current}.{i}";
            var next = i == 1 ? current : $"{current}.{i - 1}";
            if (File.Exists($"{current}.{i}"))
            {
                if (i >= MaxRetainedFiles) { try { File.Delete(target); } catch { } }
                else { try { File.Move(target, next, overwrite: true); } catch { } }
            }
        }
        try { File.Move(current, $"{current}.1", overwrite: true); } catch { /* best-effort */ }
    }

    /// <summary>Redacta claves sensibles (key=value, "key": "value", JSON) en el texto.</summary>
    internal static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        var result = input;
        foreach (var key in SensitiveKeys)
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $"(?i)([\\\"'&?]?{System.Text.RegularExpressions.Regex.Escape(key)}[\\\"'&]?)(\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|'[^']*'|\\S+)",
                "$1$2\"[REDACTADO]\"");
        }
        return result;
    }

    /// <summary>Redacta el estado estructurado (pares clave/valor ya formateados por el logger).</summary>
    internal static string Redact<TState>(TState state)
    {
        if (state == null) return string.Empty;
        var text = state.ToString() ?? string.Empty;
        return Redact(text);
    }

    private sealed class LogEntry
    {
        public string Timestamp { get; init; } = string.Empty;
        public string Level { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string? ExceptionType { get; init; }
        public string? ExceptionMessage { get; init; }
        public string State { get; init; } = string.Empty;
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