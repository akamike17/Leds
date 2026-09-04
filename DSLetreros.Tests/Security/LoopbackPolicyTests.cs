using DSLetreros.Infrastructure.Security;
using Xunit;

namespace DSLetreros.Tests.Security;

/// <summary>
/// v2.md §7: boundary local de red. LoopbackPolicy es la fuente única de verdad para
/// decidir si una lista de URLs expone una interfaz no-loopback, y para el opt-in
/// explícito de exposición LAN.
/// </summary>
public class LoopbackPolicyTests
{
    [Fact]
    public void Loopback_urls_are_not_non_loopback()
    {
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl("http://127.0.0.1:5099"));
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl("http://127.0.0.1:5099;http://localhost:5100"));
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl("http://localhost:5080"));
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl("http://[::1]:5099"));
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl("http://app.localhost:5099"));
    }

    [Fact]
    public void Non_loopback_urls_are_detected()
    {
        Assert.True(LoopbackPolicy.ContainsNonLoopbackUrl("http://0.0.0.0:5099"));
        Assert.True(LoopbackPolicy.ContainsNonLoopbackUrl("http://192.168.1.10:5099"));
        Assert.True(LoopbackPolicy.ContainsNonLoopbackUrl("http://example.com:5099"));
        // una mezcla con una sola no-loopback es suficiente para rechazar
        Assert.True(LoopbackPolicy.ContainsNonLoopbackUrl("http://127.0.0.1:5099;http://192.168.0.2:5100"));
    }

    [Fact]
    public void Empty_or_malformed_is_not_non_loopback()
    {
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl(""));
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl(null!));
        // URL sin esquema no parsea como absoluta → no se considera non-loopback
        Assert.False(LoopbackPolicy.ContainsNonLoopbackUrl("not-a-url"));
    }

    [Fact]
    public void Lan_opt_in_requires_inequivocal_true()
    {
        Assert.True(LoopbackPolicy.LanExplicitlyAllowed("true"));
        Assert.True(LoopbackPolicy.LanExplicitlyAllowed("TRUE"));
        Assert.False(LoopbackPolicy.LanExplicitlyAllowed("1"));
        Assert.False(LoopbackPolicy.LanExplicitlyAllowed(""));
        Assert.False(LoopbackPolicy.LanExplicitlyAllowed(null));
    }
}