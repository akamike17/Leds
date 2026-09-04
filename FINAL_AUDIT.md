# FINAL AUDIT — v2.md (checkpoint)

Auditoría funcional correctiva según `v2.md`. Estado al cerrar esta pasada.

## SHAs

- SHA inicial (registrado al entrar): `0bec382457cdb106a4ac8a1c5c528830bcae4600`
- SHA final local: `59f11d707911597671d2f951e160de4abb5d92e2`
- NO push (según v2.md §0 y §25).

## Commits de esta pasada (v2.md)

| SHA | Descripción |
|---|---|
| `3510dfb` | feat(editing): Group/Ungroup/Align + contrato de capa destino (Tarea 1+2) |
| `f5eb974` | fix(project): cierre con cambios sin guardar Guardar/Descartar/Cancelar (Tarea 3) |
| `bdc5dae` | fix(render): paridad pixel a pixel C#↔JS — elipse + enum Shape (Tarea 4/R5) |
| `59f11d7` | test(R2/R3): 1 DrawingObject = 1 Undo; casos hostiles básicos |

## Bugs encontrados → causa raíz → corrección

1. **Group/Ungroup/Align ausentes** (P1). `ObjectGroup` sin Id, sin `AlignObjects`.
   → `GroupId` + `ObjectGroup.Id` + `Scene.Groups` + `EditingService.GroupObjects/
   Ungroup/MoveGroup/AlignObjects` + UI (botones/atajos) + validación + persistencia.
2. **`EnsureLayer()` usaba SIEMPRE la primera capa** (P0, capa equivocada recibe
   objetos). → `EnsureLayer(scene, layer=null)`, métodos insertores aceptan capa
   destino explícita; test con capa activa ≠ primera.
3. **Navegación con cambios perdía trabajo** (P0) — sólo `beforeunload` genérico.
   → modal Guardar/Descartar/Cancelar para nav interna (nav-links + #btn-open).
4. **Divergencia elipse C#↔JS** (R5): el ternario `filled||border ? (border?stroke:
   fill) : stroke` en `SceneRenderer.DrawEllipse` pintaba stroke en el interior
   (elipse rellena); el editor JS pinta anillo. → `if (filled||border)
   SetPixel(border?stroke:fill)`. Goldens actualizados.
5. **Contrato ShapeKind invertido** (R5): JS emitía Line=1/Rect=0/Ellipse=2, pero
   C# `ShapeKind` es Line=0/Rect=1/Ellipse=2 → un rectángulo se compilaba como
   línea (diagonal) y viceversa. → alineado a 0/1/2 en editor-state + renderer JS.
6. **Efecto colateral de UI (no bug, corregido)**: botones group/align en el
   toolbar wrappeaban y desplazaban el canvas a posición subpixel, rompiendo el
   mapeo de coordenadas → movidos al timeline inferior.

## Matriz MASTER requirement → evidencia (bloques cerrados)

| Requisito | Evidencia | Estado |
|---|---|---|
| Group/Ungroup (ObjectGroup, IDs resolubles, no visual) | EditingServiceTests (5), DrawingPersistenceTests roundtrip, E2E group-align | PASS |
| AlignObjects (6 direcciones) | EditingServiceTests (2), E2E group-align | PASS |
| Capa destino explícita | EditingServiceTests (2) | PASS |
| Cierre sin guardar Guardar/Descartar/Cancelar | E2E unsaved-close | PASS |
| Paridad C#↔JS (RGB24 exacto) | E2E parity-r5: texto/elipse/rect+line/icono editor==simulador | PASS |
| R2: 1 DrawingObject + 1 Undo | E2E r2-r3 | PASS |
| R3 (undo/redo vacío, texto vacío, delete sin selección) | E2E r2-r3 | PASS |

## Resultados

- `dotnet build -c Release`: 0 errores, 0 warnings.
- Tests .NET: 517/517 pass.
- E2E Playwright: 41/41 pass (workers:1, framebuffer real + coordenadas exactas).
- Sin errores de consola JS ni HTTP 4xx/5xx (gate dedicado).

## Limitaciones / NO verificado (requiere hardware físico)

- R4 transaccional sobre hardware real (placa LED serie/Ethernet/USB/Wi-Fi):
  cubierto por `SimulatorTarget`/`Firmware`/`ChannelDisplayTarget` en tests
  deterministas, NO contra dispositivo físico.
- Soak/performance (bloques 21/22) y fuzz dirigido (bloque 17) no ejecutados en
  esta pasada; recovery edge (bloque 7.2) ya cubierto por AtlasStoreRobustnessTests.

## Siguiente bloque ejecutable

Continuar v2.md §24 orden: 5. history (100 ops largo), 7. persistence/recovery
edge, 8. R1 exacto (timestamps), 10. R3 hostil completo, 11. R4 transaccional,
12. R5 matriz amplia.