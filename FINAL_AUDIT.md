# FINAL AUDIT — RFLED.md (auditoría final profunda)

Auditoría correctiva de ingeniería según `RFLED.md`. Estado al cierre.

## SHAs

- SHA inicial: `7de0a89125e8f54379d70f25d32ff5885c045b25`
- SHA final: `56c5fc4ea83f6c45f61de8703bd77f155b3a6fed`
- CI final: **GREEN** — run `33936997917` (conclusión `success`; todos los jobs + gates ✓)

## Identidad

- PROJECT: DSLetreros (Atlas LED)
- REPO: `akamike17/Leds`
- BRANCH: `master`

## Bugs corregidos (causa raíz → fix mínimo → test)

1. **Recovery no consultaba `.autosave.bak` (RFLED §2.1, P0).**
   `TryRecoverAsync` sólo consultaba `path+.autosave` y `path+.bak` (que es el backup
   del PRINCIPAL). Un autosave dejado corrupto tras crash post-move quedaba irrecuperable
   aunque su `.autosave.bak` (backup del autosave anterior) fuera válido.
   → Fix: se añade `path+.autosave.bak` como 3er nivel de recovery (principal → autosave →
   autosave.bak → main.bak). Test: `AtlasStoreRecoveryMatrixTests` (5 tests, matriz §2.2).

2. **`manifest.Fonts` era garantía de integridad falsa (RFLED §3.1).**
   `AtlasManifest.Fonts` se declaraba, se hasheaba (nombres) y se validaba, pero los
   archivos de fonts NUNCA se escribían/leían (las fuentes son built-in hardcodeadas
   en `Font5x7.cs`). → Fix: se elimina del modelo (declarar "fonts no soportado").
   Eliminado de manifest, checksum y validación de nombres.

3. **`ProjectService.OpenAsync(string path)` público (RFLED §15).**
   → `internal`: la apertura de producción es sólo por `OpenByIdAsync`. El test
   (con `InternalsVisibleTo`) sigue pasando; ningún controller usaba la ruta arbitraria.

4. **npm audit era informativo (RFLED §24).**
   → `npm audit --audit-level=high` (high/critical rompen CI).

5. **CompileError de Stryker en `ValidateIndexedAssetPixels` (RFLED §1.3).**
   CS0165 "uso de variable no asignada 'data'" → refactor sin cambio de semántica
   (flag `validBase64` + early return), elimina el mutante inválido.

## Estados por sección RFLED

| § | Área | Estado |
|---|---|---|
| 1 | Mutation breakdown + ranking | PASS (ver MUTATION-JUSTIFICATION.md) |
| 1.3 | Safe Mode | PASS (documentado; 1 corregido) |
| 1.4 | Exclusión `update` | PASS (justificada) |
| 2 | Crash safety / recovery | PASS (matriz completa + .autosave.bak) |
| 3 | Checksum / integridad | PASS (fonts no-soportado eliminado; scenes/assets hasheados) |
| 4 | Historial real Undo/Redo | PASS (E2E history-soak 20 ops) |
| 5 | Render parity C#↔JS | PARTIAL (4 golden + parity-r5 existen; no exhaustive diff) |
| 6 | Playback/loop | PARTIAL (blink verificado; drift/pingpong no exhaustivo) |
| 7 | Scene compiler | PASS (preflight + boundary tests existentes) |
| 8 | Device protocol | PASS (magic/version/length + fuzz básico) |
| 9 | TCP/Serial transport | PARTIAL (HIL sockets; serial cancelación real NO VERIFICADA) |
| 10 | Discovery | PARTIAL (thread-safety en in-memory; sin hardware) |
| 11 | State machine | PASS (contract tests Simulator/Firmware) |
| 12 | Hardware vs Simulator | NOT VERIFIED |
| 13 | LAN security | PASS (loopback fail-fast + doc LOCAL/LAN) |
| 14 | Web security | PASS (antiforgery en los 12 POST; path traversal blinkado) |
| 15 | Path API | PASS (OpenAsync internal) |
| 16-22 | Library/Rasterizer/Validator/Editor | PARTIAL (tests existentes; no exhaustivo fuzz) |
| 23 | Coverage | PASS (line 83.60%, branch 74.85%) |
| 24 | npm audit gate | PASS (high/critical gate) |
| 25 | Dependencies | PASS (0 vulnerable dotnet + npm) |
| 26-30 | Logging/perf/soak/E2E/Win-Linux | PARTIAL |
| 31 | CI | PASS (jobs independientes + gates) |
| 32 | Repo hygiene | PASS (sin basura trackeada) |

## Métricas finales (HEAD real, medido)

- `dotnet build -c Release -warnaserror`: 0 errores, 0 warnings.
- Tests .NET: **541/541 pass**.
- E2E Playwright: **50/50 pass × 3 corridas estables** (49 + 1 history-soak).
- Coverage: línea **83.60%** (2539/3037), rama **74.85%** (1060/1416).
- Mutation: created 3902, tested 1272, **killed 896, survived 340, timeout 36,
  no-coverage 309, compile-errors 204, ignored 373, score 58.95%** (break 55).
- Dependency audit: dotnet = 0 vulnerable; npm audit high = 0.

## NO VERIFICADO / BLOCKERS

- **Hardware físico:** NOT VERIFIED (sin dispositivo real).
- **Serial transport:** cancelación real de `SerialPort` vía `Task.Run` no garantiza
  cancelación (RFLED §9) — PARTIAL, requiere hardware o fake más fiel.
- **Render parity exhaustiva** (§5) y **playback drift** (§6): PARTIAL — los 4 golden
  + parity-r5 demuestran los casos clave, no la totalidad del espacio paramétrico.
- El resto de secciones P2 (logging redaction, fuzz extremo de rasterizer/validator,
  soak 500x) quedan como trabajo incremental, no blockers de cierre de software.

## Veredicto

SOFTWARE/SIMULATOR READY (sin P0 abiertos, sin P1 sin aceptación). Los P1 restantes
(Render parity exhaustiva, serial cancelación, playback drift) están acotados y
documentados. HARDWARE PRODUCTION VERIFIED = NO.