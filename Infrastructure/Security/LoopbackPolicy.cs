namespace DSLetreros.Infrastructure.Security;

/// <summary>
/// Política de boundary de red (v2.md §7). DSLetras es una herramienta LOCAL: por
/// defecto el servidor sólo puede enlazar a loopback. Esta clase es la fuente única
/// de verdad para decidir si una lista de URLs de binding contiene una interfaz
/// no-loopback, de forma que el arranque pueda rechazar (fail-fast) una exposición
/// LAN accidental sin opt-in.
/// </summary>
public static class LoopbackPolicy
{
    /// <summary>Nombre de la variable de opt-in inequívoco para exponer fuera de loopback.</summary>
    public const string AllowLanVariable = "DS_LETRAS_ALLOW_LAN";

    /// <summary>
    /// Detecta si una lista de URLs (separadas por ';') contiene alguna dirección
    /// que NO sea loopback. Acepta 127.x.x.x, [::1], localhost y *.localhost como
    /// loopback; cualquier host/IP externo (0.0.0.0, IP LAN, DNS público) se considera
    /// exposición no-loopback.
    /// </summary>
    public static bool ContainsNonLoopbackUrl(string urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return false;
        foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                continue; // URL malformada: no la consideramos non-loopback (Kestrel la rechazará).
            var host = uri.Host;
            if (host == "localhost" || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                continue;
            if (host == "::1" || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase))
                continue;
            if (host.StartsWith("127.", StringComparison.Ordinal))
                continue;
            return true;
        }
        return false;
    }

    /// <summary>True si el operador fijó el opt-in inequívoco para exponer fuera de loopback.</summary>
    public static bool LanExplicitlyAllowed(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}