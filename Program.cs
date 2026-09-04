using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews().AddJsonOptions(o =>
{
    foreach (var c in DSLetreros.Infrastructure.Persistence.AtlasJson.Options.Converters)
        o.JsonSerializerOptions.Converters.Add(c);
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Antiforgery (CSRF) para operaciones mutables, incluido JSON fetch:
// validamos el token vía encabezado `RequestVerificationToken` (a la vez que
// el campo de formulario estándar para los <form> MVC normales).
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "RequestVerificationToken";
});

// DSLetras: domain/infrastructure services
builder.Services.AddSingleton<DSLetreros.Infrastructure.Persistence.AtlasProjectStore>();
builder.Services.AddScoped<DSLetreros.Application.Services.ProjectService>();
builder.Services.AddScoped<DSLetreros.Application.Services.LibraryService>();
builder.Services.AddScoped<DSLetreros.Application.Services.EditingService>();
builder.Services.AddScoped<DSLetreros.Application.Services.DeploymentService>();

// Seguridad (spec 21 + v2.md §7): DSLetras es una herramienta LOCAL. Por defecto
// el servidor se enlaza SOLO a loopback (127.0.0.1), de forma que el control de
// dispositivos jamás quede expuesto a interfaces externas por accidente.
//
// Boundary explícito: para exponer la app fuera de loopback el operador DEBE fijar
// la variable DS_LETRAS_ALLOW_LAN=true *y* especificar ASPNETCORE_URLS con una
// interfaz no-loopback. Sin ese opt-in inequívoco, cualquier ASPNETCORE_URLS
// que no sea loopback es rechazado al arranque (fail-fast), en lugar de enlazar
// silenciosamente a la red local sin autenticación.
var allowLan = DSLetreros.Infrastructure.Security.LoopbackPolicy.LanExplicitlyAllowed(
    Environment.GetEnvironmentVariable(DSLetreros.Infrastructure.Security.LoopbackPolicy.AllowLanVariable));
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrEmpty(configuredUrls))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5099");
}
else if (!allowLan && DSLetreros.Infrastructure.Security.LoopbackPolicy.ContainsNonLoopbackUrl(configuredUrls))
{
    throw new InvalidOperationException(
        "ASPNETCORE_URLS expone una interfaz no-loopback sin opt-in. Fija DS_LETRAS_ALLOW_LAN=true para permitirlo explícitamente (sin autenticación, sólo red de confianza).");
}

// Seguridad (spec 21): tope de tamaño de request. El límite REAL lo impone Kestrel
// (Limits.MaxRequestBodySize); un Configure<IHttpMaxRequestBodySizeFeature> por DI
// NO produce ningún límite global (la feature se resuelve por-request vía middleware),
// así que se eliminó (v2.md §8) para no aparentar una protección que no existe.
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 64 * 1024 * 1024;   // 64 MiB por request
    k.Limits.MaxRequestBufferSize = 64 * 1024;        // 64 KiB de buffer de request
});

// Simulador local: IDisplayTarget en memoria (mismo contrato que el hardware).
builder.Services.AddSingleton<DSLetreros.Domain.Deployment.SimulatorTarget>();

// Discovery de dispositivos (Slice 9): identidad estable por serial, transports LAN/USB/Serial.
builder.Services.AddSingleton<DSLetreros.Application.Services.DeviceDiscoveryService>();

// Firmware (Slice 10): runtime del lado del dispositivo (LastKnownGood/safe boot/autonomous).
builder.Services.AddSingleton<DSLetreros.Domain.Deployment.Firmware>(sp =>
    new DSLetreros.Domain.Deployment.Firmware("FW-LOCAL-0001", "DSLetras Firmware", "1.0.0", 1, 64, 32));

var app = builder.Build();

// Observabilidad (spec 21): logger rotado a archivo, sin secretos.
app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
    .AddProvider(new DSLetreros.Infrastructure.Logging.RollingFileLoggerProvider());

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>Para que WebApplicationFactory&lt;Program&gt; en tests de integración pueda referenciar el punto de entrada.</summary>
public partial class Program { }