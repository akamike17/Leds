# FINAL AUDIT — v2.md (cierre)

Auditoría funcional correctiva según `v2.md`. Estado al cierre de esta pasada.

## SHAs

- SHA inicial (registrado al entrar): `0bec382457cdb106a4ac8a1c5c528830bcae4600`
- SHA final local: `8826792c3136cab8bd4be3d14f11718d3ff221ba`
- NO push (según v2.md §0 y §25).

## Commits de esta pasada (v2.md)

| SHA | Descripción |
|---|---|
| `3510dfb` | feat(editing): Group/Ungroup/Align + contrato de capa destino (Tarea 1+2) |
| `f5eb974` | fix(project): cierre con cambios sin guardar Guardar/Descartar/Cancelar (Tarea 3) |
| `bdc5dae` | fix(render): paridad pixel a pixel C#↔JS — elipse + enum Shape (Tarea 4/R5) |
| `59f11d7` | test(R2/R3): 1 DrawingObject = 1 Undo; casos hostiles básicos |
| `4fcf4de` | docs: FINAL_AUDIT.md checkpoint |
| `57efb33` | test(R1/history): R1 exacto en timestamps + historial 100 ops |
| `8826792` | test(escenas/capas + R3): aislamiento + casos hostiles |

## Bugs encontrados → causa raíz → corrección

1. **Group/Ungroup/Align ausentes** (P1). → `GroupId` + `ObjectGroup.Id` + `Scene.Groups`
   + `EditingService.GroupObjects/Ungroup/MoveGroup/AlignObjects` + UI + validación + persistencia.
2. **`EnsureLayer()` siempre primera capa** (P0). → contrato capa destino explícito.
3. **Navegación con cambios perdía trabajo** (P0). → modal Guardar/Descartar/Cancelar.
4. **Divergencia elipse C#↔JS** (R5): C# pintaba interior con stroke (elipse rellena).
   → `if (filled||border) SetPixel(border?stroke:fill)`.
5. **Contrato ShapeKind invertido** (R5): JS Line=1/Rect=0 vs C# Line=0/Rect=1/Ellipse=2
   → rectángulo se compilaba como línea diagonal. Alineado a 0/1/2.

## Matriz MASTER requirement → evidencia → estado

| Requisito | Evidencia | Estado |
|---|---|---|
| Group/Ungroup (ObjectGroup IDs resolubles, no visual) | EditingServiceTests (5) + DrawingPersistence roundtrip + E2E group-align | PASS |
| AlignObjects (6 direcciones) | EditingServiceTests (2) + E2E group-align | PASS |
| Capa destino explícita | EditingServiceTests (2) | PASS |
| Cierre sin guardar Guardar/Descartar/Cancelar | E2E unsaved-close | PASS |
| Paridad C#↔JS RGB24 exacta | E2E parity-r5 (texto/elipse/rect+line/icono) | PASS |
| R1 exacto (timings blink/marquee en timestamps) | R1ExactTests (3) | PASS |
| R2: 1 DrawingObject + 1 Undo | E2E r2-r3 | PASS |
| R3 hostil (undo vacío, doble Send, Save repetido, posición negativa) | E2E r2-r3 + r3-hostile | PASS |
| R4 LastKnownGood transaccional | TransactionalStateMachineContractTests + SimulatorTargetLastKnownGoodTests (ya existentes, verificados) | PASS |
| R5 matriz (texto/elipse/rect/line/icono) | parity-r5 | PASS |
| Historial 100 ops (Undo/Redo/Save/Open) | E2E history-long | PASS |
| Escenas/capas aislamiento | E2E scenes-layers | PASS |

## Resultados

- `dotnet build -c Release`: 0 errores, 0 warnings.
- Tests .NET: 520/520 pass.
- E2E Playwright: 49/49 pass, estable en **3 corridas consecutivas** (sin flakes).
- Dependency audit: `dotnet list package --vulnerable` = 0; `npm audit` = 0.
- Sin `href="#"`, TODO, FIXME, NotImplemented, placeholder ni stub en vistas alcance V1.

## Limitaciones / NO verificado (requiere hardware físico)

- R4 transaccional sobre hardware REAL (placa LED serie/Ethernet/USB/Wi-Fi): cubierto
  por `SimulatorTarget`/`Firmware`/`ChannelDisplayTarget` en tests deterministas de
  máquina de estados + LastKnownGood, NO contra dispositivo físico.
- Soak y performance (v2.md §21/§22) no ejecutados en esta pasada (requieren tiempo
  de ejecución medido); no hay evidencia de fuga/crecimiento en las corridas actuales.

## Siguiente bloque ejecutable (si se continúa)

v2.md §21 Soak (200 Save/Open, 100 Send, 100 Undo/Redo) y §22 Performance (medir
render/save/compile/send en 16x16/32x16/64x32), luego bloque 13 (biblioteca con
transparencia sobre patrones) y 14 (fuentes: golden Ñ/Ü/¿/¡/%/&/@/#).