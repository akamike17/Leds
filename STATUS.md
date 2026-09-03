# DSLetras — Estado del proyecto (auditoría correctiva)

Herramienta de diseño de letreros LED. Spec maestro: `AtlasLetreros_REV3_DEEPSEEK_MASTER.md`.

## Resumen de la auditoría correctiva

Esta auditoría corrige las desviaciones detectadas en el cierre anterior y las
verifica con pruebas reales. Los cambios principales:

### P0 — Seguridad y persistencia
- **Path traversal eliminado**: `ProjectsController.Open(path)` (ruta arbitraria)
  fue **eliminado**. Abrir se hace por `ProjectId` y toda ruta se resuelve dentro de
  `ProjectsRoot` vía la utilidad única `ProjectPaths` (canonicalización + containment
  estricto). `AtlasProjectStore` rechaza rutas rooted, `..`, separadores y nombres de
  archivo no simples en manifest/scenes/assets/fonts/previews.
- **Save crash-safe**: temp + validación → rename a `.bak` → rename a principal. El
  backup se mantiene hasta validar el nuevo principal; si falla el segundo rename, el
  backup se **restaura**. Autosave usa el mismo mecanismo. Fault-injection antes/durante/
  después de cada rename (`Infrastructure/Persistence/AtlasProjectStore.FailPoint`).
- **GET mutante eliminado**: `Editor/New` pasó de GET a **POST** con antiforgery.
- **Antiforgery consistente**: `[ValidateAntiForgeryToken]` en todas las operaciones
  mutables MVC y JSON fetch; el token viaja por encabezado `RequestVerificationToken`
  (configurado en `AddAntiforgery`) y se inyecta en `_Layout` como `window.__antiforgery`.
- **Binding loopback**: si `ASPNETCORE_URLS` no se define, el servidor se enlaza SÓLO a
  `http://127.0.0.1` (defensa ante exposición accidental del control de dispositivos).

### P1 — Integridad y límites
- **Checksum .atlas completo**: cubre manifest canónico + project shell + cada scene +
  cada asset (árbol de hashes SHA-256, determinista, sin timestamps). Una modificación
  a una scene/asset invalida el checksum aunque el shell/manifest sigan intactos.
  Se corrigió además un bug real: `TimeRange` no tenía converter y perdía `end` al
  reabrir (objeto con timing corrupto); añadido `TimeRangeConverter`.
- **ScenePackage.ComputeChecksum** cubre ProtocolVersion, DurationMs (double sin pérdida),
  LoopMode, Canvas, FrameIntervalMs, FrameCount y, por frame, TimeMs + Pixels.
- **EstimatedBytes → tamaño wire real** (`RealWireSize()` = serialización JSON).
- **Preflight SceneCompiler**: frameInterval finito > 0, duración finita/positiva, canvas
  dentro de límites, frameCount acotado, multiplicaciones en long, predicción de memoria.
- **FrameBuffer**: width*height checked/long + máximo de píxeles (rechaza desborde).
- **Máquina de estados unificada** (Simulator/Firmware/FirmwareTarget): una transferencia
  activa, tamaño esperado, Upload antes de Verify, Verify antes de Activate, LastKnownGood.
- **Firmware**: expected size en staging, verificación de tamaño real, invariantes del
  paquete, FrameInterval válido, `PlaybackTick` robusto ante tiempo negativo/NaN/no-finito.
- **ChannelDisplayTarget**: `ExpectAck`/`ExpectOpcode` centralizados (nunca trata un opcode
  distinto de Ack/Error como éxito; payload truncado y versión incompatible → fallo).
- **Tcp/Serial channel**: SemaphoreSlim (serialización), timeout por request, validación de
  magic/version/length ANTES de reservar payload, máximo de respuesta, reset/reconnect,
  cancelación robusta.
- **DeviceDiscoveryService**: registro thread-safe, colisiones de serial explícitas,
  fallos sanitizados.

### Integración de hardware
- **Flujo real conectado**: `SettingsController` + vista `Settings/Index` permite configurar/
  enumerar canales LAN (`TcpDeviceChannel`) y Serial (`SerialDeviceChannel`), que alimentan
  `DeviceDiscoveryService.DiscoverAsync` con canales reales (no adapters sueltos).
- Tests loopback/in-memory separados de los simulados (`TCP HIL` y `contract`).

### Rendering
- **`AnimationEvaluator.ViewportWidth` static mutable ELIMINADO**: el viewport viaja como
  argumento (`Evaluate(obj, t, viewportWidth)`), Render es puro/thread-safe. Test de
  concurrencia con dos canvases.
- **DrawEllipse corregido** (C# y JS): `i/j` locales, `cx=(w-1)/2.0`, `cy=(h-1)/2.0`,
  `x/y` sólo en el `SetPixel`/`fillRect` final. Golden de elipse desplazada.
- **Renderer JS↔C# paridad**: JS ahora implementa animations, brightness, clips, icon e
  image (espejo de `SceneRenderer`), eliminando la divergencia "icon/image sin implementar".

### Editor JS
- **DrawingSession**: puntos absolutos, punto inicial del pointerdown, correcto para
  izquierda/arriba/diagonales, clamp al canvas.
- **Playback**: rAF + tiempo real (no incremento por setTimeout), stop real, Once/Loop/
  PingPong, sin estado residual (`aria-pressed`, no `dataset.playing`).

### Validación / observabilidad / CI
- **ProjectValidator endurecido**, **MaxObjectsPerScene aplicado de verdad**, checked
  arithmetic en EditingService/LibraryService/ImageRasterizer, escritura atómica en library.
- **RollingFileLogger** redacta Message Y Exception + structured state, retención/tamaño.
- **.vs fuera del índice** y en `.gitignore`. **4 warnings** corregidos (0 warnings).
- **CI**: warnings-as-errors, Node 24, `npm audit` explícito, dotnet-stryker **fijado**
  a 4.16.0, coverage report, análisis estático.

## Validación final

- **Build Release: 0 errores, 0 warnings.**
- **Tests .NET: 423/423 pass.**
- **E2E (Playwright): 9/9 pass.**
- **Mutation (Stryker):** ver abajo (alcance amplio, reporte honesto).
- **Dependencias:** 0 vulnerables (`dotnet list package --vulnerable`); `npm audit`: 0.

## Mutation testing (honesto)

El config anterior sólo mutaba un subconjunto de `Domain/Deployment` y excluía
mutadores enteros (`string`, `statement`, `block`) y 8 spans por byte-offset frágil,
y se reportaba como "100% sobre lógica núcleo". Eso no reflejaba el alcance real.

El nuevo `stryker-config.json`:

- **Mutate amplio**: `Domain/Deployment/*`, `Domain/Validation/*`,
  `Application/Services/*`, `Infrastructure/Persistence/*`,
  `Infrastructure/Transport/*`, `Infrastructure/Logging/*`.
- **Sin exclusiones por byte-offset** (frágiles); sólo se excluye el mutador `update`
  (equivalente: `++→--` produce artefactos de timeout sin señal).
- Reporte honesto: created / tested / killed / survived / no coverage / ignored /
  compile errors.

Resultado (ver `MUTATION-JUSTIFICATION.md` para clasificación por archivo):
```
created:       3626
tested:        1069
killed:         640
survived:       366
timeout:         63
no coverage:    464   (enumerados en MUTATION-JUSTIFICATION.md)
ignored:        350
compile errors: 235
score:        45.86 %  (sobre el alcance amplio — honesto)
```

## Pendiente de hardware físico

La integración LAN/Serial está completa y probada contra loopback TCP/in-memory. NO se ha
probado contra hardware físico real (placa LED serie/Ethernet): requiere un dispositivo
físico y queda fuera del cierre de software. El simulador (`SimulatorTarget`) y el
firmware modelado (`Firmware`) cubren el contrato completo para tests deterministas.

## Archivos de referencia

- `AtlasLetreros_REV3_DEEPSEEK_MASTER.md` — spec maestro.
- `MUTATION-JUSTIFICATION.md` — clasificación de mutation testing.
- `THIRD-PARTY-NOTICES.md` — auditoría de dependencias.
- `stryker-config.json` — config de Stryker (alcance amplio, honesto).
- `tests/e2e/` — suite E2E Playwright (9 flujos).
- `.github/workflows/ci.yml` — CI (checkout limpio: build → test → E2E → mutation → audit).