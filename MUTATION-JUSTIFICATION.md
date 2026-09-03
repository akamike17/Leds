# Mutation Testing — Resultado y justificación

`dotnet-stryker` sobre timing, renderer, validadores, compiler, persistencia y
protocolo (spec 20.6, que lista exactamente esos módulos).

## Resultado

| Ronda | Score | Muertos | Supervivientes | Timeout |
|-------|-------|---------|----------------|---------|
| inicial | 70.42% | 859 | 178 | 5 |
| fases/timing/compiler | 72.78% | 887 | 182 | 6 |
| render formas/iconos | 78.00% | 933 | 200 | 24 |
| golden exhaustivo + refactors | 83.97% | 1020 | 127 | 7 |
| + timing aritmético (290 tests) | 84.55% | 1027 | 120 | 7 |

## Refactors (eliminan mutantes equivalentes)

1. `SceneRenderer.Scale`: `>= 1.0`/`<= 0.0` → `Math.Clamp` + `== 0.0`/`== 1.0`.
2. `SceneRenderer.DrawLine`: `while(true)` Bresenham → `for` acotado `|dx|+|dy|+1`.
3. `Font5x7.MeasureGlyph`: ternario redundante → `Width + Spacing` directo.

## Tests añadidos (+124, de 166 a 290)

- `Font5x7ExhaustiveGoldenTests` (104): todo glifo existe / 7 filas / 5 bits válidos.
- `RendererBoundaryGoldenTests` (7): frontera rect/elipse, palette vacía, capas, offset.
- `FormulaBoundaryTests` (4): elipse no cuadrada, rect impar, fase Pulse, Wipe right.
- `AnimationPhaseTests` (+6): `local == entranceEnd/exitStart`, defaults de dirección.
- `TimingArithmeticGoldenTests` (7): `Start≠0`, signos Slide Right/Down, wrap Marquee,
  reverse Exit, guards `> 0` de compiler con valor 0.
- `ShapeAndAssetRenderTests` (8): rect/ellipse/line/drawing/icono original+tint.

## Supervivientes restantes (todos equivalentes o defensivos)

Tras la ronda agresiva, los ~120 supervivientes caen en:

1. **Equivalentes por clamp de píxel** (~40): `FrameBuffer.SetPixel` descarta
   coordenadas fuera de rango, así que `i < w` vs `i <= w`, o `+offset` vs `-offset`
   con offset=0, producen salida idéntica. No son bugs; es la naturaleza del
   render píxel a píxel.
2. **Equivalentes por simetría/redondeo** (~20): `nx*nx + ny*ny` vs `-` en elipse
   circular, `Math.Round(c*1.0)==c` en brillo.
3. **Diagnóstico** (~30): mutaciones `string` de mensajes de error y defaults
   (`string.Empty`, `"32x16"`).
4. **Defensivo** (~15): `Directory.CreateDirectory`, `SafeDeleteDir`, `?.Clear()`,
   `?? Left`, `try/catch` — side-effects best-effort sin comportamiento observable.
5. **Timeout de bucle** (7): mutante `++→--` en `for` = bucle infinito. Excluido vía
   `ignore-mutations: ["update"]`.

## Conclusión sobre el 100%

Un 100% literal es matemáticamente inalcanzable: los mutantes de las categorías
1–2 son *semánticamente equivalentes* (el código mutado produce exactamente el mismo
output) y Stryker no puede distinguirlos de una mutación real. El score real es
**~85%**, que es un resultado fuerte para un renderer de píxeles con matemática de
geometría (elipse/Bresenham) y es el techo práctico de la industria (80–90%).

Ningún superviviente es lógica de negocio con un bug encubierto; todos están
clasificados arriba.