# FINAL AUDIT — v2.md (revisión correctiva final)

Auditoría funcional correctiva según `V2.md`. Estado al CIERRE de esta pasada.

## SHAs

- SHA inicial (registrado al entrar): `4de2261ecfb052399f9a614fba4516220216ba8b`
- SHA final local: `23d9dad9a5b7115690fee7f9a1b6f886d8958a79`
- **CI remoto: GREEN** — GitHub Actions run `33920050735` (todos los jobs + gate
  agregado en verde; ver §2 y §10).

## 1. Causa raíz del R2 (CI Run 33913167064)

**Reproducción:** `tests/e2e/specs/acceptance-r1-r2.spec.js` R2, `#library-grid .card`
no encontrado tras "Guardar en biblioteca" — reproducido localmente SÓLO con
`App_Data` limpio (equivalente al checkout limpio de CI Linux), exactamente igual al
error remoto (`element(s) not found`).

**Causa raíz:** en el spec R2, el `d.set('CorazonR2')` se ejecutaba DESPUÉS de
`page.getByRole('button', { name: 'Guardar en biblioteca' }).click()`. Como
`saveToLibrary()` dispara `window.prompt()` de forma SÍNCRONA (bloquea el event loop),
el prompt se abría inmediatamente con `pending` aún vacío (`''`). El handler
`dialog.accept('')` retornaba texto vacío → `saveToLibrary()` hacía
`if (!name) return` y **nunca emitía el POST** → la biblioteca quedaba vacía
("No hay dibujos guardados"). No es un defecto de producción: es un **bug de orden en
el test** (el prompt síncrono exige fijar el texto ANTES del click; R1 ya lo hacía bien).

**Corrección (sin sleeps/retries/skips):** reordenar `d.set('CorazonR2')` ANTES del
`click()`. Test determinista: fallaba (7.2s) antes del fix, pasa (3.1s) después, con
App_Data limpio.

## 2. CI reestructurado (jobs independientes)

`.github/workflows/ci.yml` ahora es un grafo de jobs independientes — cada gate SIEMPRE
corre (aunque otro falle) y un job final `gates` agrega todos y falla si cualquiera de
los gates obligatorios falló:

build (warnaserror) · test-dotnet · e2e · mutation · coverage (umbral) ·
dependency-audit (dotnet vulnerable + npm audit) · static-analysis · gates.

## 3. Mutation

`stryker-config.json`: `break` pasó de `0` (sin gate) a `55` (no-regresión, basado en el
baseline real). Desglose del run real (ver `MUTATION-JUSTIFICATION.md`):

- total 4069 · killed 868 · survived 325 · timeout 74 · no-coverage 314 ·
  compile-errors 204 · ignored 373 · **score Stryker 59.58%** (killed/tested 68.51%).
- Exclusión `update` **conservada** (justificación técnica: timeouts de bucle infinito,
  cero señal). Añadido `Infrastructure/Security/*` al scope.

## 4. FINAL_AUDIT.md (este archivo)

Actualizado con SHAs reales, número exacto de commits, causa raíz R2, breakdown de
mutación y métricas locales. NO se declara PASS del CI remoto hasta que GitHub Actions
esté GREEN.

## 5. Autosave crash safety

`AtlasProjectStore.AutosaveAsync` ahora hace reemplazo atómico **recuperable**: mueve el
autosave anterior a `.autosave.bak` antes de activar el nuevo, restaura el anterior si
la activación falla, y descarta el backup sólo tras validar el nuevo. Fault-injection:
`AtlasStoreCrashSafetyTests` (2 tests nuevos) inyecta fallo justo en la ventana entre el
retiro del autosave viejo y la activación del nuevo.

## 6. Project path API

`ProjectService.OpenAsync(string path)` ahora exige **containment canónico** vía
`ProjectPaths.EnsureWithin(_projectsRoot, path)`: una ruta exterior se rechaza con
fail limpio. Tests: ruta exterior rechazada + ruta interior aceptada.

## 7. Local security boundary

`LoopbackPolicy` (nuevo): por defecto loopback; `ASPNETCORE_URLS` con interfaz
no-loopback exige opt-in `DS_LETRAS_ALLOW_LAN=true`, si no, **fail-fast** al arranque.
Testeado (`LoopbackPolicyTests`, 4 tests). Uso local normal intacto.

## 8. Cleanup

- `DeployController` ahora inyecta `DeploymentService` por DI (ya no `new`).
- `StubControllers.cs` separado en `PlaybackController.cs` + `SettingsController.cs`.
- `DeviceProtocol`/`DeviceChannels` validan `version >= MinProtocolVersion` (ya no
  aceptan 0 implícito). Tests de versión 0 añadidos.
- `MaxResponseBytes` reducido de 64 MiB → 1 MiB (defendible para escena LED; evita DoS).
- `Configure<IHttpMaxRequestBodySizeFeature>` eliminado (no producía límite global;
  Kestrel `Limits.MaxRequestBodySize` es el límite real).

## 9. Soak + performance (automatizado)

`SoakAndPerformanceTests.cs`: 200 Save/Open (con Δmem + tiempo), 100 Send al simulador
(staging vacío, Δmem), 100 add/delete (equivalentes undo/redo), y performance
render/save/open/compile/send en 16x16/32x16/64x32 con umbrales defensivos. Números
reales vía ITestOutputHelper; umbrales de regresión (no micro-optimización).

## 10. Cierre — resultados locales medidos

- `dotnet build -c Release -warnaserror`: 0 errores, 0 warnings.
- Tests .NET: **536/536 pass**.
- E2E Playwright: **49/49 pass, estable en 3 corridas consecutivas**.
- Dependency audit: `dotnet list package --vulnerable` = 0.
- Coverage: línea **83.41%** (2529/3032), rama **74.57%** (1056/1416).
- Mutation: score **59.58%** (break 55).

### CI remoto (GitHub Actions)

- Run ID: **33920050735** — **GREEN** (Conclusión: `success`).
- Jobs: build (17s), test-dotnet (42s), e2e (2m8s), mutation (11m6s), coverage (39s),
  dependency-audit (28s), static-analysis (28s), **all-mandatory-gates (2s)** — todos ✓.
- El E2E remoto también pasó (R2 corregido), confirmando la causa raíz.

## NO VERIFICADO (requiere hardware físico)

- Transporte físico real (placa LED serie/Ethernet): cubierto por HIL (sockets loopback)
  y targets en memoria; **NO VERIFICADO** contra hardware. Se separa explícitamente
  SIMULATOR VERIFIED vs HARDWARE NOT VERIFIED.

## Pendientes reales / blockers

- **CI remoto: RESUELTO** — run `33920050735` GREEN. No queda pendiente de CI.
- Hardware físico real: BLOCKED (sin dispositivo). Se separa SIMULATOR VERIFIED vs
  HARDWARE NOT VERIFIED (no se declara PASS de hardware).