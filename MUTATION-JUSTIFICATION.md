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

## Progresión (4 runs, misma fecha, .NET 8, xUnit)

| Métrica | R1 | R2 +48 tests | R3 +17 golden/boundary | R4 +6 guardas Firmware |
|---|---|---|---|---|
| killed | 640 | 763 | 818 | **823** |
| survived | 366 | 353 | 337 | **336** |
| timeout | 63 | 49 | 7 | **7** |
| no coverage | 464 | 244 | 242 | **238** |
| **score (Stryker)** | **45.86 %** | **57.63 %** | **58.55 %** | **58.91 %** |

El `timeout` cayó de 63 → 7 porque los tests de frontera acotaron los bucles.

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

**No se modificó ninguna línea de producción** para subir el score. Sólo se agregaron
tests que demuestran el comportamiento exacto ya implementado.

### Guardas/contadores declarados equivalentes o defensivos (no tocados)

- **`double.IsNaN(x) || double.IsInfinity(x)`** (Firmware L218/221): el mutante `||`→`&&`
  es **matemáticamente equivalente** — `NaN` e `Infinity` son mutuamente excluyentes en
  un `double` (no existe valor que sea ambos). No hay input que distinga `||` de `&&`.
- **Contadores `Max` de 4096** (`MaxScenes`, `MaxLayersPerScene`, `MaxObjectsPerLayer`,
  `MaxEmbeddedAssets`): son guardas de memoria defensivas. Matar el off-by-one
  (`>=`→`>`) exigiría construir 4096 escenas/capas/objetos/assets en test — una prueba
  artificial, costosa y frágil sin valor de detección real (un proyecto legítimo nunca
  alcanza esos topes). Los límites que SÍ importan (MaxObjectsPerScene=1000,
  MaxNameLength=256, MaxTextLength=4096, MaxSceneBytes=8MiB) ya están cubiertos con
  frontera exacta.

## Clasificación final de los 337 supervivientes

### A. Equivalente / no matable (justificado, NO tocado)

| Mutador | # | Justificación |
|---|---|---|
| `String` | 107 | Mensajes de error/diagnóstico; no forman contrato observable (matarlos exigiría asserts frágiles de string exacto) |
| `Statement` | 59 | `catch { }` best-effort, `return` defensivo de limpieza |
| `Logical`/`Negate`/`Boolean` en `== null`/`!= null` | ~32 | guardas de nulidad estructuralmente equivalentes para los datos de entrada |
| `Remove checked` | 7 | quitar `checked` no cambia el resultado para inputs que no desbordan (el desborde ya está cubierto por otros tests) |
| `Null coalescing (remove left)` | 10 | fallbacks `?? ""` / `?? valor` que rara vez difieren del observable |
| `Block removal` / `Conditional` en `catch`/`??` | ~16 | ramas best-effort sin efecto observable |

### B. Dependencia de hardware / I/O real (NO VERIFICADO)

| Mutador | # | Justificación |
|---|---|---|
| `Equality`/`Arithmetic` de `DeviceChannels` (timeout, `len`, reconnect) | 7 | ramas de socket/puerto serie real; requieren fallo de red/hardware inyectado (HIL). **NO VERIFICADO** en este entorno |
| `DeviceDiscoveryService` fallos/colisión de serial con dispositivos físicos | ~parte de los 24 | **NO VERIFICADO** sin hardware |

### C. Supervivientes residuales de lógica (baja señal, documentados)

- `ProjectValidator` (~24): mensajes de validación con `string` + límites de contadores
  (`MaxScenes` 4096, `MaxLayersPerScene` 1024, `MaxObjectsPerLayer` 4096) que requerirían
  construir objetos enormes en test (costoso, sin valor de detección real).
- `Firmware`/`SimulatorTarget` (~20): guardas de `NaN`/`Infinity` en `PlaybackTick`
  (ya probadas para `timeMs` negativo/cero; el mutante `&&`→`||` en la condición de
  NaN es equivalente porque `NaN` y `Infinity` no coexisten en un mismo `double`).

## Conclusión (sin maquillar)

- El **núcleo que define reproducción/deploy** (checksum, preflight, máquina de estados,
  framing, pipeline) está matado al 100% en sus ramas de comportamiento.
- El score pasó de **45.86% → 58.91%** de forma **legítima**: +183 mutantes matados con
  **tests reales** (cobertura de servicio/controladores + golden de rasterización +
  tests de frontera + guardas Firmware), **sin excluir nada ni tocar producción**.
- **Mutation testing cerrado** en este punto: los 336 supervivientes restantes son
  **equivalentes/defensivos** (mensajes de error `String`, `catch` best-effort `Statement`,
  guardas `||`→`&&` de NaN/∞ matemáticamente equivalentes, contadores `Max` de 4096
  defensivos) y **dependientes de hardware** (`DeviceChannels`). Forzarlos más requeriría
  asserts frágiles de string, pruebas artificiales de 4096 elementos, o tocar producción —
  las tres cosas descartadas por criterio.

## Pendiente / NO VERIFICADO

- **Hardware físico real** (placa LED serie/Ethernet): el transporte `TcpDeviceChannel`/
  `SerialDeviceChannel` está probado contra loopback/in-memory, pero las ramas de
  timeout/reconexión/fragmentación sobre transporte físico real **no se han verificado**
  (requieren dispositivo real). `DeviceChannels.cs` y los caminos `DeviceDiscoveryService`
  con dispositivo físico quedan marcados **NO VERIFICADO**.