# AtlasLetreros --- MASTER BUILD SPEC

## Contrato definitivo de implementación desde cero para DeepSeek

**Objetivo:** construir AtlasLetreros V1 BASIC completo desde cero.\
**Regla:** no rediseñar, no inventar alcance, no detenerse en
diagnóstico. Ejecutar:
`READ → IMPLEMENT → BUILD → TEST → RUN/VERIFY → FIX → RETEST → CONTINUE`.

## 1. Producto V1 BASIC

Crear un diseño LED; texto, iconos, dibujo, formas e imágenes; timing y
animación; preview exacto; guardar/reabrir; biblioteca local; simulador;
detectar/seleccionar controlador; validar; enviar; reproducción
autónoma.

Fuera de V1: cuentas, nube, IA, pagos, colaboración, marketplace,
plugins, móvil nativo, DMX/Art-Net y video.

## 2. Invariantes

1.  El usuario diseña contenido; no programa hardware.
2.  `Project/Scene` = intención; `FrameBuffer` = verdad visual;
    `ScenePackage` = ejecución; `DeviceCapabilities` = límites del
    target.
3.  Project nunca contiene GPIO, RGB order, buses ni timings eléctricos.
4.  Un solo `Render(scene,time)` define editor, preview, simulator y
    compilación.
5.  Todo contenido visible es objeto.
6.  Una sesión continua de lápiz crea **un DrawingObject**.
7.  Group organiza miembros; no crea otra semántica de render/timing.
8.  Assets usados quedan embebidos en el proyecto.
9.  Save = temp + validate + atomic replace.
10. Transferencia fallida conserva `LastKnownGoodScene`.
11. Offline-first.
12. Ninguna abstracción sin responsabilidad real.
13. Ningún porcentaje inventado: reportar criterios.
14. No cambiar arquitectura salvo imposibilidad técnica demostrada.

## 3. Stack

-   ASP.NET Core MVC + C#.
-   JavaScript ES modules + HTML Canvas 2D para editor.
-   `System.Text.Json`.
-   SkiaSharp permitido para raster/import/export cuando Canvas/JS no
    baste.
-   Persistencia local `.atlas`; sin SQL.
-   Firmware separado.
-   Tests .NET + navegador.

### Política de terceros

Antes de adoptar: **documentación oficial → licencia → compatibilidad →
mantenimiento → vulnerabilidades → spike real → decisión**. Registrar en
`THIRD-PARTY-NOTICES.md`.

Preferidos: - SkiaSharp para gráficos C#. - Pixelarticons FREE como
banco primario; copiar/adaptar recursos libres localmente, nunca CDN
runtime. - Font Awesome Free sólo como complemento útil y conservando
atribución/licencia. - Fuentes libres OFL/MIT/CC0 pueden ser origen;
para LED se convierten a `BitmapFont` Atlas certificado.

Si falla un tercero, reemplazar por C#/JS mínimo detrás del mismo
contrato.

## 4. Solución

``` text
AtlasLetreros.sln
src/
  AtlasLetreros.Domain/
  AtlasLetreros.Application/
  AtlasLetreros.Infrastructure/
  AtlasLetreros.Protocol/
  AtlasLetreros.Web/
simulator/AtlasLetreros.Simulator/
firmware/AtlasLetreros.Firmware/
tests/
  AtlasLetreros.Domain.Tests/
  AtlasLetreros.Application.Tests/
  AtlasLetreros.Infrastructure.Tests/
  AtlasLetreros.Protocol.Tests/
  AtlasLetreros.Web.Tests/
  AtlasLetreros.Contract.Tests/
  AtlasLetreros.Golden.Tests/
  AtlasLetreros.E2E/
```

`Domain` no referencia Web/Infrastructure. `Application → Domain`.
`Infrastructure → Application/Domain/Protocol`. `Web → Application`.

## 5. Dominio obligatorio

``` text
Project
  Id, Name, FormatVersion, Canvas, Scenes, EmbeddedAssets, CreatedAt, UpdatedAt

Scene
  Id, Name, Duration, LoopMode, Layers

Layer
  Id, Name, Order, Visible, Locked, Objects

SceneObject
  Id, Name, Position, Size, Visible, Locked, Brightness, Timing, Animations
```

Derivados: `TextObject`, `IconObject`, `DrawingObject`, `ShapeObject`,
`ImageObject`.

`ObjectGroup`: IDs de miembros + operaciones/transformación común; no
contenido visual propio.

`TextObject`: Text, FontId, Color, alignments, LayoutMode.\
`IconObject`: AssetId, Tint/PaletteMode.\
`DrawingObject`: bitmap/pixel data + palette + bounds.\
`ShapeObject`: Line/Rectangle/Ellipse + geometry/stroke/fill.\
`ImageObject`: AssetId + pixel representation + conversion metadata.

Value Objects: `ProjectId`, `SceneId`, `ObjectId`, `AssetId`,
`DeviceId`, `PixelPoint`, `PixelSize`, `PixelRect`, `RgbColor`,
`TimeRange`, `CanvasDefinition`, `Checksum`.

Assets: `AssetCatalog`, `IconAsset`, `CustomDrawingAsset`, `ImageAsset`,
`BitmapFont`, `AssetLicenseInfo`.

Invariantes: IDs únicos; ≥1 scene; ≥1 layer/scene; referencias
resolubles; duración \>0; canvas válido; ningún objeto huérfano.

## 6. Animación

``` text
AnimationSet: Entrance?, Main?, Exit?
AnimationDefinition:
  Kind = Fixed | Blink | Marquee | Slide | Pulse | Wipe | Frame
  SpeedPreset = Slow | Normal | Fast
  Direction?
  Loop
```

Los valores finos son internos/avanzados.

## 7. Dispositivo/protocolo

``` text
Device
  DeviceId, FriendlyName, LastKnownEndpoint, Status, Capabilities

DeviceCapabilities
  LogicalWidth/Height
  ColorCapability
  MaxSceneBytes
  MaxAssetBytes
  SupportedAnimations
  ProtocolVersion
  AutonomousPlayback
```

Configuración eléctrica sólo en `DeviceProfile`, `MatrixTopology`,
`ControllerProfile`, `OutputDriverProfile`.

Pipeline:
`Scene → Validate → Optimize(copy) → Compile → ScenePackage → TargetValidate → Prepare → Upload → VerifyChecksum → Activate`.

`IDisplayTarget`: Connect/Discover, GetIdentity, GetCapabilities,
PrepareTransfer, Upload, Verify, Activate, Stop, GetStatus. Simulator y
hardware cumplen el mismo contrato.

## 8. Casos de uso / servicios

**Projects:** CreateProject, OpenProject, SaveProject, AutosaveProject,
RecoverProject, ValidateProject.\
**Editing:** AddObject, DeleteObjects, DuplicateObjects, MoveObjects,
ChangeObjectProperties, CreateDrawing, Group/Ungroup, AlignObjects,
ChangeTiming, AssignAnimation.\
**Library:** SearchAssets, ImportImage, CreateCustomAsset,
SaveToUserLibrary, EmbedUsedAssets.\
**Rendering:** RenderSceneAtTime, MeasureText, AutoLayoutText,
RasterizeAsset.\
**Devices:** DiscoverDevices, SelectDevice, GetCapabilities,
ValidateForTarget.\
**Deployment:** CompileScene, OptimizeForTarget, SendScene,
ActivateScene.

## 9. Controllers

``` text
EditorController: Index, New
ProjectsController: List, Create, Open, Save, Autosave
LibraryController: Search, Import, SaveCustom
DevicesController: Discover, Status, Select
PlaybackController: Compile, Send, Stop
SettingsController: Index
```

Controllers delgados: nada de filesystem, render, discovery o lógica de
escena.

## 10. ViewModels

`EditorViewModel`, `ProjectSummaryViewModel`, `NewProjectViewModel`,
`AssetSearchViewModel`, `DeviceViewModel`,
`DeviceCapabilitiesViewModel`, `SendResultViewModel`,
`SettingsViewModel`.

## 11. Vistas

``` text
Views/
  Editor/Index.cshtml
  Projects/Index.cshtml
  Projects/New.cshtml
  Library/Index.cshtml
  Devices/Index.cshtml
  Settings/Index.cshtml
  Shared/_Layout.cshtml
  Shared/_EditorToolbar.cshtml
  Shared/_ObjectInspector.cshtml
  Shared/_Timeline.cshtml
  Shared/_AssetLibrary.cshtml
  Shared/_DeviceSelector.cshtml
```

Editor: toolbar arriba; herramientas izquierda; canvas centro; inspector
contextual derecha; timeline abajo. Layers secundarios/avanzados.

## 12. JavaScript

``` text
wwwroot/js/editor/
  editor-state.js
  editor-canvas.js
  editor-renderer.js
  editor-objects.js
  editor-tools.js
  editor-selection.js
  editor-history.js
  editor-timeline.js
  editor-library.js
  editor-devices.js
  editor-ui.js
```

Estado único. Coordenadas lógicas LED. Zoom sólo visual.
Pointer→coordenada lógica. `pointerdown→drawing session→pointerup` = un
DrawingObject + un Undo. Drag/sliders = una operación histórica.
Selección click/Ctrl/Shift/rectángulo. Copia genera IDs nuevos.

## 13. Fuentes

Catálogo LED mínimo: `4x6`, `5x7`, `8x8` sólo si pasan certificación.

Obligatorio: A-Z, a-z, 0-9, ÁÉÍÓÚÜÑ/áéíóúüñ, ¿?¡!, puntuación y
`$%&+-/@#()`.

Prohibido crear 8x8 estirando 5x7.

Auto-layout: medir → conservar fuente legible → multilinea → fuente
menor certificada → marquee → nunca clipping silencioso.

Golden tests para cada glyph crítico y frases reales.

## 14. Iconos/biblioteca

Pixelarticons FREE primario. Adaptar localmente a `IconAsset`; no
dependencia runtime. Categorías: Tecnología, Herramientas, Comercio,
Comida, Símbolos, Flechas, Transporte, Personas, Corazones,
Comunicación.

Cada asset: nombre, aliases ES/EN, tags, licencia/origen, bitmaps
normalizados, preview. Una variante reducida sólo se acepta si sigue
siendo reconocible.

`Mi biblioteca`: cualquier dibujo/icono modificado puede guardarse.
Insertar crea copia independiente. Proyecto embebe el asset usado.

## 15. Dibujo/imágenes

Herramientas: Pencil, Eraser, Line, Rectangle, Ellipse, Fill, Selection.
Todo en coordenadas LED.

Import:
`decode → crop → nearest-neighbor scale → quantize → optional dither → preview → PixelAsset`.
PNG/JPEG/SVG según dependencia verificada. Nunca guardar sólo ruta
externa.

## 16. Persistencia

`.atlas` autocontenido:

``` text
manifest.json
scenes/
assets/
fonts/
previews/
```

Manifest: `format=atlas-project`, `version`.

Open: detect → migrate copy/in-memory → validate → open.\
Save: serialize temp → validate → close/flush → atomic replace →
recovery.\
Autosave separado. Nunca modificar original durante migración hasta
Save.

## 17. Renderer

``` text
FrameBuffer Render(Scene scene, TimeSpan time, RenderContext context)
```

Misma entrada = mismos píxeles.

Orden: objetos activos por timing → animación/transformación → capas →
clipping explícito → composición → framebuffer lógico. `MatrixMapper`
ocurre después y nunca altera preview lógico.

## 18. Simulator/firmware

Simulator implementa `IDisplayTarget`.

Firmware: identidad estable, capabilities, staging temporal, límites,
versión de protocolo, checksum, activación atómica,
`LastKnownGoodScene`, safe boot, playback autónomo, timeouts. USB/Serial
y Wi-Fi/LAN son transports detrás del mismo protocolo.

## 19. UX/errores

Normal = lenguaje humano; Advanced = técnico separado. Mostrar siempre:
cambios sin guardar, escena/tiempo, selección, target, online/offline y
envío.

Texto no cabe → auto-layout/marquee. Objeto fuera → aviso + traer al
canvas. Offline → retry sin perder trabajo. Incompatible → capacidad
faltante. Send fallido → escena anterior intacta. Cerrar modificado →
Guardar/Descartar/Cancelar.

## 20. PRUEBAS CELESTIALES

1.  **Unitarias:** dominio, geometría, timing, renderer, auto-layout,
    validación, migración, compilación.
2.  **Property-based:** coordenadas nunca fuera de rango; dimensiones
    invariantes; serialize/deserialize conserva semántica;
    transformaciones inversas; timing correcto.
3.  **Golden framebuffer:** matrices exactas para fuentes, iconos,
    escenas y tiempos; un pixel cambiado falla.
4.  **Contract:** mismo suite para SimulatorTarget y adapters:
    capabilities/prepare/upload/verify/activate/stop/status.
5.  **Model-based/state machine:** secuencias generadas de
    Project/Device/Transfer; sólo transiciones legales.
6.  **Mutation testing:** Stryker.NET sobre timing, renderer,
    validadores, compiler, persistencia y protocolo; mutantes críticos
    supervivientes requieren test o justificación.
7.  **Fuzz:** manifest/JSON/protocolo/importadores truncados, enormes,
    IDs duplicados, Unicode raro, versiones/checksums inválidos; nunca
    crash/hang/OOM razonable.
8.  **Fault injection:** disco lleno; temp corrupto; caída durante
    replace; red cortada 1/50/99%; timeout; doble Send; reboot; checksum
    malo; payload grande. Proyecto recuperable y LastKnownGood intacto.
9.  **E2E real:** pointer/mouse/keyboard, drawing, selection, drag,
    timeline, Undo/Redo, save/open, library, simulator, send.
10. **Soak:** loops largos, muchos save/open/send; detectar leaks,
    timers duplicados, logs crecientes.
11. **Performance:** 32x16/64x32 fluidos; 100 objetos sin bloqueo
    perceptible; preview estable.
12. **Dependency gate:** licencia, vulnerabilidades, mantenimiento y
    spike real.

### R1 --- anuncio reina

32x16; 0--5s `MG SOL` blink; 5--10s PC blink; 10--30s
`SE ARREGLAN COMPUTADORAS` marquee; preview; Save/Close/Open; Send
simulator; resultado idéntico.

### R2 --- dibujo

16x16; dibujar corazón continuo; debe existir un DrawingObject;
mover/blink; guardar en Mi biblioteca; borrar; reinsertar; Undo/Redo;
Save/Open; idéntico.

### R3 --- usuario hostil

Texto enorme, duración límite, objeto fuera, borrar layer con contenido,
clicks repetidos Send, target desaparece, cerrar sin guardar, archivo
corrupto/viejo. Sin corrupción ni estado inexplicable.

### R4 --- transferencia celestial

A está activo. Enviar B y cortar conexión en cada fase. Tras reboot
sigue A salvo que B haya sido verificado y activado completo.

### R5 --- equivalencia

Para batería de escenas/tiempos:
`Editor logical render == Golden == Simulator logical output == compiled semantic output`.
Divergencia bloquea release.

## 21. Seguridad/robustez

Validar archivos/requests/payloads; límites de
tamaño/conteo/dimensiones; prevenir path traversal `.atlas`; assets no
ejecutables; identidad no basada en IP; checksum; timeouts/cancel; una
transferencia/target; logs sin secretos y rotados; auditoría de
dependencias CI.

## 22. Orden obligatorio

0.  Skeleton/CI/contratos.
1.  Project + Scene + Layer + objects + editor vacío + Save/Open.
2.  FrameBuffer + Text + fonts + auto-layout + golden.
3.  Selección/move/duplicate/delete/inspector/Undo/Redo/timeline.
4.  DrawingObject + shapes + pointer tests + Mi biblioteca.
5.  Iconos/imágenes + raster + embedded assets/licencias.
6.  Timing + animaciones + golden temporal.
7.  Simulator + compile/send/activate + contract tests.
8.  Autosave/recovery/atomic save/migrations/fuzz/fault.
9.  Discovery + identidad + USB/LAN + upload transaccional.
10. Firmware + LastKnownGood + autonomous + safe boot.
11. UX hardening/accesibilidad/advanced.
12. R1--R5 + mutation + fuzz + fault + soak + dependency audit.

No avanzar con defecto crítico del slice actual.

## 23. Definition of Done

V1 sólo termina cuando R1--R5 pasan; Release limpio; tests relevantes
pasan; fault injection no corrompe; mutation revisado en lógica crítica;
fuzz sin crash/hang; Save/Open fiel; simulator/real comparten contrato;
offline funciona; licencias documentadas; cero TODO/Fake/Stub en rutas
V1; cero botón sin comportamiento.

## 24. Reporte de DeepSeek

Responder al terminar cada slice:

``` text
SLICE: N - nombre
IMPLEMENTADO: archivos principales
BUILD: PASS/FAIL
TESTS: X pass / Y fail
PRUEBA FUNCIONAL: PASS/FAIL + ejecución
RIESGOS/PENDIENTES: sólo reales
SIGUIENTE: N+1
```

Si falla, corregir antes de continuar. No pedir confirmación sobre
decisiones resueltas aquí.

**FIN DEL CONTRATO.**
