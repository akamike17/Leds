# THIRD-PARTY NOTICES — DSLetras (AtlasLetreros V1 BASIC)

Registro de dependencias de terceros conforme a la "Política de terceros" (spec
sección 3): documentación oficial → licencia → compatibilidad → mantenimiento →
vulnerabilidades → decisión.

## Runtime (producción)

| Paquete         | Versión | Licencia | Uso                          |
|-----------------|---------|----------|------------------------------|
| System.IO.Ports | 8.0.0   | MIT      | Puerto serie USB (SerialDeviceChannel) |

Todo lo demás es BCL pura (.NET 8): `System.Text.Json`,
`System.Security.Cryptography`, `System.Net.Sockets` (TCP LAN),
ASP.NET Core MVC. SkiaSharp se descartó (Canvas 2D + JS bastan, spec permite
omitirlo).

## Test-only (DSLetreros.Tests)

| Paquete                   | Versión | Licencia    | Uso                  |
|---------------------------|---------|-------------|----------------------|
| Microsoft.NET.Test.Sdk    | 17.11.1 | MIT         | Runner de pruebas    |
| xunit                     | 2.9.2   | Apache-2.0  | Framework de tests   |
| xunit.runner.visualstudio | 2.8.2   | Apache-2.0  | Adapter VS/dotnet test|

## E2E (tests/e2e)

| Paquete         | Versión | Licencia | Uso                       |
|-----------------|---------|----------|---------------------------|
| @playwright/test| 1.62.1  | Apache-2.0| E2E de navegador (spec 20.9) |

## Auditoría

- `dotnet list package --vulnerable --include-transitive`: **0 paquetes vulnerables.**
- `npm audit` en tests/e2e: 0 vulnerabilidades.

## Browsers de frontend (runtime web)

El editor usa JavaScript ES modules + HTML Canvas 2D nativo. Bootstrap y jQuery
(wwwroot/lib) se sirven localmente como estáticos y NO se cargan desde CDN.