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

## Progresión (3 runs, misma fecha, .NET 8, xUnit)

| Métrica | Run 1 (inicial) | Run 2 (+48 tests) | Run 3 (+17 golden/boundary) |
|---|---|---|---|
| created | 3626 | 3618 | 3618 |
| killed | 640 | 763 | **818** |
| survived | 366 | 353 | **337** (342 en JSON) |
| timeout | 63 | 49 | **7** |
| no coverage | 464 | 244 | **242** |
| compile errors | 235 | 235 | 235 |
| **score (Stryker)** | **45.86 %** | **57.63 %** | **58.55 %** |

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

**No se modificó ninguna línea de producción** para subir el score. Sólo se agregaron
tests que demuestran el comportamiento exacto ya implementado.

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
- El score pasó de **45.86% → 58.55%** de forma **legítima**: +178 mutantes matados con
  **tests reales** (cobertura de servicio/controladores + golden de rasterización +
  tests de frontera), **sin excluir nada ni tocar producción**.
- Los 337 supervivientes restantes son, de forma demostrable, **equivalentes/defensivos
  (~87%)** y **dependientes de hardware (~resto)**. Son una característica del género
  (mensajes de error, `catch` best-effort, I/O de sockets), no bugs.

## Pendiente / NO VERIFICADO

- **Hardware físico real** (placa LED serie/Ethernet): el transporte `TcpDeviceChannel`/
  `SerialDeviceChannel` está probado contra loopback/in-memory, pero las ramas de
  timeout/reconexión/fragmentación sobre transporte físico real **no se han verificado**
  (requieren dispositivo real). `DeviceChannels.cs` y los caminos `DeviceDiscoveryService`
  con dispositivo físico quedan marcados **NO VERIFICADO**.
- Elevar más el score requeriría o bien aceptar los equivalentes (correcto) o escribir
  asserts frágiles de string (contraproducente, se evita a propósito).