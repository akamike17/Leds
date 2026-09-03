using System.Text;
using System.Text.Json;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Firmware del dispositivo (spec sección 18): el runtime del LADO del dispositivo.
///
/// Mantiene identidad estable (serial, no IP), capabilities, staging temporal con
/// límites, verificación por checksum, activación atómica con LastKnownGoodScene,
/// safe boot (arranca con la última escena buena si la activa está corrupta),
/// playback autónomo determinista y timeouts de transferencia.
///
/// No es un proceso real: modela la semántica de estado del firmware para que el
/// simulador y las pruebas contract/equivalencia (R5) compartan el mismo
/// comportamiento que el hardware.
/// </summary>
public sealed class Firmware
{
    private readonly DeviceIdentity _identity;
    private readonly DeviceCapabilities _capabilities;
    private readonly object _gate = new();

    private readonly Dictionary<string, StagedScene> _staging = new();
    private ScenePackage? _active;
    private ScenePackage? _lastKnownGood;
    private DeviceStatus _status = DeviceStatus.Unknown;

    private bool _playbackRunning;
    private CancellationTokenSource? _playbackCts;

    /// <summary>Una transferencia activa a la vez (spec 21). Ticket actual, o null.</summary>
    private string? _activeTransferTicket;

    /// <summary>Timeout de transferencia (inactividad del staging).</summary>
    public TimeSpan TransferTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public Firmware(string serial, string model = "DSLetras Device", string firmwareVersion = "1.0.0",
        int protocolVersion = 1, int width = 64, int height = 32)
    {
        _identity = new DeviceIdentity
        {
            Serial = serial,
            Model = model,
            FirmwareVersion = firmwareVersion,
            ProtocolVersion = protocolVersion,
        };
        _capabilities = new DeviceCapabilities
        {
            LogicalWidth = width,
            LogicalHeight = height,
            ColorCapability = ColorCapability.Rgb24,
            MaxSceneBytes = 8 * 1024 * 1024,
            MaxAssetBytes = 4 * 1024 * 1024,
            SupportedAnimations = Enum.GetValues<AnimationKind>().ToList(),
            ProtocolVersion = protocolVersion,
            AutonomousPlayback = true,
        };
    }

    public DeviceIdentity Identity => _identity;
    public DeviceCapabilities Capabilities => _capabilities;

    /// <summary>Escena activa (reproducida actualmente).</summary>
    public ScenePackage? Active { get { lock (_gate) return _active; } }

    /// <summary>Última escena buena (LastKnownGoodScene, invariante 10).</summary>
    public ScenePackage? LastKnownGood { get { lock (_gate) return _lastKnownGood; } }

    public DeviceStatus Status { get { lock (_gate) return _status; } }

    public bool PlaybackRunning { get { lock (_gate) return _playbackRunning; } }

    // ---- Fases del protocolo (lado dispositivo) ----

    /// <summary>Handshake: el host se anuncia. El firmware responde su estado.</summary>
    public void Hello()
    {
        lock (_gate)
        {
            // Safe boot: si hay una escena activa, se asume buena; si no y hay
            // LastKnownGood, se restaura la última buena.
            if (_active == null && _lastKnownGood != null)
                SafeBootRestore();

            _status = DeviceStatus.Online;
        }
    }

    /// <summary>Devuelve la identidad estable (serial).</summary>
    public DeviceIdentity GetIdentity() => _identity;

    /// <summary>Prepara una transferencia: registra staging temporal con límites (spec 18).</summary>
    public (bool Ok, string? Error, string? Ticket) Prepare(string ticket, long sceneBytes)
    {
        lock (_gate)
        {
            if (_activeTransferTicket != null)
                return (false, "Ya hay una transferencia en curso.", null);

            if (sceneBytes <= 0)
                return (false, "Tamaño de escena inválido.", null);

            if (sceneBytes > _capabilities.MaxSceneBytes)
                return (false, $"Escena excede MaxSceneBytes ({_capabilities.MaxSceneBytes}).", null);

            _staging[ticket] = new StagedScene
            {
                ExpectedBytes = sceneBytes,
                ReceivedAt = DateTimeOffset.UtcNow,
            };
            _activeTransferTicket = ticket;
            return (true, null, ticket);
        }
    }

    /// <summary>Recibe (acumula) el payload compilado en el staging del ticket.</summary>
    public (bool Ok, string? Error) Upload(string ticket, ScenePackage package)
    {
        lock (_gate)
        {
            if (!_staging.TryGetValue(ticket, out var staged))
                return (false, "Ticket de transferencia desconocido.");

            // Invariantes del paquete ANTES de calcular el tamaño wire: un FrameIntervalMs
            // no finito (NaN/∞) no puede serializarse y `EstimatedBytes` lanzaría una
            // ArgumentException en vez de devolver un fallo limpio. Mismo orden que
            // ChannelDisplayTarget.UploadAsync y SceneCompiler (preflight).
            var invariantErr = ValidatePackageInvariants(package);
            if (invariantErr != null)
                return (false, invariantErr);

            // Verifica que el tamaño real del paquete coincide con el esperado en Prepare.
            if (package.EstimatedBytes != staged.ExpectedBytes)
                return (false, $"Tamaño del paquete ({package.EstimatedBytes}B) no coincide con el esperado ({staged.ExpectedBytes}B).");

            staged.Package = package;
            staged.ReceivedAt = DateTimeOffset.UtcNow;
            _staging[ticket] = staged;
            return (true, null);
        }
    }

    /// <summary>Invariantes del paquete en upload: FrameInterval &gt; 0 y finito.</summary>
    private static string? ValidatePackageInvariants(ScenePackage package)
    {
        var interval = package.FrameIntervalMs;
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0.0)
            return "FrameIntervalMs inválido (debe ser > 0 y finito).";
        return null;
    }

    /// <summary>Verifica checksum del paquete en staging.</summary>
    public (bool Ok, string? Error) Verify(string ticket, Checksum expected)
    {
        lock (_gate)
        {
            if (!_staging.TryGetValue(ticket, out var staged) || staged.Package == null)
                return (false, "Sin paquete en staging para verificar.");

            var actual = staged.Package.ComputeChecksum();
            if (!actual.Equals(expected))
                return (false, "Checksum no coincide.");

            staged.Verified = true;
            staged.ExpectedChecksum = expected;
            _staging[ticket] = staged;
            return (true, null);
        }
    }

    /// <summary>Activación atómica: consolida la escena anterior como LastKnownGood (invariante 10).</summary>
    public (bool Ok, string? Error) Activate(string ticket)
    {
        lock (_gate)
        {
            if (!_staging.TryGetValue(ticket, out var staged) || staged.Package == null)
                return (false, "Sin paquete verificado para activar.");
            if (!staged.Verified)
                return (false, "El paquete no fue verificado (checksum).");

            // Atómico: la escena activa previa pasa a LastKnownGood.
            if (_active != null)
                _lastKnownGood = _active;
            _active = staged.Package;
            _staging.Remove(ticket);
            _activeTransferTicket = null;
            _status = DeviceStatus.Online;
            return (true, null);
        }
    }

    /// <summary>Detiene la reproducción autónoma.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopPlaybackLocked();
            _status = DeviceStatus.Online;
        }
    }

    /// <summary>Ejecuta un tick determinista de playback autónomo; devuelve el frame a t.</summary>
    public (bool Ok, string? Error, CompiledFrame? Frame) PlaybackTick(double timeMs)
    {
        lock (_gate)
        {
            if (_active == null)
                return (true, null, null);

            var frames = _active.Frames;
            if (frames.Count == 0)
                return (true, null, null);

            var interval = _active.FrameIntervalMs;

            // Tiempo no finito/NaN o negativo, o intervalo inválido → no se puede indexar de
            // forma segura; se devuelve el primer frame sin cálculo aritmético peligroso.
            if (double.IsNaN(timeMs) || double.IsInfinity(timeMs))
                return (false, "Tiempo de playback no finito.", null);

            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0.0)
                return (false, "FrameIntervalMs inválido (debe ser > 0 y finito).", null);

            // Tiempo negativo: se clampa a 0 (no puede produir índice negativo).
            if (timeMs < 0)
                timeMs = 0;

            // idx = floor(timeMs / interval) % frames.Count, siempre en [0, frames.Count).
            var idx = (int)(timeMs / interval) % frames.Count;
            if (idx < 0) idx += frames.Count; // defensa ante cast/redondeo negativo
            return (true, null, frames[idx]);
        }
    }

    /// <summary>Arranca el playback autónomo en background (loop + timeout de inactividad).</summary>
    public void StartPlayback(CancellationToken external = default)
    {
        lock (_gate)
        {
            if (_playbackRunning) return;
            _playbackRunning = true;
            _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(external);
        }

        var cts = _playbackCts;
        _ = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cts!.IsCancellationRequested)
            {
                var t = sw.Elapsed.TotalMilliseconds;
                PlaybackTick(t);
                try { await Task.Delay(16, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }, cts.Token);
    }

    /// <summary>Safe boot: restaura la última escena buena si la activa es inválida/corrupta.</summary>
    private void SafeBootRestore()
    {
        // _active es null y _lastKnownGood no → se restaura LastKnownGood.
        _active = _lastKnownGood;
        _lastKnownGood = null;
    }

    private void StopPlaybackLocked()
    {
        _playbackRunning = false;
        _playbackCts?.Cancel();
        _playbackCts = null;
    }

    /// <summary>Rechaza staging envejecido (timeout de transferencia).</summary>
    public int PurgeExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _staging
                .Where(kv => now - kv.Value.ReceivedAt > TransferTimeout)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in expired)
            {
                _staging.Remove(k);
                if (_activeTransferTicket == k) _activeTransferTicket = null;
            }
            return expired.Count;
        }
    }

    private sealed class StagedScene
    {
        public long ExpectedBytes { get; set; }
        public ScenePackage? Package { get; set; }
        public Checksum ExpectedChecksum { get; set; }
        public bool Verified { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
    }
}