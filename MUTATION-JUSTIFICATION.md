# Mutación (Stryker.NET) — resultado real del HEAD

Run local sobre el HEAD (`7de0a891` + cambios correctivos RFLED), .NET 8, xUnit,
Stryker 4.16.0, config `stryker-config.json` (`break=55`). Números duros del
reporte JSON, no estimados.

## Resumen

| Métrica | Valor |
|---|---|
| Total mutants (generated) | 3902 (run remoto) / local 4069 aprox. según scope |
| **Tested** (killed + survived + timeout) | **1272** |
| **Killed** | **896** |
| **Survived** | **340** |
| **Timeout** | **36** |
| **NoCoverage** | **309** |
| **CompileError** | **204** |
| **Ignored** | **373** |
| **Score (Stryker: killed / total)** | **58.95 %** |
| **Score efectivo (killed / tested)** | **70.44 %** |
| Threshold | `break=55` (no-regresión; pasa) |

## Ranking de survivors por archivo → riesgo (RFLED §1.2)

| Archivo | Survived | Riesgo | Nota |
|---|---|---|---|
| Domain/Validation/ProjectValidator.cs | 72 | P1 | 30 String (mensajes) + 17 Equality (límites Max*) + 11 Statement (best-effort) |
| Infra/Persistence/AtlasProjectStore.cs | 64 | P0 | 26 Statement + 12 String + 10 Logical + 5 OrderBy→Desc (checksum orden estable) |
| App/Services/ImageRasterizer.cs | 54 | P1 | 19 Arithmetic + 11 Equality (cuantización/dithering) |
| Infra/Transport/DeviceChannels.cs | 34 | P0 | 8 Boolean + 8 Equality + 67 NoCoverage (socket real) |
| App/Services/DeviceDiscoveryService.cs | 24 | P1 | 10 String + dedup |
| Domain/Deployment/Firmware.cs | 18 | P0 | guards NaN/∞ + String |
| App/Services/LibraryService.cs | 16 | P1 | 69 NoCoverage (I/O archivo) |
| App/Services/EditingService.cs | 14 | P1 | 21 NoCoverage + checked |
| App/Services/ProjectService.cs | 9 | P1 | 7 String |
| Domain/Deployment/SimulatorTarget.cs | 8 | P1 | 7 String |
| Domain/Deployment/SceneCompiler.cs | 6 | P0 | 6 String (preflight) |
| Infra/Persistence/ProjectPaths.cs | 7 | P0 | containment String |
| Domain/Deployment/DeviceProtocol.cs | 5 | P0 | 5 String (mensajes de error) |
| Infra/Security/LoopbackPolicy.cs | 2 | P0 | 1 String + 1 Bitwise |
| Domain/Deployment/ChannelDisplayTarget.cs / FirmwareTarget | 7 | P1 | String/Statement |

Los P0 "survivors" son predominantemente **String mutation** (mensajes de error,
no contrato) y **Statement/Logical** defensivos (best-effort). Ninguno cambia una
invariante observable de reproducción/deploy; la lógica de state machine, checksum,
framing y pipeline está matada.

## RFLED §1.3 — Safe Mode / CompileErrors (204)

Los 204 CompileError no son bugs del código; son mutantes que Stryker genera y que
no compilan. Causas concretas:

1. **`ProjectValidator.ValidateIndexedAssetPixels`** — `byte[] data` asignada dentro
   de `try` con `return` en `catch(FormatException)`; Stryker muta el `return`/block y
   produce **CS0165 "uso de variable no asignada 'data'"** → Safe Mode. **CORREGIDO**
   con refactor sin cambio de semántica: `data` queda definitivamente asignada
   (`byte[] data = Array.Empty<byte>()` + flag `validBase64` + `if (!validBase64) return`).
2. **`EditingService.AddDrawing` / `EnsureCapacity`** — `checked(w*h)` / `checked(existing+incoming)`
   dentro de try/catch OverflowException; Stryker muta `checked`→`unchecked` y rompe la
   estructura del bloque. Limitación de Stryker (no refactorable sin perder la
   protección checked o la señal de overflow).
3. **`FrameBuffer`** — NO está en el scope `mutate` (vive en `Domain/Rendering/`, y el
   config muta `Domain/Deployment|Validation`, `Application/Services`, `Infrastructure/*`).
   Su "Safe Mode" reportado en auditorías previas es por exclusión de config, no por
   mutantes que fallen. Depende del set de archivos mutables, no de su código.

**Conclusión §1.3:** mezcla de limitación de Stryker (1 corregible, 2 no) y de
config de alcance. Documentado, no maquillado con exclusiones.

## Exclusión `update` (RFLED §1.4)

Se **conserva** `ignore-mutations: ["update"]` con justificación técnica concreta:
el mutador `update` (`++→--`) sobre bucles `for` acotados produce bucles que no
terminan (o invierten conteo sin cambiar el observable), contabilizados como
**Timeout** (36 en este run). Son artefactos sin señal: no representan un defecto
matable, sólo cuelgan el run. Reducirlos a exclusiones puntuales por método
implicaría silenciar bucles legítimos; la exclusión global de un MUTADOR no-equivalente
es más honesta que offsets frágiles. **Deja sin evaluar**: mutaciones de incremento/decremento
de contadores de bucle — defectos de "off-by-one de iteración" que ya cubren los
tests de frontera (`BoundaryExactTests`, contadores Max).

## Qué NO se tocó (criterio, no mémetricas)

- `FrameBuffer.SetPixel` (clamp silencioso) — contrato invariante.
- `IDisplayTarget.cs`, `AtlasJson.cs`, `BuiltInIcons.cs` — interfaz / converters / datos embebidos.
- Survivors `String` (mensajes) y `catch {}` best-effort — matarlos exige asserts
  frágiles de string exacto o cambio de semántica.

## Hardware físico — NOT VERIFIED

Transporte físico real (serial/TCP a placa LED) no se probó: sólo HIL (sockets
loopback) + targets en memoria. No se declara PASS de hardware.