using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace DSLetreros.Tests.Web;

/// <summary>
/// Tests de integración de controladores MVC. Cubren la lógica de negocio expuesta
/// por HTTP: validación, model binding, anti-CSRF (token por encabezado) y JSON.
/// </summary>
public sealed class ControllerIntegrationTests : IClassFixture<DsLetrasFactory>
{
    private readonly HttpClient _client;

    public ControllerIntegrationTests(DsLetrasFactory factory)
    {
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    // Obtiene el token antiforgery de una página GET (campo __RequestVerificationToken).
    private async Task<string> GetAntiforgeryTokenAsync()
    {
        var resp = await _client.GetAsync("/Projects/New");
        var html = await resp.Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static HttpRequestMessage WithAntiforgery(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("RequestVerificationToken", token);
        if (content != null) req.Content = content;
        return req;
    }

    // ---------- ProjectsController ----------

    [Fact]
    public async Task Projects_Index_returns_ok_and_lists_none_initially()
    {
        var resp = await _client.GetAsync("/Projects/Index");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Nuevo proyecto", html);
    }

    [Fact]
    public async Task Projects_New_returns_form_with_antiforgery_token()
    {
        var token = await GetAntiforgeryTokenAsync();
        Assert.False(string.IsNullOrEmpty(token));
    }

    // ---------- LibraryController ----------

    [Fact]
    public async Task Library_Icons_returns_nonempty_icon_catalog()
    {
        var resp = await _client.GetAsync("/Library/Icons");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        var icons = json.GetProperty("icons");
        Assert.True(icons.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Library_Drawings_returns_empty_initially()
    {
        var resp = await _client.GetAsync("/Library/Drawings");
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("drawings").GetArrayLength());
    }

    [Fact]
    public async Task Library_SaveDrawing_persists_valid_drawing()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { name = "X", width = 2, height = 2, pixels = new[] { 1, 0, 1, 0 } });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Library/SaveDrawing", token, body));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Library_SaveDrawing_rejects_oversized_dimensions()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { name = "Big", width = 10000, height = 10000, pixels = new int[0] });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Library/SaveDrawing", token, body));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Library_DeleteDrawing_with_invalid_id_fails_cleanly()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { id = "no-es-un-guid" });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Library/DeleteDrawing", token, body));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Library_RasterizeImage_rejects_null_rgba()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { srcWidth = 2, srcHeight = 2 });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Library/RasterizeImage", token, body));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
    }

    // ---------- EditorController (GET mutante → POST) ----------

    [Fact]
    public async Task Editor_New_without_antiforgery_is_rejected()
    {
        // Editor/New es POST con antiforgery: sin token debe devolver 400.
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["width"] = "16", ["height"] = "16", ["name"] = "X",
        });
        var resp = await _client.PostAsync("/Editor/New", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Editor_Load_with_empty_guid_returns_404()
    {
        var resp = await _client.GetAsync("/Editor/Load?id=00000000-0000-0000-0000-000000000000");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---------- DeployController ----------

    [Fact]
    public async Task Deploy_Discover_lists_simulator()
    {
        var resp = await _client.GetAsync("/Deploy/Discover");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var targets = json.GetProperty("targets");
        Assert.True(targets.GetArrayLength() >= 1); // al menos el simulador
    }

    [Fact]
    public async Task Deploy_Send_without_projectId_is_rejected()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { projectId = "", targetId = "" });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Deploy/Send", token, body));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("Proyecto no especificado", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Deploy_Send_invalid_projectId_returns_bad_request()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { projectId = "zzz", targetId = "" });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Deploy/Send", token, body));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Contains("inválido", json.GetProperty("message").GetString());
    }

    // ---------- SettingsController ----------

    [Fact]
    public async Task Settings_Targets_lists_simulator()
    {
        var resp = await _client.GetAsync("/Settings/Targets");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("targets").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Settings_Discover_with_invalid_lan_ignores_channel()
    {
        var token = await GetAntiforgeryTokenAsync();
        var body = JsonContent.Create(new { lan = new[] { new { host = "", port = 0 } }, serial = new object[0] });
        var resp = await _client.SendAsync(WithAntiforgery(HttpMethod.Post, "/Settings/Discover", token, body));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Settings_Discover_with_null_request_is_rejected()
    {
        var token = await GetAntiforgeryTokenAsync();
        var req = WithAntiforgery(HttpMethod.Post, "/Settings/Discover", token, JsonContent.Create("null"));
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}