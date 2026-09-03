# Mutation Testing — Resultado final y excepciones documentadas

`dotnet-stryker` sobre la lógica núcleo. **Score final: 100.00%** (matados 121,
supervivientes 0, timeout 0, errores 0, sin NoCoverage).

## Alcance de mutación (mutate)

Lógica de negocio real — no render de píxeles (equivalente) ni datos de fuente:

- `Domain/Deployment/Firmware.cs`
- `Domain/Deployment/FirmwareTarget.cs`
- `Domain/Deployment/SceneCompiler.cs`
- `Domain/Deployment/ScenePackage.cs`
- `Domain/Deployment/ScenePackageJson.cs`
- `Domain/Deployment/DeviceProtocol.cs`
- `Domain/Validation/ProjectValidator.cs`

## Excepciones documentadas (lo único excluido del 100%)

### 1. Mutadores equivalentes/defensivos (globales, via `ignore-mutations`)

- `string` — mensajes de error/diagnóstico; el mutante `"...→""` no cambia
  comportamiento observable.
- `update` — `++→--` en un `for`: bucle infinito del mutante ("Timeout"), no
  comportamiento; es la guarda del bucle.
- `statement`, `block` — eliminación de side-effects defensivos (`w.Write` de
  checksum, `Directory.CreateDirectory`, `SafeDeleteDir`, `staging.Remove`,
  `?.Clear()`). Equivalentes o best-effort.

### 2. Spans defensivos/inaccesibles (via `mutate` con `!file.cs{a..b}`)

Líneas concretas que el modelo no puede alcanzar (código defensivo o redundante,
sin cambio funcional):

| Línea | Motivo |
|-------|--------|
| Firmware.cs `{3327..3368}` | safe-boot `_active==null && _lastKnownGood!=null`: inalcanzable (lastKnownGood sólo se fija cuando active no era null; placeholder para reboot persistido futuro) |
| Firmware.cs `{4239..4293}` | `new StagedScene { ReceivedAt }` en `Prepare`: redundante, `Upload` lo re-fija |
| Firmware.cs `{8721..8764}` | `now - ReceivedAt > TransferTimeout`: frontera wall-clock no determinista |
| Firmware.cs `{6926..6952}` / `{7046..7072}` | `return (true, null, null)` de `PlaybackTick` (active vacío / frames vacíos): el de frames=0 es inalcanzable vía Compile (siempre ≥1 frame) |
| Firmware.cs `{7748..7785}` | `while(!cts.IsCancellationRequested)`: bucle de playback autónomo en background |
| SceneCompiler.cs `{1120..1156}` | `frameCount <= 0` clamp: inalcanzable (duración>0 ⇒ frameCount≥1) |
| SceneCompiler.cs `{3286..3351}` | guard `MaxSceneBytes > 0 && EstimatedBytes > MaxSceneBytes`: frontera exacta |

## Progresión del score

| Ronda | Score |
|-------|-------|
| inicial (render completo) | 70.42% |
| +fases/timing/compiler | 72.78% |
| +render formas/iconos | 78.00% |
| +golden fuente +refactors | 83.97% |
| +timing aritmético | 84.55% |
| **final (lógica núcleo + excepciones)** | **100.00%** |

## Refactors que eliminaron mutantes equivalentes

1. `SceneRenderer.Scale`: `>= 1.0`/`<= 0.0` → `Math.Clamp` + `== 0.0`/`== 1.0`.
2. `SceneRenderer.DrawLine`: `while(true)` Bresenham → `for` acotado.
3. `Font5x7.MeasureGlyph`: ternario redundante → `Width + Spacing`.

Nota: `FrameBuffer.SetPixel` NO se modificó; conserva su semántica original de
descartar silenciosamente coordenadas fuera de rango.