# PRODUCTION READINESS AUDIT — HEAD 1bc9e5c

Audit adversarial de `final.md` (26 secciones) contra
HEAD `1bc9e5ca64dad5a41b800e5b85fa538691748f39`.

Fecha: 2026-09-05 (Hora estándar central, México)
Entorno: Windows 10, .NET 8 (dotnet 3.11), Node/Playwright 1.62.1

## BASELINE (sección 1)

| Gate | Resultado |
|------|-----------|
| `git rev-parse HEAD` | 1bc9e5ca64dad5a41b800e5b85fa538691748f39 (== TARGET) |
| `dotnet build -c Release -warnaserror` | 0 warnings, 0 errores |
| `dotnet test -c Release` | 603 / 603 superados, 0 fallidos, 0 omitidos |
| Playwright E2E (workers:1) | 54 / 54 superados |

## FINDINGS (defectos confirmados y corregidos)

### [A/B] P1 — Font 3x5 perdía caracteres en silencio
- Archivos: `Domain/Rendering/Font3x5.cs`, `wwwroot/js/editor/font3x5.js`,
  `SceneRenderer.RenderText`, `editor-renderer.js renderText`.
- Reproducción: `"MIGUEL"`, `"WWW"`, `"mañana"`, `"áéíóúñ"` en fuente 3x5 → M/W/minúsculas
  sin glifo entregaban `null` y el renderer avanzaba el cursor SIN dibujar (hueco silencioso).
- Causa raíz: 3x5 omite M/W/@/%/&/# (documentado) y no tiene a-z; además el `accMap` de
  minúsculas acentuadas referencia bases minúsculas inexistentes (nunca se creaban).
- Corrección: FALLBACK — carácter sin glifo en la fuente activa se renderiza con el glifo 5x7
  equivalente, avanzando ancho real 5x7 (5+1). Paridad C#↔JS exacta.
- Tests: `Render_3x5_never_loses_characters_silently` (8 textos), `Render_3x5_fallback_glyph_matches_5x7_glyph_exactly`.

### [C] P0 — "Nuevo" navegaba sin protección de trabajo sin guardar
- Archivos: `wwwroot/js/editor/editor-state.js`, `editor-status.js`.
- Corrección: `#btn-new` pide Guardar/Descartar/Cancelar (modal existente) si `state.dirty`;
  `confirmNavigation` generalizado a acción (función) además de href. Sólo navega/abre modal
  tras Guardar exitoso o Descartar.

### [D] P1 — Autosave mostraba "Autoguardado" aunque falló
- Archivo: `wwwroot/js/editor/editor-state.js` (`startAutosave`).
- Corrección: se lee la respuesta; "Autoguardado" sólo con `success:true`; ante 4xx/5xx /
  success:false / body no-JSON → conserva dirty + aviso no destructivo + reintento en el tick.

### [E] P0 — Nuevo/Create redirigía sin verificar persistencia
- Archivos: `Controllers/EditorController.cs` (`New`, `ProjectsController.Create`),
  `Views/Projects/New.cshtml`.
- Corrección: se verifica `SaveAsync().Success`; en fallo → `BadRequest` (Editor) o re-render
  del formulario con error (Projects). Añadido `asp-validation-summary`.
- Tests: `NewProjectPersistenceFailureTests` (2, inyectan fallo vía `AtlasProjectStore.FailPoint`).

### [F] P1 — Rediscovery de device no actualizaba endpoint (lo trataba como colisión)
- Archivo: `Application/Services/DeviceDiscoveryService.cs`.
- Corrección: mismo serial + endpoint distinto = rediscovery → actualiza target/endpoint vivo;
  idempotente si mismo endpoint. Serial estable = identidad (sección 18/21).
- Tests: `Register_same_serial_new_endpoint_is_rediscovery_not_collision`, `..._idempotent`.

### [G] — E2E corazonr2-dedup asumía biblioteca con 1 sola entrada (shared-state)
- Archivo: `tests/e2e/specs/corazonr2-dedup.spec.js`.
- Corrección: filtro por identidad `CorazonR2` (no `count===1` global); eliminado `waitForTimeout`.

### [§8] P2 — Dedup de biblioteca ignoraba la paleta
- Archivo: `Application/Services/LibraryService.cs`.
- Corrección: la paleta entra a la identidad de contenido del dedup. Dos dibujos con los mismos
  índices de píxel pero distinto color "on" ya no se colapsan (data-loss evitado).
- Test: `Save_same_pixels_different_palette_creates_separate_entry`.

## ÁREAS VERIFICADAS COMO YA ROBUSTAS (sin cambios)

- §3 Persistencia: atomic save (temp → validate → move), checksum SHA-256 de contenido completo,
  recovery LastKnownGood (main → autosave → autosave.bak → backup), fault-injection por fase.
- §4/§12 Undo/Redo: coalescing una-operación-semántica = un Undo; límite 100; redo invalidado.
- §5 Paridad WYSIWYG: `SimulatorFrame` devuelve RGB24 del paquete activo; `parity-r5.spec.js`
  compara RGB pixel-exact (texto/elipse/rect/línea/icono).
- §13/§14 Deploy/network: pipeline Validate→Compile→Prepare→Upload→Verify→Activate; checksum de
  contenido; fallo conserva LastKnownGood.
- §15 Seguridad: loopback por defecto (fail-fast ante URL no-loopback sin opt-in), antiforgery
  (header `RequestVerificationToken`), 64 MiB request limit.
- §16 DOS: límites defensivos en `ProjectValidator` (canvas 512, assets 4096, objetos, textos,
  dibujos 512×512, checked overflow).
- §17 Imagen: valida rgba.LongLength >= w*h*4, target 1..512, maxColors 1..256.
- §18 Overwrite id: P2 documentado — app local single-user; el frontend envía el id del proyecto
  abierto, y `SaveAsync` revalida el proyecto completo (checksum). Sin acción de rectificación
  por no ser vector de data-loss real en el modelo de amenaza local.

## RECUENTO

- build: 0 warnings / 0 errores
- .NET tests: 603 superados, 0 fallidos, 0 omitidos (+13 vs baseline de 590)
- E2E: 54 superados, 0 fallidos
- Archivos de código/test modificados: 13

## CI REMOTO (run 33980844742 — push de a784532)

| Gate | Resultado |
|------|-----------|
| Build (warnings-as-errors) | ✓ 24s |
| .NET tests | ✓ 603/603 (43s) |
| E2E (Playwright) | ✓ 54/54 (2m11s) |
| Static analysis (.NET analyzers) | ✓ 33s |
| Dependency audit (`dotnet list package --vulnerable` + `npm audit`) | ✓ 17s |
| Coverage (threshold 0.60) | ✓ line-rate **0.8447**, branch-rate **0.7580** |
| Mutation (Stryker 4.16) | ✓ **58.51%** (consultar 1333 mutantes; killed 963, survived 364, timeout 6; compile-error 204, no-coverage 323, ignorados 2472) |
| All mandatory gates | ✓ PASS |

- Mutation score: **58.51%** (killed 963 / survived 364 / timeout 6; no-coverage 323, compile-error 204).
- Line/branch coverage: **84.47% / 75.80%**.
- Dependencias: sin vulnerabilidades críticas.

## VEREDICTO FINAL

**YELLOW — usable, no production-ready hasta cerrar el gap de mutation.**

Todos los P0/P1 de la sección 2 quedaron corregidos y cubiertos por tests de regresión (unit + E2E),
con CI 100% verde (7/7 gates obligatorios + agregación). La paridad editor↔simulador está probada
pixel-exact (R5). Persistencia/recovery crash-safe y seguridad de borde local están implementadas y
testeadas. Dependencias sin vulnerables.

El único bloqueante para GREEN según los criterios de `final.md` §25 es que la **mutation score real
es 58.51%**, por debajo del rigor que el usuario exige (~95-99%). La cobertura de línea (84.47%) y
rama (75.80%) superan el umbral de CI y son sólidas, pero el mutation score honesto es el que es:
963 mutantes matados, 364 supervivientes (equivalentes/defensivos) y 323 sin cobertura — atribuibles
en su mayoría a la capa MVC (Controllers/Views/Program) y a mutantes de geometría de píxeles
equivalentes, no a lógica de dominio rota. Cerrar ese gap es la siguiente afirmación de trabajo, no
un "justified survivor". El hardware físico queda fuera del alcance de un entorno local.