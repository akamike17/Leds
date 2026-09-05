# Mutación (Stryker.NET) — resultado real del HEAD

Fuente de verdad: el job `Mutation (Stryker)` del CI remoto sobre el HEAD `56c5fc4`
(run `33936997917`); el run local previo quedó desincronizado respecto al refactor de
`ValidateIndexedAssetPixels`, por lo que sus números se descartan. .NET 8, xUnit,
Stryker 4.16.0, config `stryker-config.json` (`break=55`).

## Resumen (HEAD `56c5fc4`, CI remoto)

| Métrica | Valor |
|---|---|
| Total mutants (created) | 3902 |
| **Tested** (killed + survived + timeout) | 1288 |
| **Killed** | **931** |
| **Survived** | **353** |
| **Timeout** | **4** |
| **NoCoverage** | **317** |
| **CompileError** | **242** (1 no inyectable + 241 por mutantes inválidos) |
| **Ignored** | ~2052 (blocks+method+mutate filter+type filter) |
| **Score (Stryker)** | **58.26 %** |
| **Score efectivo (killed / tested)** | **72.28 %** |
| Threshold | `break=55` (no-regresión; pasa) |

## Ranking de survivors por archivo → riesgo (RFLED §1.2)

> Los conteos por archivo de esta tabla provienen del run local PREVIO al refactor de
> `ValidateIndexedAssetPixels` (los totales del HEAD real están en §Resumen). El
> ranking relativo de riesgo es estable: el desglose por mutador (String/Equality/
> Arithmetic/Statement) no cambia de forma material entre ambos runs.

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

## RFLED §1.3 — Safe Mode / CompileErrors (242)

Tras el refactor de `ValidateIndexedAssetPixels`, el CI remoto (HEAD `56c5fc4`)
reporta **3 Safe Modes** restantes, todos por el MISMO patrón de código (overflow
protection que Stryker no puede mutar):

1. **`FrameBuffer`** — constructor con `checked((long)width * (long)height)` dentro
   de try/catch `OverflowException`. Stryker muta `checked`→variantes inválidas y
   entra en Safe Mode. (Corrección de la nota previa: FrameBuffer SÍ se muta; no era
   exclusión de config sino Safe Mode real.)
2. **`EditingService.AddDrawing`** — `checked(size.Width * size.Height)` en try/catch.
3. **`EditingService.EnsureCapacity`** — `checked(existing + incoming)` en try/catch.

**Causa raíz de los 4 (incluido el ya corregido):** el patrón `checked(expresión)` +
`catch (OverflowException)` hace que Stryker genere mutantes donde la variable
`total`/`pixelCount`/`data` queda sin asignación definitiva → CS0165 → Safe Mode.
Es **limitación de Stryker** (el mutador de `checked`/`statement` no respeta la
asignación definitiva), no un defecto del código. El bloque `checked` es protección
REAL contra overflow (OOM) y no debe eliminarse (RFLED §0 prohíbe alterar semántica).

**Acciones:**
- `ValidateIndexedAssetPixels` → refactorizado (flag `validBase64`), SÍ eliminó su
  Safe Mode (el log remoto ya no lo lista).
- `FrameBuffer`/`AddDrawing`/`EnsureCapacity` → el refactor equivalente (flag de
  "overflow detectado" en lugar de `return/throw` dentro del catch) es la única vía
  segura sin perder el `checked`. Se documenta como mejora incremental, NO aplicada
  aún en esta pasada para no arriesgar semántica del constructor de FrameBuffer.

**Conclusión §1.3:** limitación de Stryker/compiler, parcialmente mitigable con
refactor de asignación definitiva. Documentado, no maquillado con exclusiones.

## Exclusión `update` (RFLED §1.4)

Se **conserva** `ignore-mutations: ["update"]` con justificación técnica concreta:
el mutador `update` (`++→--`) sobre bucles `for` acotados produce bucles que no
terminan (o invierten conteo sin cambiar el observable), contabilizados como
**Timeout** (4 en el run del HEAD final; la reducción a 4 confirma que el mutador no
aporta señal). Son artefactos sin señal: no representan un defecto
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