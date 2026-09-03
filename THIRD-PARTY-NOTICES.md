# THIRD-PARTY NOTICES — DSLetras (AtlasLetreros V1 BASIC)

Registro de dependencias de terceros conforme a la "Política de terceros" (spec
sección 3): documentación oficial → licencia → compatibilidad → mantenimiento →
vulnerabilidades → decisión.

## Runtime (producción)

**Ninguna dependencia de terceros.**

El proyecto `DSLetreros` compila sobre .NET 8 exclusivamente con la BCL
(`System.Text.Json`, `System.Security.Cryptography`, ASP.NET Core MVC). SkiaSharp
se descartó: Canvas 2D + JS cubren el raster/editor (spec permite omitirlo cuando
no hace falta).

## Test-only (DSLetreros.Tests)

| Paquete                   | Versión | Licencia    | Uso                                  |
|---------------------------|---------|-------------|--------------------------------------|
| Microsoft.NET.Test.Sdk    | 17.11.1 | MIT         | Runner de pruebas                    |
| xunit                     | 2.9.2   | Apache-2.0  | Framework de tests                   |
| xunit.runner.visualstudio | 2.8.2   | Apache-2.0  | Adapter de Visual Studio / dotnet test|

Dependencias transitivas del tooling de test (TestPlatform, Newtonsoft.Json 13.0.1,
xunit.* , System.Reflection.Metadata) heredan las mismas licencias permisivas
(MIT / Apache-2.0).

## Auditoría

- `dotnet list package --vulnerable --include-transitive`: **0 paquetes vulnerables.**
- Sin dependencias runtime → sin superficie de vulnerabilidad de terceros en producción.

## Browsers de frontend (runtime web)

El editor usa únicamente JavaScript ES modules + HTML Canvas 2D nativo del navegador.
Bootstrap y jQuery (wwwroot/lib) se sirven localmente como estáticos del template
inicial de ASP.NET Core y NO se cargan desde CDN en tiempo de ejecución.