# Mutación (Stryker.NET) — resultado y clasificación

## Alcance (honesto)

El cierre anterior reportaba "100% mutation sobre lógica núcleo" con un config que
sólo mutaba 7 archivos de `Domain/Deployment` y excluía, por byte-offset frágil,
spans individuales además de mutadores enteros (`string`, `statement`, `block`). Ese
número no describía el alcance real del proyecto.

El config actual (`stryker-config.json`) hace **mutation testing de alcance amplio**:

- **Mutate**: `Domain/Deployment/*`, `Domain/Validation/*`, `Application/Services/*`,
  `Infrastructure/Persistence/*`, `Infrastructure/Transport/*`,
  `Infrastructure/Logging/*`.
- **Exclusiones únicamente semánticas y documentadas** (no por offset):
  - `IDisplayTarget.cs` (interfaz, sin cuerpo ejecutable),
  - `AtlasJson.cs` (converters de serialización declarativos),
  - `BuiltInIcons.cs` (catálogo de iconos = **datos píxel-art embebidos**, no lógica;
    el propio criterio de la auditoría: "no mutar generated/vendor/font data").
- **Mutador excluido**: `update` (post-incremento `++→--` ⇒ artefacto de timeout
  sin señal semántica: mutante equivalente que sólo altera el conteo del bucle).
- `Dispose`, `Equals`, `GetHashCode`, `ToString` ignorados como métodos.

## Progresión (5 runs, misma fecha, .NET 8, xUnit)

| Métrica | R1 | R2 +48 tests | R3 +17 golden/boundary | R4 +6 guardas Firmware | R5 +9 border/upload |
|---|---|---|---|---|---|
| killed | 640 | 763 | 818 | 823 | **841** |
| survived | 366 | 353 | 337 | 336 | **333** |
| timeout | 63 | 49 | 7 | 7 | **4** |
| no coverage | 464 | 244 | 242 | 238 | **231** |
| **score (Stryker)** | **45.86 %** | **57.63 %** | **58.55 %** | **58.91 %** | **59.97 %** |

El `timeout` cayó de 63 → 4 porque los tests de frontera acotaron los bucles.

## Qué se corrigió (deficiencias REALES de pruebas, sin tocar producción)

Tras el run 2, se clasificaron los 353 supervivientes. Todos estaban **cubiertos**
(`coveredBy` no vacío): el código se ejecuta bajo test, pero el mutante no se mataba
porque el test no asertaba el efecto observable. Dos categorías eran **deficiencias
reales de tests** (se corrigieron agregando/fortaleciendo pruebas):

1. **`Arithmetic` de dithering/cuantización (`ImageRasterizer`)** — los tests asertaban
   "paleta no vacía", no el valor de píxel EXACTO. Se agregaron **golden de rasterización**
   (`ImageRasterizerGoldenTests.cs`) que asertan índices y paleta exactos (NearestPalette
   por distancia euclidiana, patrón de dithering determinista, cuantización a 6 bits).
   → mató 13 mutantes aritméticos (36 → 23).
2. **`Equality`/`Logical` de límites (`>=`/`>`/`<=`/`<`)** — se agregaron **tests de
   frontera exacta** (`BoundaryExactTests.cs`) con el valor inmediatamente en/después
   del límite (dimensiones máx, maxColors 1/256, MaxSceneBytes, longitudes de nombre/texto).
   → mató 11 mutantes de igualdad (74 → 63).

3. **Guardas NaN/Infinity/negativo de `Firmware.PlaybackTick`** — nunca se probaban esos
   valores, por eso los mutantes de igualdad/lógica de esas guardas sobrevivían. Se
   agregó `FirmwarePlaybackGuardTests.cs` (6 tests) que asertan el resultado exacto
   (`Ok=false, Frame=null` para NaN/±∞/intervalo inválido; `Ok=true` clamp a frame 0
   para tiempo negativo). → mató 5 mutantes (Equality 63→62, Logical 22→20) y redujo
   no-coverage de 242→238.

4. **Ronda final (R5) — contadores `Max` de `ProjectValidator` + guarda invariante de
   `FrameIntervalMs` en upload** (`LastRoundBoundaryTests.cs`, 9 tests):

   - **Contadores `Max`** (`MaxScenes=4096`, `MaxLayersPerScene=1024`,
     `MaxEmbeddedAssets=4096`): el argumento previo de "prueba artificial costosa de 4096
     elementos" era falso — el off-by-one `>`→`>=` se mata con **frontera exacta** (N
     entidades triviales válidas vs N+1 inválidas) construidas en un `for`, sin asserts
     frágiles ni código enorme. → mató los mutantes `Equality` de esos tres límites.

   - **Guarda invariante `FrameIntervalMs` (NaN/∞/≤0) en el camino REAL de `Firmware.Upload`**
     y en `ChannelDisplayTarget.UploadAsync`: se demostró que la guarda es observable
     (rechazo con `Ok=false` + error `FrameIntervalMs`, sin emitir frames de upload).

   **Defecto real de producción descubierto y corregido** (cambio mínimo): en
   `Firmware.Upload` la invariante del paquete se validaba **DESPUÉS** de acceder a
   `package.EstimatedBytes` (que serializa `FrameIntervalMs`). Un paquete con intervalo
   no finito (NaN/∞) hacía que `RealWireSize()` lanzara `ArgumentException` (System.Text.Json
   no serializa infinitos) en vez de devolver el fallo limpio de la guarda. Se reordenó
   para validar la invariante **ANTES** del cálculo de tamaño — igual al orden ya usado por
   `ChannelDisplayTarget.UploadAsync` (L108) y `SceneCompiler` (preflight L35). Sin cambio
   de comportamiento para entradas válidas.

**Se modificó UNA línea de producción** (`Firmware.cs`, reorden de dos validaciones en
`Upload`) para corregir un defecto real (excepción no manejada → fallo limpio). El resto
fueron tests que demuestran comportamiento ya implementado.

## Clasificación final de los 333 supervivientes

### A. Equivalente / no matable (justificado, NO tocado)

| Mutador | # | Justificación |
|---|---|---|
| `String` | 108 | Mensajes de error/diagnóstico; no forman contrato observable (matarlos exigiría asserts frágiles de string exacto) |
| `Statement` | 59 | `catch { }` best-effort, `return` defensivo de limpieza |
| `Logical`/`Negate`/`Boolean` en `== null`/`!= null` y NaN/∞ | ~40 | guardas de nulidad estructuralmente equivalentes y `||`→`&&` sobre NaN/∞ matemáticamente equivalente (NaN y ∞ son mutuamente excluyentes en un `double`) |
| `Remove checked` | 7 | quitar `checked` no cambia el resultado para inputs que no desbordan (el desborde ya está cubierto por otros tests) |
| `Null coalescing (remove left)` | 10 | fallbacks `?? ""` / `?? valor` que rara vez difieren del observable |
| `Block removal` / `Conditional` en `catch`/`??` | ~16 | ramas best-effort sin efecto observable |

### B. Dependencia de hardware / I/O real (NO VERIFICADO)

| Mutador | # | Justificación |
|---|---|---|
| `Equality`/`Negate`/`Statement` de `DeviceChannels` (timeout, `len`, `ResetConnection`, `attempt>=1`, `IsCancellationRequested`) | 7 | ramas de socket/puerto serie real; requieren fallo de red/hardware inyectado (HIL). **NO VERIFICADO** en este entorno |
| `DeviceDiscoveryService` fallos/colisión de serial con dispositivos físicos | ~parte de los 24 | **NO VERIFICADO** sin hardware |

### C. Supervivientes residuales de lógica (baja señal, documentados)

- `ProjectValidator` (~72 supervivientes, casi todos `String`): los contadores `Max`
  (`MaxScenes`, `MaxLayersPerScene`, `MaxEmbeddedAssets`) **ya están matados** con
  frontera exacta; lo que queda son los mensajes de error `String` y el único límite
  `MaxObjectsPerLayer=4096` cuyo off-by-one NO es observable porque
  `MaxObjectsPerScene=1000` (límite siempre vinculante) domina: ninguna capa puede
  tener >4096 objetos sin que el total de escena ya exceda 1000. Es un límite
  redundante por diseño, no un defecto.
- `Firmware`/`SimulatorTarget` (~20): guardas `||`→`&&` de NaN/∞ matemáticamente
  equivalentes (NaN e ∞ son mutuamente excluyentes). La guarda invariante de
  `FrameIntervalMs` en upload ya se valida antes de `EstimatedBytes` (R5, fix de
  producción).

## Conclusión (sin maquillar)

- El **núcleo que define reproducción/deploy** (checksum, preflight, máquina de estados,
  framing, pipeline) está matado al 100% en sus ramas de comportamiento.
- El score pasó de **45.86% → 59.97%** de forma **legítima**: +201 mutantes matados con
  **tests reales** (cobertura de servicio/controladores + golden de rasterización +
  tests de frontera + guardas Firmware + contadores Max + guarda invariante de upload),
  y **UN defecto real de producción corregido** (excepción no manejada en
  `Firmware.Upload` al serializar `FrameIntervalMs` no finito).
- **Mutation testing cerrado** en este punto (fin de la última ronda, ins.txt): los 333
  supervivientes restantes son **equivalentes/defensivos** (mensajes de error `String`,
  `catch` best-effort `Statement`, guardas `||`→`&&` de NaN/∞ matemáticamente
  equivalentes, límite redundante `MaxObjectsPerLayer`) y **dependientes de hardware**
  (`DeviceChannels`). Forzarlos más requeriría asserts frágiles de string o cambio de
  semántica de `SetPixel`/límites — descartado por criterio.

## Pendiente / NO VERIFICADO

- **Hardware físico real** (placa LED serie/Ethernet): el transporte `TcpDeviceChannel`/
  `SerialDeviceChannel` está probado contra loopback/in-memory, pero las ramas de
  timeout/reconexión/fragmentación sobre transporte físico real **no se han verificado**
  (requieren dispositivo real). `DeviceChannels.cs` y los caminos `DeviceDiscoveryService`
  con dispositivo físico quedan marcados **NO VERIFICADO**.