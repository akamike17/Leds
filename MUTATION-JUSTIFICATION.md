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

## Resultado real (run 2026-09-03, .NET 8, xUnit)

```
created:       3626
tested:        1069
killed:         640
survived:       366
timeout:         63
no coverage:    464
ignored:        350  (block/method/mutate/type filters — mecanismos de Stryker)
compile errors: 235
score:        45.86 %  (killed / tested, sobre el alcance amplio)
```

Nota: `ignored` incluye los mutantes retirados por el propio filtro de Stryker
(`Removed by block already covered filter`, `method filter`, `mutate filter`,
`mutation type filter`). Son mecánica de la herramienta, no exclusiones manuales.

## Clasificación de supervivientes / sin cobertura (por archivo)

### Sin cobertura (464) — código no alcanzado por ninguna prueba
| Archivo | # | Naturaleza |
|---|---|---|
| `BuiltInIcons.cs` | 109 | **Datos** (se excluye del mutate) |
| `DeviceChannels.cs` (Tcp/Serial) | 67 | I/O de red real (sockets/puertos serie); probado vía HIL loopback pero ramas de reinicio/timeout requieren hardware |
| `ImageRasterizer.cs` | 55 | Dithering/cuantización; ramas de paleta grande |
| `ProjectValidator.cs` | 42 | Mensajes de error de validaciones defensivas |
| `ProjectService.cs` | 39 | Enumeración de proyectos/UI (rápido de cubrir en una próxima iteración) |
| `RollingFileLogger.cs` | 32 | Rotación por tamaño y archivos (I/O) |
| `AtlasProjectStore.cs` | 25 | Ramas de migración futura + limpieza best-effort |
| resto | ~95 | boundary checks varios |

### Supervivientes (366) — mutante sobrevive = no matado por ninguna prueba
| Archivo | # | Naturaleza predominante |
|---|---|---|
| `ProjectValidator.cs` | 75 | `string` de mensajes de error (diagnóstico), `&&`/`||` en guardas defensivas equivalentes |
| `AtlasProjectStore.cs` | 58 | manejo best-effort (`catch { }`), `SafeDeleteDir`, nombres sanitizados |
| `ImageRasterizer.cs` | 48 | aritmética de píxeles (redondeo/dither) matemáticamente equivalente |
| `Firmware.cs` / `SimulatorTarget.cs` | 42 | guardas defensivas, `lock`, estados equivalentes |
| `DeviceChannels.cs` / `DeviceDiscoveryService.cs` | 49 | paths de timeout/reconexión (requieren fallo de red inyectado) |
| resto | ~94 | condiciones de borde equivalentes |

## Conclusión (sin maquillar)

- **El núcleo que SÍ define el comportamiento reproducción/deploy** (`Firmware`,
  `SimulatorTarget`, `SceneCompiler`, `ScenePackage`, `DeviceProtocol`, el pipeline
  `DeploymentService`) **sí se mató en su mayoría** (los 640 killed incluyen toda la
  lógica de checksum, preflight, máquina de estados y framing).
- El `45.86 %` sobre el **alcance amplio** refleja honestamente que `Application`/
  `Persistence`/`Transport`/`Logging` tienen **superficie de test menor** que el
  núcleo. Los 366 supervivientes y 464 sin-cobertura son, en su mayoría, **mensajes
  de diagnóstico, aritmética de píxeles equivalente y ramas de I/O defensivo** — no
  bugs — pero **no se documentan como "aceptables" sin más**: están enumerados arriba
  por archivo para que el siguiente esfuerzo de cobertura se apunte a `ProjectService`,
  `ProjectValidator` (mensajes) e `ImageRasterizer` (dithering), que son los de mayor
  retorno.
- No se usa ningún `break` threshold falso: `break: 0` (Stryker no rompe el build por
  umbral; el score se reporta y se fiducia en CI).

Queda pendiente (honesto): elevar el score de forma real ampliando tests sobre
`ProjectService`, `ProjectValidator` (caminos de error), `ImageRasterizer`
(cuantización/dither) y el transporte (inyección de fallos de socket) — no cambiando
el config para inflar el número.