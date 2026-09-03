using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews().AddJsonOptions(o =>
{
    foreach (var c in DSLetreros.Infrastructure.Persistence.AtlasJson.Options.Converters)
        o.JsonSerializerOptions.Converters.Add(c);
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// DSLetras: domain/infrastructure services
builder.Services.AddSingleton<DSLetreros.Infrastructure.Persistence.AtlasProjectStore>();
builder.Services.AddScoped<DSLetreros.Application.Services.ProjectService>();
builder.Services.AddScoped<DSLetreros.Application.Services.LibraryService>();
builder.Services.AddScoped<DSLetreros.Application.Services.EditingService>();
builder.Services.AddScoped<DSLetreros.Application.Services.DeploymentService>();

// Seguridad (spec 21): tope de tamaño de request y límites de formulario/kestrel.
// Rechaza payloads gigantes antes de llegar al dominio (evita OOM en fuzz).
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(
    o => o.MaxRequestBodySize = 64 * 1024 * 1024); // 64 MiB
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 64 * 1024 * 1024;
    k.Limits.MaxRequestBufferSize = 64 * 1024;
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