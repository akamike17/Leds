using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DSLetreros.Application.Services;
using DSLetreros.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DSLetreros.Tests.Web;

/// <summary>
/// final.md §2.E / §24.F — "Nuevo/Create" NUNCA debe redirigir a un proyecto
/// inexistente si la persistencia inicial falló. Inyecta un fallo de save real
/// (AtlasProjectStore.FailPoint) y verifica que el controller devuelve error y
/// mantiene al usuario en el flujo de creación (no un redirect a un id fantasma).
/// </summary>
public sealed class NewProjectPersistenceFailureTests : IClassFixture<NewProjectPersistenceFailureTests.FaultyFactory>
{
    private readonly HttpClient _client;

    public NewProjectPersistenceFailureTests(FaultyFactory factory)
    {
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    // Factory que reemplaza ProjectService por uno cuyo store falla en la fase
    // before-rename-temp (se invoca SIEMPRE, también en el primer save), de modo
    // que SaveAsync devuelve Success=false de forma determinista.
    public sealed class FaultyFactory : WebApplicationFactory<Program>
    {
        public string ProjectsRoot { get; } = Path.Combine(Path.GetTempPath(), "dsletras-web-fault", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ProjectService>();
                services.RemoveAll<LibraryService>();

                services.AddScoped(_ =>
                {
                    var store = new AtlasProjectStore
                    {
                        FailPoint = phase => phase == "before-rename-temp" ? throw new System.IO.IOException("fault") : false,
                    };
                    return new ProjectService(store, ProjectsRoot);
                });
                services.AddScoped(_ => new LibraryService(Path.Combine(Path.GetTempPath(), "dsletras-web-fault-lib", Guid.NewGuid().ToString("N"))));
            });
        }
    }

    private static HttpRequestMessage WithAntiforgery(HttpMethod method, string url, string token, HttpContent? content)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("RequestVerificationToken", token);
        if (content != null) req.Content = content;
        return req;
    }

    private async Task<string> GetAntiforgeryTokenAsync()
    {
        var resp = await _client.GetAsync("/Projects/New");
        var html = await resp.Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    [Fact]
    public async Task Editor_New_save_failure_does_not_redirect_to_new_project()
    {
        var token = await GetAntiforgeryTokenAsync();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["width"] = "16", ["height"] = "16", ["name"] = "X",
        });

        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Editor/New", token, content));

        // No debe redirigir (302/Redirect); debe devolver un error al cliente.
        Assert.NotEqual(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Found, resp.StatusCode);

        // El error debe ser un 400 con success:false y un mensaje útil.
        var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("No se pudo crear el proyecto", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Projects_Create_save_failure_returns_form_with_error()
    {
        var token = await GetAntiforgeryTokenAsync();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "X", ["Width"] = "16", ["Height"] = "16",
        });

        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Projects/Create", token, content));

        // No debe redirigir; vuelve al formulario "New" re-renderizado con el error.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("No se pudo crear el proyecto", html);
    }
}