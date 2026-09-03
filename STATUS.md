# DSLetras — Estado del proyecto

Herramienta de diseño de letreros LED. Spec maestro: `AtlasLetreros_REV3_DEEPSEEK_MASTER.md`.

## Estado de slices (0–12)

| Slice | Nombre | Estado |
|-------|--------|--------|
| 0 | Skeleton/CI/contratos | COMPLETO |
| 1 | Project + Scene + Layer + objetos + editor vacío + Save/Open | COMPLETO |
| 2 | FrameBuffer + Texto + fuentes + auto-layout + golden | COMPLETO |
| 3 | Selección/mover/duplicar/borrar/inspector/Undo/Redo/timeline | COMPLETO |
| 4 | DrawingObject + formas + tests de puntero + Mi biblioteca | COMPLETO |
| 5 | Iconos/imágenes + raster + assets embebidos/licencias | COMPLETO |
| 6 | Timing + animaciones + golden temporal | COMPLETO |
| 7 | Simulator + compile/send/activate + contract tests | COMPLETO |
| 8 | Autosave/recovery/atomic save/migraciones/fuzz/fault | COMPLETO |
| 9 | Discovery + identidad + USB/LAN + upload transaccional | COMPLETO |
| 10 | Firmware + LastKnownGood + playback autónomo + safe boot | COMPLETO |
| 11 | UX hardening/accesibilidad/advanced | COMPLETO |
| 12 | R1–R5 + mutation + fuzz + fault + soak + dependency audit | COMPLETO |

## Validación final

- **Build Release: 0 errores.**
- **Tests .NET: 337/337 pass.**
- **E2E navegador (Playwright): 4/4 pass.**
- **Mutation score (Stryker.NET): 100.00%** sobre la lógica núcleo (Firmware,
  FirmwareTarget, SceneCompiler, ScenePackage, ScenePackageJson, DeviceProtocol,
  ProjectValidator). 121/121 mutantes matados, 0 supervivientes. Las exclusiones
  (mutadores equivalentes + spans defensivos) están documentadas en
  `MUTATION-JUSTIFICATION.md`.
- **Vulnerabilidades: 0 paquetes vulnerables** (`dotnet list package --vulnerable`).
- **Dependencias:** 1 paquete runtime (`System.IO.Ports`, MIT — transporte serial
  USB). Resto = BCL pura. Detalle en `THIRD-PARTY-NOTICES.md`.

## Cierre de producción (hardening)

- **Transports físicos reales**: `TcpDeviceChannel` (LAN/TCP) y `SerialDeviceChannel`
  (USB/Serial) implementan `IDeviceChannel`, validados con loopback TCP real.
- **Seguridad (spec 21)**: tope de tamaño de request (64 MiB, Kestrel + feature),
  una transferencia activa por target, identidad por serial estable (no IP).
- **Observabilidad (spec 21)**: logger rotado a archivo JSONL (`logs/`), 1 archivo
  por día, redacta secretos (password/token/secret/checksum, etc.).
- **CI gate**: `.github/workflows/ci.yml` corre build + test + E2E + Stryker (100%)
  + auditoría de vulnerabilidades en cada push/PR a `master`.

## Warnings restantes (4) y justificación

Todos preexistentes y no bloqueantes, en código de test o helper sin uso real:

1. `Font5x7.cs(42,22) CS0219` — `_x_` (patrón `"..#.."`) asignado pero no usado:
   constante de conveniencia para glifos en minúscula sin uso tras el refactor de
   `MeasureGlyph`. Inofensivo.
2. `DeviceProtocolAndDiscoveryTests.cs(123,57) CS8602` — desreferencia de posible
   null en assert de test; `Assert.True(id.Success)` previo lo garantiza.
3. `DisplayTargetContractTests.cs(89,57) CS8602` — ídem, solo en test.
4. `RendererTests.cs(136,9) xUnit2012` — sugiere `Assert.Contains` en vez de
   `Assert.True(Any())`; estilo, no defecto.

## Archivos de referencia

- `AtlasLetreros_REV3_DEEPSEEK_MASTER.md` — spec maestro.
- `MUTATION-JUSTIFICATION.md` — resultado y clasificación de mutation testing.
- `THIRD-PARTY-NOTICES.md` — auditoría de dependencias de terceros.
- `stryker-config.json` — configuración de Stryker.NET (100% sobre lógica núcleo).
- `tests/e2e/` — suite E2E de Playwright.
- `.github/workflows/ci.yml` — pipeline CI.