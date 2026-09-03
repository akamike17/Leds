using System.Net.Http;
using System.Net.Http.Headers;
using DSLetreros.Application.Services;
using DSLetreros.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace DSLetreros.Tests.Web;

/// <summary>
/// Factory de integración real (WebApplicationFactory) para probar controladores MVC.
/// Aísla ProjectService y LibraryService a directorios temporales para no tocar App_Data
/// real. El antiforgery se conserva (endpoints POST exigen token vía encabezado).
/// </summary>
public sealed class DsLetrasFactory : WebApplicationFactory<Program>
{
    public string ProjectsRoot { get; } = Path.Combine(Path.GetTempPath(), "dsletras-web", Guid.NewGuid().ToString("N"));
    public string LibraryRoot { get; } = Path.Combine(Path.GetTempPath(), "dsletras-web", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Reemplaza los servicios con instancias aisladas en temp.
            services.RemoveAll<ProjectService>();
            services.AddScoped(_ => new ProjectService(new AtlasProjectStore(), ProjectsRoot));

            services.RemoveAll<LibraryService>();
            services.AddScoped(_ => new LibraryService(LibraryRoot));
        });
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
        where T : class
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(T))
                services.RemoveAt(i);
        }
    }
}