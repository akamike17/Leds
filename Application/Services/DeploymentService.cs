using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Application.Services;

/// <summary>Resultado del despliegue con estado de cada fase.</summary>
public sealed class DeployResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public Checksum? Checksum { get; init; }

    public static DeployResult Ok(string phase, Checksum? checksum = null)
        => new() { Success = true, Phase = phase, Checksum = checksum };
    public static DeployResult Fail(string phase, string error)
        => new() { Success = false, Phase = phase, Error = error };
}

/// <summary>
/// Orquesta el pipeline de despliegue (sección 7):
/// Scene → Validate → Optimize(copy) → Compile → ScenePackage → TargetValidate
/// → Prepare → Upload → VerifyChecksum → Activate.
/// Transferencia fallida conserva LastKnownGoodScene en el target (invariante 10).
/// </summary>
public sealed class DeploymentService
{
    /// <summary>Valida la escena (proyecto válido, IDs, duración, canvas).</summary>
    public static (bool Ok, string? Error) Validate(Scene scene, CanvasDefinition canvas)
    {
        if (scene == null) return (false, "Escena nula.");
        if (scene.Duration <= TimeSpan.Zero) return (false, "Duración de escena debe ser > 0.");
        if (scene.Layers.Count == 0 || scene.Layers.All(l => l.Objects.Count == 0))
            return (false, "La escena no tiene contenido visible.");
        if (canvas.Width <= 0 || canvas.Height <= 0) return (false, "Canvas inválido.");
        return (true, null);
    }

    /// <summary>Pipeline de despliegue completo contra un IDisplayTarget.</summary>
    public async Task<DeployResult> SendAsync(
        Scene scene, CanvasDefinition canvas, Domain.Deployment.IDisplayTarget target, CancellationToken ct = default)
    {
        // 1. Validate
        var (ok, err) = Validate(scene, canvas);
        if (!ok) return DeployResult.Fail("Validate", err!);

        // 2. Connect / identity / capabilities
        var conn = await target.ConnectAsync(ct);
        if (!conn.Success) return DeployResult.Fail("Connect", conn.Error);
        var capsResult = await target.GetCapabilitiesAsync(ct);
        if (!capsResult.Success || capsResult.Value == null)
            return DeployResult.Fail("GetCapabilities", capsResult.Error);
        var caps = capsResult.Value;

        // 3. Compile (Optimize(copy) está implícito: ScenePackage es inmutable en destino)
        var (pkg, compileErr) = Domain.Deployment.SceneCompiler.CompileForTarget(scene, canvas, caps);
        if (pkg == null) return DeployResult.Fail("Compile", compileErr!);

        // 4. Prepare transfer
        var prep = await target.PrepareTransferAsync(pkg.EstimatedBytes, ct);
        if (!prep.Success || string.IsNullOrEmpty(prep.Value))
            return DeployResult.Fail("Prepare", prep.Error);
        var ticket = prep.Value!;

        // 5. Upload
        var up = await target.UploadAsync(ticket, pkg, ct);
        if (!up.Success) return DeployResult.Fail("Upload", up.Error);

        // 6. VerifyChecksum
        var expected = pkg.ComputeChecksum();
        var ver = await target.VerifyAsync(ticket, expected, ct);
        if (!ver.Success) return DeployResult.Fail("VerifyChecksum", ver.Error);

        // 7. Activate (atómico; falla conserva LastKnownGood)
        var act = await target.ActivateAsync(ticket, ct);
        if (!act.Success) return DeployResult.Fail("Activate", act.Error);

        return DeployResult.Ok("Activate", expected);
    }
}