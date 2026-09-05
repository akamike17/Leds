# DSLetras — Estado del proyecto (auditoría funcional correctiva)

Herramienta de diseño de letreros LED. Spec maestro: `AtlasLetreros_REV3_DEEPSEEK_MASTER.md`.

## Alcance de esta auditoría

Rige `ins.txt`: **auditoría funcional correctiva para cerrar V1 BASIC**. Se
congelaron mutation/coverage/tests abstractos hasta que el producto funcione
realmente desde navegador. Criterio de aceptación: una función está terminada
sólo cuando el usuario completa el flujo desde la UI real y el resultado
sobrevive Save/Reload.

## Lo que se DEMOSTRÓ funcional (verificado con framebuffer real)

Evidencia vía Playwright real + `getImageData` (píxeles observables), no
"canvas visible" ni "dirty cambió".

### P0 — Editor
- Nuevo proyecto, abrir, guardar, texto, lápiz, borrador (elimina objeto, no negro
  encima), línea, rectángulo, elipse (real, no strokeRect), fill (flood-fill
  acotado), selección por click, Ctr/Shift multiselect, selección rectangular
  (por intersección), mover, borrar, duplicar, Undo/Redo, inspector, playback.
- `#btn-open` conectado al flujo real (navega a /Projects).
- `previewShape` dibuja línea/rect/elipse según la herramienta real.

### P0 — Texto
- `HOLA` pinta y sobrevive Save/Open (60 px idénticos).
- Caracteres acentuados `ÁÉÍÓÚ ñü¿!` y `$%&+-/@#().` pintan píxeles.
- Overflow: texto que no cabe activa marquee automático y NO se recorta
  silenciosamente (anclado al lienzo, visible).

### P0 — Inspector
- Precarga valores existentes (nombre, X/Y, texto, color, visible, locked,
  brillo, timing inicio/fin) sin destruirlos.
- Edición actualiza objeto → render inmediato → markDirty → Undo/Redo →
  Save/Open. X/Y mueve el objeto (verificado por píxeles).

### P0 — Biblioteca
- Guardar dibujo con confirmación visible (HUD `#stat-notify`).
- Modal en el editor con tabs Dibujos/Iconos/Imágenes, preview correcto y botón Insertar.
- Insertar crea copia independiente; el asset usado queda embebido en el proyecto.
- 16 iconos incluidos (no sólo Corazón): Corazón, Estrella, Flechas, Teléfono,
  Carrito, Engranaje, Wi-Fi, etc.
- Transparencia de icono real (spec 14): el fondo del icono NO borra los objetos
  debajo (señal explícita `TransparentIndex` en el asset; antes un Corazón sobre
  fondo blanco de 512 px lo dejaba en 465).
- Imágenes importadas persisten en la biblioteca global (`i-*.json`) y se listan
  en el tab "Imágenes" (insertar/borrar). El flujo importar→listar→insertar→
  persistir está verificado.
- Contrato de píxeles C#↔JS DEFINIDO y probado: `byte[]` base64 en wire ↔ array
  JS; al cargar se decodifica, al guardar se codifica (`normalizePixels`/
  `projectForWire`). Corrigió el bug real "Proyecto no deserializable" de todo
  dibujo.

### P0 — Imágenes
- Seleccionar archivo → decode → rasterizar (nearest-neighbor/quantize/dither)
  → insertar ImageObject con asset embebido. Save/Open sin archivo origen
  conserva (verificado 32 px idénticos).

### R5 — equivalencia editor == simulador
- El overlay de selección (contorno azul) ahora vive en un canvas superpuesto
  SEPARADO; `#led-canvas` conserva SÓLO el framebuffer real. `/Deploy/SimulatorFrame`
  devuelve el framebuffer del paquete activo en el simulador, y el E2E verifica
  `editor.lit == simulador.lit` con píxeles idénticos (no un simple "Enviado").

### P1 — Send/Simulator
- Send guarda el estado actual ANTES de enviar (canvas nunca desincronizado del
  device); envía la escena seleccionada (SceneIndex), no siempre Scenes[0].

### P1 — Timeline/animaciones
- Duración de escena editable; panel de animación en inspector (Fixed/Blink/
  Marquee/Slide/Pulse/Wipe/Frame, Slow/Normal/Fast, dirección, Loop,
  Entrance/Main/Exit). Blink cambia el framebuffer al reproducir (512→0 px).

### P1 — Escenas/capas
- Selector de escena y capa; añadir escena/capa; persistir. La capa activa
  recibe los objetos nuevos. Visible/Locked respetados: un objeto `visible:false`
  no renderiza; un objeto/capa `locked` no se borra ni edita.

### P1 — Autosave/Recovery
- Autosave conectado a la sesión (`/Projects/Autosave` cada 30 s si hay cambios).
  Recovery ante corrupción ya vive y está probado en `AtlasProjectStore`
  (temp + validación + rename + restauración de backup).

### P1 — Devices/Playback/Home
- `/Devices`: lista targets (serial/transport/endpoint/online), ya no placeholder.
- `/Playback`: compilar/enviar escena + estado del target, ya no placeholder.
- Home: portada (Nuevo/Abrir/Biblioteca/Dispositivos), eliminado el Welcome
  default.

## R1 y R2 del MASTER SPEC (ejecutados con navegador real)

- **R1 anuncio reina**: `MG SOL` → `PC` → `SE ARREGLAN COMPUTADORAS` → Save/Open
  (píxeles idénticos) → enviar al simulador.
- **R2 dibujo**: corazón continuo con lápiz → mover → blink → guardar en
  biblioteca → borrar → reinsertar → Undo/Redo → Save/Open (idéntico).

## Gates finales

- `dotnet build -c Release -warnaserror`: 0 errores, 0 warnings.
- Tests .NET: **541/541 pass**.
- E2E Playwright (50 specs): **50/50 pass × 3 corridas estables** (49 + history-soak).
- Coverage (coverlet): línea **83.60%** (2539/3037), rama **74.85%** (1060/1416).
- Mutation (Stryker): score **58.95%** (break 55; ver MUTATION-JUSTIFICATION.md).
- Dependency audit: dotnet = **0 vulnerable**; `npm audit --audit-level=high` = **0**.
- Sin errores de consola JS ni HTTP 4xx/5xx inesperados (gate dedicado).
- Sin botones muertos ni placeholders "se implementará después" en el alcance V1.

## Pendientes reales (NO verificados)

- Hardware físico real (placa LED serie/Ethernet): SIMULATOR VERIFIED, **HARDWARE
  NOT VERIFIED** (sin dispositivo físico).
- Transports USB/Serial/Wi-Fi reales: HIL (sockets loopback) + in-memory; la
  cancelación real de `SerialPort` vía `Task.Run` no garantiza cancelación (RFLED §9).
- Render parity exhaustiva (§5) y playback drift (§6): PARTIAL — casos clave cubiertos
  por golden + parity-r5, no el espacio paramétrico completo.

## Commits de la pasada correctiva (RFLED.md)

Ver `FINAL_AUDIT.md`. Bugfixes P0/P1 de esta pasada: recovery `.autosave.bak`
(matriz completa), `manifest.Fonts` eliminado (garantía falsa), `OpenAsync(path)`
→ internal, npm audit = gate high/critical, compile-error Stryker de
`ValidateIndexedAssetPixels`, soak real de Undo/Redo (E2E history-soak).