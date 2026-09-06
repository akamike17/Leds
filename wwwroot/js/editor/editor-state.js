// Estado único del editor (spec sección 12): un único árbol de estado.
import { Renderer } from './editor-renderer.js';
import { History } from './editor-history.js';
import { Selection } from './editor-selection.js';
import { Tools } from './editor-tools.js';
import { Inspector } from './editor-ui.js';

export class EditorState {
    constructor() {
        this.project = null;
        this.canvas = null;
        this.ctx = null;
        this.renderer = null;
        this.pixelScale = 10;
        this.currentTime = 0;
        this.dirty = false;

        this.history = new History();
        this.selection = new Selection(this);
        this.tools = new Tools(this);
        this.inspector = new Inspector(this);

        // estado de sesión de dibujo (una sesión continua = un object + un undo)
        this.drawingSession = null;
        this.shapeSession = null;
        this._playback = null;
    }

    async loadProject(projectId) {
        const res = await fetch(`/Editor/Load?id=${projectId}`);
        const data = await res.json();
        if (!data.success) throw new Error(data.message || 'No se pudo cargar el proyecto');
        this.project = JSON.parse(data.project);
        this.normalizePixels(this.project);
        document.getElementById('project-name').textContent = this.project.name || 'Sin título';
        this.setupCanvas();
        this.populateSceneSelect();
        this.populateLayerSelect();
        this.updateSceneTimeLabel();
        this.render();
        this.startAutosave();
    }

    // Autosave (spec 16): conecta el backend Autosave a la sesión de edición. Escribe
    // <id>.atlas.autosave cada 30 s si hay cambios, sin tocar el documento principal.
    startAutosave() {
        if (this._autosaveTimer) clearInterval(this._autosaveTimer);
        this._autosaveTimer = setInterval(async () => {
            if (!this.dirty) return;
            const projectId = document.getElementById('project-id')?.value;
            try {
                const res = await fetch('/Projects/Autosave', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': window.__antiforgery?.token || '',
                    },
                    body: JSON.stringify(this.projectForWire()),
                });
                // "Autoguardado" SOLO si el servidor confirma success:true. Un HTTP 2xx
                // con success:false, un 4xx/5xx o un body no-JSON son un FALLO: se
                // conserva el estado dirty, se muestra aviso no destructivo y se
                // reintenta en el próximo tick (spec 2.D).
                let ok = false;
                try {
                    const data = await res.json();
                    ok = data && data.success === true;
                } catch { ok = false; }   // body malformado / no-JSON → fallo
                if (ok) {
                    if (this.hud) this.hud.setSend('Autoguardado');
                } else {
                    if (this.hud) this.hud.notify('warning', 'Autoguardado falló; se reintentará.');
                }
            } catch { /* offline: el autosave retoma en el próximo tick */ }
        }, 30_000);
    }

    setupCanvas() {
        const canvas = document.getElementById('led-canvas');
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        canvas.width = this.project.canvas.width;
        canvas.height = this.project.canvas.height;
        canvas.style.width = (this.project.canvas.width * this.pixelScale) + 'px';
        canvas.style.height = (this.project.canvas.height * this.pixelScale) + 'px';

        // Overlay de selección (contorno azul + rect-select) va en un canvas SEPARADO
        // superpuesto, de modo que el canvas de contenido (#led-canvas) conserva SÓLO
        // el framebuffer real (invariante 4/R5: editor == simulador == compiled).
        const overlay = document.getElementById('selection-overlay');
        this.overlayCtx = overlay ? overlay.getContext('2d') : null;
        if (overlay) {
            overlay.width = canvas.width;
            overlay.height = canvas.height;
            overlay.style.width = canvas.style.width;
            overlay.style.height = canvas.style.height;
        }

        this.renderer = new Renderer(canvas, this.ctx);
        // Assets embebidos (assetId -> JSON) para iconos/imágenes (invariante 8).
        this.renderer.embeddedAssets = this.project.embeddedAssets || {};
        this.installPointerHandlers();
    }

    populateSceneSelect() {
        const sel = document.getElementById('scene-select');
        sel.innerHTML = '';
        (this.project.scenes || []).forEach((s, i) => {
            const opt = document.createElement('option');
            opt.value = String(i);
            opt.textContent = s.name || `Escena ${i + 1}`;
            sel.appendChild(opt);
        });
        this.syncSceneDurationInput();
    }

    // Sincroniza el input de duración con la escena seleccionada.
    syncSceneDurationInput() {
        const scene = this.currentScene();
        const el = document.getElementById('scene-duration');
        if (el && scene) el.value = ((scene.duration ?? 5000) / 1000).toFixed(1);
    }

    // Cambia la duración de la escena seleccionada (markDirty + re-render).
    setSceneDuration() {
        const scene = this.currentScene();
        const el = document.getElementById('scene-duration');
        if (!scene || !el) return;
        const secs = parseFloat(el.value);
        if (!(secs > 0)) return;
        this.history.captureOnce(this.project);
        scene.duration = Math.round(secs * 1000);
        this.history.commitPending();
        this.updateSceneTimeLabel();
        this.markDirty();
        this.render();
    }

    render() {
        if (!this.renderer) return;
        const scene = this.currentScene();
        if (scene) this.renderer.renderScene(scene, this.currentTime);
        this.renderSimulator();
        this.drawSelectionOverlay();
        this.inspector.render();
        this.updateLibraryButton();
        if (this.hud) this.hud.setSelection(this.selection.list().length);
    }

    updateLibraryButton() {
        const sel = this.selection.list();
        const hasDrawing = sel.some(o => o.kind === 'drawing');
        document.getElementById('btn-save-library').classList.toggle('d-none', !hasDrawing);
    }

    currentScene() {
        const sel = document.getElementById('scene-select');
        const idx = sel ? parseInt(sel.value, 10) : 0;
        return (this.project.scenes || [])[idx] || (this.project.scenes || [])[0] || null;
    }

    // Capa actual seleccionada en el selector (por índice). Devuelve null si no hay.
    currentLayer(scene) {
        const sel = document.getElementById('layer-select');
        const idx = sel ? parseInt(sel.value, 10) : 0;
        const layers = [...(scene?.layers || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
        return layers[idx] || layers[0] || null;
    }

    // Puebla el selector de capas de la escena actual (y reconstr. selección).
    populateLayerSelect() {
        const scene = this.currentScene();
        const sel = document.getElementById('layer-select');
        if (!sel) return;
        sel.innerHTML = '';
        [...(scene?.layers || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).forEach((l, i) => {
            const opt = document.createElement('option');
            opt.value = String(i);
            opt.textContent = l.name || `Capa ${i + 1}`;
            sel.appendChild(opt);
        });
    }

    // Añade una capa a la escena actual (markDirty + re-render).
    addLayer() {
        const scene = this.currentScene();
        if (!scene) return;
        this.history.captureOnce(this.project);
        const order = (scene.layers || []).length;
        scene.layers = scene.layers || [];
        scene.layers.push({ id: 'l-' + this.newId(), name: `Capa ${order + 1}`, order, visible: true, locked: false, objects: [] });
        this.history.commitPending();
        this.populateLayerSelect();
        const sel = document.getElementById('layer-select');
        if (sel) sel.value = String(order);
        this.markDirty();
        this.render();
    }

    // Añade una escena vacía (markDirty + re-render + seleccionarla).
    addScene() {
        this.history.captureOnce(this.project);
        const idx = (this.project.scenes || []).length;
        this.project.scenes.push({
            id: this.newId(), name: `Escena ${idx + 1}`, duration: 5000, loopMode: 1,
            layers: [{ id: 'l-' + this.newId(), name: 'Capa 1', order: 0, visible: true, locked: false, objects: [] }],
        });
        this.history.commitPending();
        this.populateSceneSelect();
        const sceneSel = document.getElementById('scene-select');
        if (sceneSel) sceneSel.value = String(idx);
        this.populateLayerSelect();
        this.syncSceneDurationInput();
        this.updateSceneTimeLabel();
        this.markDirty();
        this.render();
    }

    // ----- puntero -----
    installPointerHandlers() {
        const c = this.canvas;
        c.addEventListener('pointerdown', e => this.onPointerDown(e));
        c.addEventListener('pointermove', e => this.onPointerMove(e));
        c.addEventListener('pointerup', e => this.onPointerUp(e));
        c.addEventListener('pointerleave', () => this.onPointerLeave());
    }

    onPointerDown(e) {
        const logical = this.tools.toLogical(e.clientX, e.clientY);
        const scene = this.currentScene();
        if (!scene) return;

        if (this.tools.activeTool === 'select') {
            const hit = this.selection.hitTest(logical.x, logical.y, scene);
            if (hit) {
                this.history.captureOnce(this.project);
                this.selection.clickOn(hit, { ctrl: e.ctrlKey, meta: e.metaKey, shift: e.shiftKey });
                this.selection.beginDrag(logical);
                this.render();
            } else {
                this.selection.beginDrag(null);
                this.tools.beginRectSelect(logical);
            }
        } else if (this.tools.activeTool === 'text') {
            this.history.captureOnce(this.project);
            const text = window.prompt('Texto:', 'HOLA');
            if (text) {
                this.addTextObject(scene, text, logical);
                this.render();
            }
        } else if (this.tools.activeTool === 'icon') {
            // Icon picker integrado (spec 19): abre el modal y ancla la posición para
            // insertar el icono elegido en el punto del click, sin salir del editor.
            this._pendingIconPos = logical;
            this.openIconPicker();
        } else if (this.tools.activeTool === 'pencil') {
            this.history.captureOnce(this.project);
            this.startDrawingSession(logical, 'pencil');
        } else if (this.tools.activeTool === 'eraser') {
            // El borrador (spec 15) elimina píxeles de forma semántica: quita el/los
            // objeto(s) cuyo bounding box cae en la coordenada. No crea un DrawingObject
            // negro encima (eso no sobreviviría Save/Open como "borrado").
            this.history.captureOnce(this.project);
            this.eraseAt(logical);
        } else if (this.tools.activeTool === 'fill') {
            this.history.captureOnce(this.project);
            this.floodFill(logical);
        } else if (this.tools.activeTool === 'rect' || this.tools.activeTool === 'ellipse' || this.tools.activeTool === 'line') {
            this.history.captureOnce(this.project);
            this.startShapeSession(scene, logical, this.tools.activeTool);
        }
    }

    // Borrado semántico: elimina de la capa activa todo objeto cuyo bounding box
    // contenga la coordenada lógica. Un borrado vacío no ensucia (markDirty innecesario).
    eraseAt(logical) {
        const scene = this.currentScene();
        if (!scene) return;
        let removed = 0;
        for (const layer of scene.layers || []) {
            if (layer.locked) continue;
            const before = (layer.objects || []).length;
            layer.objects = (layer.objects || []).filter(obj => {
                if (obj.locked) return true;
                const x = obj.position?.x ?? 0, y = obj.position?.y ?? 0;
                const w = (obj.size?.width ?? this.selection.guessSize(obj).w) || 1;
                const h = (obj.size?.height ?? this.selection.guessSize(obj).h) || 1;
                const inside = logical.x >= x && logical.x < x + w && logical.y >= y && logical.y < y + h;
                if (inside) return false;
                return true;
            });
            removed += before - layer.objects.length;
        }
        if (removed > 0) {
            this.selection.deselectAll();
            this.markDirty();
            this.render();
        }
    }

    // Flood fill (spec 15): rellena la región conexa (4-conectada) del framebuffer
    // lógico actual que comparte el color del píxel origen, limitada al canvas/drawing.
    // El resultado se materializa como un DrawingObject (1bpp monocromo) para que
    // sobreviva Save/Open como un objeto más y no un "parche" efímero.
    floodFill(logical) {
        const scene = this.currentScene();
        if (!scene) return;
        const w = this.canvas.width, h = this.canvas.height;
        if (logical.x < 0 || logical.x >= w || logical.y < 0 || logical.y >= h) return;

        // Color del píxel origen en el framebuffer lógico actual.
        const target = this.framebufferPixel(logical.x, logical.y);
        // Si el píxel origen ya está "encendido" (pintado) no hay región vacía que rellenar;
        // rellenar sólo tiene sentido sobre regiones apagadas (transparentes/negras).
        if (target) return;

        const visited = new Uint8Array(w * h);
        const stack = [[logical.x, logical.y]];
        let minX = logical.x, maxX = logical.x, minY = logical.y, maxY = logical.y;

        while (stack.length > 0) {
            const [x, y] = stack.pop();
            const idx = y * w + x;
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            if (visited[idx]) continue;
            visited[idx] = 1;
            // rellenamos únicamente píxeles apagados (mismo "color" que el origen).
            if (this.framebufferPixel(x, y)) continue;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            stack.push([x + 1, y], [x - 1, y], [x, y + 1], [x, y - 1]);
        }

        const fw = maxX - minX + 1, fh = maxY - minY + 1;
        const data = new Uint8Array(fw * fh);
        for (let y = minY; y <= maxY; y++)
            for (let x = minX; x <= maxX; x++)
                if (visited[y * w + x] && !this.framebufferPixel(x, y))
                    data[(y - minY) * fw + (x - minX)] = 1;

        const obj = {
            id: this.newId(), kind: 'drawing', name: 'Relleno',
            position: { x: minX, y: minY }, size: { width: fw, height: fh },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            bitsPerPixel: 1, palette: [{ r: 255, g: 255, b: 255 }],
            pixelData: Array.from(data),
            bounds: { origin: { x: 0, y: 0 }, size: { width: fw, height: fh } },
        };
        this.layer().objects.push(obj);
        this.markDirty();
        this.render();
    }

    // Lee un píxel del framebuffer lógico actual (composición de la escena al tiempo 0).
    // Devuelve true si el píxel está encendido (no negro).
    framebufferPixel(x, y) {
        if (!this.renderer) return false;
        return this.renderer.pixelAt(x, y);
    }

    onPointerMove(e) {
        const logical = this.tools.toLogical(e.clientX, e.clientY);

        if (this.selection.dragStart) {
            this.selection.dragTo(logical);
            this.render();
            return;
        }
        if (this.tools.rectSelect) {
            this.tools.updateRectSelect(logical);
            this.render();
            return;
        }
        if (this.drawingSession) {
            this.continueDrawing(logical);
            return;
        }
        if (this.shapeSession) {
            this.continueShape(logical);
            return;
        }
    }

    onPointerUp(e) {
        const logical = this.tools.toLogical(e.clientX, e.clientY);

        if (this.selection.dragStart) {
            this.selection.dragTo(logical);
            this.selection.endDrag();
            this.history.commitPending();
            this.markDirty();
            this.render();
            return;
        }
        if (this.tools.rectSelect) {
            const r = this.tools.endRectSelect();
            this.selectRect(r);
            this.render();
            return;
        }
        if (this.drawingSession) {
            this.endDrawingSession();
            this.history.commitPending();
            this.markDirty();
            this.render();
            return;
        }
        if (this.shapeSession) {
            this.endShapeSession();
            this.history.commitPending();
            this.markDirty();
            this.render();
            return;
        }
    }

    onPointerLeave() {
        // drag fuera del canvas: cancelamos sin commit
        this.selection.endDrag();
        this.tools.rectSelect = null;
        this.drawingSession = null;
        this.shapeSession = null;
        this.render();
    }

    // ----- operaciones de objetos -----
    addTextObject(scene, text, pos) {
        const textWidth = Math.max(1, text.length * 6 - 1);   // 5x7 con spacing 1 → 6px/char
        const canvasW = this.canvas.width;

        // Spec 13: nunca clipping silencioso. Si el texto no cabe en el ancho del
        // canvas, se activa el modo marquee (una animación Marquee automática) y se
        // ancla el texto dentro del lienzo para que sea VISIBLE (no recortado).
        const overflow = textWidth > canvasW;
        const x = overflow ? 0 : Math.min(pos.x, Math.max(0, canvasW - textWidth));

        const obj = {
            id: this.newId(), kind: 'text', name: text.slice(0, 8), text,
            position: { x, y: pos.y }, size: { width: textWidth, height: 7 },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            fontId: '5x7', color: { r: 255, g: 255, b: 255 },
            horizontalAlignment: 0, verticalAlignment: 0,
            layoutMode: overflow ? 2 : 0,
        };
        if (overflow) {
            // Animación Marquee: el renderer desplaza el texto dentro del viewport en
            // lugar de recortarlo; la Y se ancla dentro del lienzo para que sea visible.
            // Valores NUMÉRICOS de los enums C# (AnimationKind.Marquee=2, Normal=1,
            // Direction.Left=0, Slot.Main=1) para que el Save deserialize correctamente.
            obj.position.y = Math.min(pos.y, Math.max(0, this.canvas.height - 7));
            obj.animations = [{
                kind: 2, speedPreset: 1, direction: 0,
                loop: true, slot: 1,
            }];
        }
        this.layer().objects.push(obj);
        this.markDirty();
        return obj;
    }

    startDrawingSession(logical, tool) {
        const scene = this.currentScene();
        this.drawingSession = {
            obj: {
                id: this.newId(), kind: 'drawing', name: 'Dibujo',
                position: { x: logical.x, y: logical.y },
                size: { width: 1, height: 1 }, visible: true, locked: false, brightness: 255,
                timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
                bitsPerPixel: 1, palette: [{ r: tool === 'eraser' ? 0 : 255, g: tool === 'eraser' ? 0 : 255, b: tool === 'eraser' ? 0 : 255 }],
                pixelData: [], bounds: { origin: { x: 0, y: 0 }, size: { width: 0, height: 0 } },
            },
            tool,
            // Punto inicial del pointerdown SIEMPRE incluido (coordenadas ABSOLUTAS).
            points: [[logical.x, logical.y]],
            minX: logical.x, minY: logical.y, maxX: logical.x, maxY: logical.y,
        };
    }

    continueDrawing(logical) {
        const s = this.drawingSession;
        // Guardar puntos ABSOLUTOS durante el stroke; en cierre se rebase contra minX/minY.
        s.points.push([logical.x, logical.y]);
        s.minX = Math.min(s.minX, logical.x); s.maxX = Math.max(s.maxX, logical.x);
        s.minY = Math.min(s.minY, logical.y); s.maxY = Math.max(s.maxY, logical.y);
        // Previsualización incremental (clamp implícito al canvas vía toLogical).
        const ctx = this.ctx;
        ctx.fillStyle = this.pixelLayerColor(s);
        ctx.fillRect(logical.x, logical.y, 1, 1);
    }

    pixelLayerColor(session) {
        const c = session.obj.palette[0];
        return `#${c.r.toString(16).padStart(2, '0')}${c.g.toString(16).padStart(2, '0')}${c.b.toString(16).padStart(2, '0')}`;
    }

    endDrawingSession() {
        const s = this.drawingSession;
        if (!s) return;
        const w = s.maxX - s.minX + 1;
        const h = s.maxY - s.minY + 1;
        s.obj.position = { x: s.minX, y: s.minY };
        s.obj.size = { width: w, height: h };
        // Rebase de puntos ABSOLUTOS contra minX/minY: correcto para dibujar hacia
        // izquierda, arriba y diagonales (coordenadas locales siempre >= 0).
        const data = new Uint8Array(w * h);
        for (const [ax, ay] of s.points) {
            const dx = ax - s.minX;
            const dy = ay - s.minY;
            if (dx >= 0 && dy >= 0 && dx < w && dy < h) data[dy * w + dx] = 1;
        }
        s.obj.pixelData = Array.from(data);
        s.obj.bounds = { origin: { x: 0, y: 0 }, size: { width: w, height: h } };
        this.layer().objects.push(s.obj);
        this.drawingSession = null;
    }

    startShapeSession(scene, logical, tool) {
        this.shapeSession = {
            kind: tool === 'rect' ? 'rectangle' : tool === 'ellipse' ? 'ellipse' : 'line',
            x0: logical.x, y0: logical.y, x1: logical.x, y1: logical.y,
            state: this,
        };
    }

    continueShape(logical) {
        const s = this.shapeSession;
        s.x1 = logical.x; s.y1 = logical.y;
        // re-render + preview
        this.render();
        this.previewShape(s);
    }

    // Preview de forma: dibuja la forma REAL (línea/rectángulo/elipse) con la misma
    // geometría que el renderer, en lugar de un strokeRect genérico. El preview es
    // efímero (se re-renderiza al arrastrar) y no toca el árbol de objetos.
    previewShape(s) {
        const x = Math.min(s.x0, s.x1), y = Math.min(s.y0, s.y1);
        const w = Math.abs(s.x1 - s.x0) + 1, h = Math.abs(s.y1 - s.y0) + 1;
        const shapeNum = s.kind === 'rectangle' ? 1 : s.kind === 'ellipse' ? 2 : 0;
        this.renderer.renderShape({
            kind: 'shape', shape: shapeNum,
            position: { x, y }, size: { width: w, height: h },
            strokeColor: { r: 255, g: 255, b: 255 },
            fillColor: { r: 0, g: 0, b: 0 },
        }, 0, 0, 1, null);
    }

    endShapeSession() {
        const s = this.shapeSession;
        if (!s) return;
        const x = Math.min(s.x0, s.x1), y = Math.min(s.y0, s.y1);
        // +1 para que la dimensión coincida con el renderer (itera i < w, i < h),
        // de modo que un rect desde (5,6) a (10,9) mida exactamente 6x4.
        const w = Math.abs(s.x1 - s.x0) + 1, h = Math.abs(s.y1 - s.y0) + 1;
        const obj = {
            id: this.newId(), kind: 'shape', name: s.kind,
            // ShapeKind C#: Line=0, Rectangle=1, Ellipse=2 (contrato de enum).
            shape: s.kind === 'rectangle' ? 1 : s.kind === 'ellipse' ? 2 : 0,
            position: { x, y }, size: { width: Math.max(w, 1), height: Math.max(h, 1) },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: this.currentScene().duration ?? 5000 }, animations: [],
            strokeColor: { r: 255, g: 255, b: 255 }, fillColor: { r: 0, g: 0, b: 0 },
            strokeWidth: 1,
        };
        this.layer().objects.push(obj);
        this.shapeSession = null;
    }

    // ----- selección util -----
    layer() {
        const scene = this.currentScene();
        if (!scene.layers || scene.layers.length === 0)
            scene.layers = [{ id: 'l1', name: 'Capa 1', order: 0, visible: true, locked: false, objects: [] }];
        const idx = this.currentLayerIndex();
        const sorted = [...scene.layers].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
        return sorted[idx] || sorted[0];
    }

    // Índice de la capa activa en el selector (para `layer()`).
    currentLayerIndex() {
        const sel = document.getElementById('layer-select');
        const idx = sel ? parseInt(sel.value, 10) : 0;
        const scene = this.currentScene();
        const count = [...(scene?.layers || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).length;
        return (idx >= 0 && idx < count) ? idx : 0;
    }

    drawSelectionOverlay() {
        const ctx = this.overlayCtx || this.ctx;
        ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        ctx.strokeStyle = '#0d6efd';
        ctx.lineWidth = 1;
        for (const obj of this.selection.list()) {
            const x = obj.position?.x ?? 0, y = obj.position?.y ?? 0;
            const w = this.selection.guessSize(obj).w, h = this.selection.guessSize(obj).h;
            ctx.strokeRect(x - 0.5, y - 0.5, w + 1, h + 1);
        }
        // superpone el rectángulo de selección en marcha (mismo overlay)
        const s = this.tools.rectSelect;
        if (s) {
            ctx.strokeRect(Math.min(s.x0, s.x1), Math.min(s.y0, s.y1),
                Math.abs(s.x1 - s.x0), Math.abs(s.y1 - s.y0));
        }
    }

    drawRectSelect() {
        // dibuja el overlay completo (selección + rect en marcha) sobre el overlay-canvas
        this.drawSelectionOverlay();
    }

    newId() {
        // ObjectId N (32 hex)
        const b = new Uint8Array(16);
        crypto.getRandomValues(b);
        return Array.from(b).map(x => x.toString(16).padStart(2, '0')).join('');
    }

    markDirty() {
        this.dirty = true;
        if (this.hud) this.hud.setDirty(true);
    }

    // ---- contrato de píxeles C# ↔ JS (único, probado) ----
    // C# serializa byte[] como base64 string; el modelo JS usa array de números.
    // Al cargar se decodifica base64→array; al guardar se codifica array→base64.

    normalizePixels(project) {
        for (const scene of project.scenes || [])
            for (const layer of scene.layers || [])
                for (const obj of layer.objects || []) {
                    if (obj.kind === 'drawing' && typeof obj.pixelData === 'string') {
                        obj.pixelData = this.pixelsFromBase64(obj.pixelData);
                    } else if (obj.kind === 'drawing' && !Array.isArray(obj.pixelData)) {
                        obj.pixelData = [];
                    }
                }
    }

    pixelsFromBase64(b64) {
        const bin = atob(b64);
        const out = [];
        for (let i = 0; i < bin.length; i++) out.push(bin.charCodeAt(i) & 0xff);
        return out;
    }

    pixelsToBase64(arr) {
        const bytes = new Uint8Array(arr.length);
        for (let i = 0; i < arr.length; i++) bytes[i] = arr[i] & 0xff;
        let bin = '';
        for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return btoa(bin);
    }

    // Copia del proyecto con pixelData codificado a base64 (formato que espera C#).
    projectForWire() {
        const clone = JSON.parse(JSON.stringify(this.project));
        for (const scene of clone.scenes || [])
            for (const layer of scene.layers || [])
                for (const obj of layer.objects || []) {
                    if (obj.kind === 'drawing' && Array.isArray(obj.pixelData))
                        obj.pixelData = this.pixelsToBase64(obj.pixelData);
                }
        return clone;
    }

    // ----- acciones de UI -----
    bindUi() {
        document.getElementById('btn-preview')?.addEventListener('click', () => this.openSimulator());
        document.getElementById('simulator-play')?.addEventListener('click', () => this.togglePlay());
        document.getElementById('simulator-stop')?.addEventListener('click', () => this.restartPlayback());
        document.querySelectorAll('.tool-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.tool-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.tools.setTool(btn.dataset.tool);
            });
        });
        document.getElementById('btn-save').addEventListener('click', () => this.save());
        // Nuevo proyecto (spec 12): modal de matriz → POST /Editor/New → recarga editor.
        // Con cambios sin guardar, primero se ofrece Guardar/Descartar/Cancelar; sólo
        // con Guardar exitoso o Descartar se abre el modal de Nueva matriz.
        document.getElementById('btn-new').addEventListener('click', () => {
            const openNewModal = () => {
                const m = document.getElementById('new-modal');
                if (m && window.bootstrap) bootstrap.Modal.getOrCreateInstance(m).show();
            };
            if (this.dirty && this.hud) this.hud.confirmNavigation(openNewModal);
            else openNewModal();
        });
        const newMatrix = document.getElementById('new-matrix');
        if (newMatrix) {
            newMatrix.addEventListener('change', () => {
                document.getElementById('new-custom').classList.toggle('d-none', newMatrix.value !== 'custom');
            });
        }
        const newCreate = document.getElementById('btn-new-create');
        if (newCreate) {
            newCreate.addEventListener('click', async () => {
                const name = document.getElementById('new-name').value || 'Sin título';
                let w = 32, h = 16;
                const mv = newMatrix.value;
                if (mv === 'custom') {
                    w = parseInt(document.getElementById('new-width').value, 10) || 32;
                    h = parseInt(document.getElementById('new-height').value, 10) || 16;
                } else if (mv && mv.includes(',')) {
                    [w, h] = mv.split(',').map(n => parseInt(n, 10));
                }
                const form = new FormData();
                form.append('name', name);
                form.append('width', String(Math.max(1, Math.min(256, w))));
                form.append('height', String(Math.max(1, Math.min(256, h))));
                const res = await fetch('/Editor/New', {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': window.__antiforgery?.token || '' },
                    body: form,
                });
                if (res.redirected) window.location.href = res.url;
                else window.location.href = '/Editor';
            });
        }
        document.getElementById('btn-play').addEventListener('click', () => this.togglePlay());
        document.getElementById('btn-pause')?.addEventListener('click', () => this.pausePlayback());
        document.getElementById('btn-stop')?.addEventListener('click', () => this.restartPlayback());
        document.getElementById('btn-save-library').addEventListener('click', () => this.saveToLibrary());
        document.getElementById('btn-library').addEventListener('click', () => this.openLibrary());
        document.getElementById('btn-image').addEventListener('click', () => document.getElementById('image-file').click());
        document.getElementById('image-file').addEventListener('change', (e) => {
            if (e.target.files && e.target.files[0]) this.importImage(e.target.files[0]);
            e.target.value = '';
        });
        // búsqueda del icon picker integrado (con debounce leve, sin perder foco)
        const iconSearch = document.getElementById('icon-picker-search');
        if (iconSearch) {
            let t = null;
            iconSearch.addEventListener('input', () => {
                clearTimeout(t);
                t = setTimeout(() => this.populateIconPicker(iconSearch.value), 120);
            });
        }
        document.querySelectorAll('[data-lib-tab]').forEach(b => {
            b.addEventListener('click', () => this.loadLibraryTab(b.dataset.libTab));
        });
        document.getElementById('scene-select').addEventListener('change', () => {
            this.populateLayerSelect();
            this.syncSceneDurationInput();
            this.updateSceneTimeLabel();
            this.render();
        });
        document.getElementById('layer-select').addEventListener('change', () => this.render());
        document.getElementById('btn-add-scene').addEventListener('click', () => this.addScene());
        document.getElementById('btn-add-layer').addEventListener('click', () => this.addLayer());
        document.getElementById('scene-duration').addEventListener('change', () => this.setSceneDuration());

        // group / ungroup / align
        document.getElementById('btn-group').addEventListener('click', () => this.groupSelected());
        document.getElementById('btn-ungroup').addEventListener('click', () => this.ungroupSelected());
        document.getElementById('btn-align-left').addEventListener('click', () => this.alignSelected('left'));
        document.getElementById('btn-align-hcenter').addEventListener('click', () => this.alignSelected('hcenter'));
        document.getElementById('btn-align-right').addEventListener('click', () => this.alignSelected('right'));
        document.getElementById('btn-align-top').addEventListener('click', () => this.alignSelected('top'));
        document.getElementById('btn-align-vmiddle').addEventListener('click', () => this.alignSelected('vmiddle'));
        document.getElementById('btn-align-bottom').addEventListener('click', () => this.alignSelected('bottom'));

        // teclado: borrar/duplicar/undo/redo/group
        window.addEventListener('keydown', e => this.onKeyDown(e));
    }

    openSimulator() {
        const modal = document.getElementById('simulator-modal');
        const preview = document.getElementById('simulator-canvas');
        if (!modal || !preview || !this.canvas) return;
        preview.width = this.canvas.width;
        preview.height = this.canvas.height;
        const maxW = Math.max(160, Math.min(window.innerWidth - 120, 960));
        const scale = Math.max(4, Math.floor(maxW / preview.width));
        preview.style.width = `${preview.width * scale}px`;
        preview.style.height = `${preview.height * scale}px`;
        this.previewCanvas = preview;
        if (window.bootstrap) bootstrap.Modal.getOrCreateInstance(modal).show();
        this.renderSimulator();
    }

    renderSimulator() {
        if (!this.previewCanvas || !this.renderer) return;
        const ctx = this.previewCanvas.getContext('2d');
        const previewRenderer = new Renderer(this.previewCanvas, ctx);
        previewRenderer.embeddedAssets = this.project?.embeddedAssets || {};
        previewRenderer.renderScene(this.currentScene(), this.currentTime);
    }

    onKeyDown(e) {
        if (e.key === 'Delete' || e.key === 'Backspace') {
            const sel = this.selection.list().filter(o => !o.locked);   // locked no se borra
            if (sel.length) {
                this.history.captureOnce(this.project);
                const ids = new Set(sel.map(o => o.id));
                this.removeObjects(ids);
                this.history.commitPending();
                this.selection.deselectAll();
                this.markDirty();
                this.render();
            }
        } else if (e.key === 'd' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            this.duplicateSelected();
        } else if (e.key === 'z' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            this.performUndo();
        } else if (e.key === 'y' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            this.performRedo();
        } else if (e.key === 'g' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            if (e.shiftKey) this.ungroupSelected();
            else this.groupSelected();
        }
    }

    removeObjects(ids) {
        for (const scene of this.project.scenes || [])
            for (const layer of scene.layers || [])
                layer.objects = (layer.objects || []).filter(o => !ids.has(o.id));
        // limpia referencias de grupos a miembros borrados
        for (const scene of this.project.scenes || [])
            for (const g of scene.groups || [])
                g.memberIds = (g.memberIds || []).filter(id => !ids.has(id));
    }

    // Selección rectangular: delega en el Selection.
    selectRect(rect) {
        this.selection.selectRect(rect);
    }

    duplicateSelected() {
        const sel = this.selection.list();
        if (!sel.length) return;
        this.history.captureOnce(this.project);
        const copies = sel.map(o => {
            const c = JSON.parse(JSON.stringify(o));
            c.id = this.newId();
            c.name = (o.name || '') + ' (copia)';
            c.position = { x: (o.position?.x ?? 0) + 1, y: (o.position?.y ?? 0) + 1 };
            return c;
        });
        this.layer().objects.push(...copies);
        this.history.commitPending();
        this.markDirty();
        this.render();
    }

    // ----- Group / Ungroup / Align (spec 5 + 8) -----

    // Agrupa la selección (≥2 objetos): crea un ObjectGroup con IDs únicos/resolubles.
    // El grupo no tiene contenido visual: el framebuffer no cambia.
    groupSelected() {
        const sel = this.selection.list();
        if (sel.length < 2) { this.notify(false, 'Selecciona al menos 2 objetos para agrupar.'); return; }
        const scene = this.currentScene();
        if (!scene) return;
        this.history.captureOnce(this.project);
        const ids = sel.map(o => o.id);
        scene.groups = scene.groups || [];
        const key = ids.slice().sort().join(',');
        const existing = scene.groups.find(g => (g.memberIds || []).slice().sort().join(',') === key);
        if (!existing) {
            scene.groups.push({ id: this.newId(), name: `Grupo ${scene.groups.length + 1}`, memberIds: ids });
        }
        this.history.commitPending();
        this.markDirty();
        // el group no cambia el framebuffer; sólo re-render para overlay
        this.render();
        this.notify(true, `Grupo creado (${ids.length} objetos)`);
    }

    // Desagrupa los grupos cuya intersección con la selección contenga ≥1 miembro,
    // conservando los objetos (framebuffer idéntico).
    ungroupSelected() {
        const scene = this.currentScene();
        if (!scene) return;
        const sel = new Set(this.selection.list().map(o => o.id));
        const groups = scene.groups || [];
        const touched = groups.filter(g => (g.memberIds || []).some(id => sel.has(id)));
        if (touched.length === 0) { this.notify(false, 'La selección no pertenece a ningún grupo.'); return; }
        this.history.captureOnce(this.project);
        scene.groups = groups.filter(g => !touched.includes(g));
        this.history.commitPending();
        this.markDirty();
        this.render();
        this.notify(true, `Desagrupado (${touched.length} grupo/s)`);
    }

    // Alinea la selección (≥1 objeto). No modifica tamaño/timing/animation/layer.
    alignSelected(direction) {
        const list = this.selection.list().filter(o => !o.locked);   // locked respetado
        if (list.length === 0) return;
        this.history.captureOnce(this.project);
        const left = Math.min(...list.map(o => o.position?.x ?? 0));
        const right = Math.max(...list.map(o => (o.position?.x ?? 0) + (o.size?.width ?? this.selection.guessSize(o).w)));
        const top = Math.min(...list.map(o => o.position?.y ?? 0));
        const bottom = Math.max(...list.map(o => (o.position?.y ?? 0) + (o.size?.height ?? this.selection.guessSize(o).h)));
        for (const o of list) {
            const w = o.size?.width ?? this.selection.guessSize(o).w;
            const h = o.size?.height ?? this.selection.guessSize(o).h;
            o.position = o.position || { x: 0, y: 0 };
            switch (direction) {
                case 'left': o.position.x = left; break;
                case 'right': o.position.x = right - w; break;
                case 'hcenter': o.position.x = left + Math.floor((right - left - w) / 2); break;
                case 'top': o.position.y = top; break;
                case 'bottom': o.position.y = bottom - h; break;
                case 'vmiddle': o.position.y = top + Math.floor((bottom - top - h) / 2); break;
            }
        }
        this.history.commitPending();
        this.markDirty();
        this.render();
    }

    performUndo() {
        const prev = this.history.undo(this.project);
        if (prev) { this.project = prev; this.markDirty(); this.render(); }
    }

    performRedo() {
        const next = this.history.redo(this.project);
        if (next) { this.project = next; this.markDirty(); this.render(); }
    }

    async saveToLibrary() {
        const sel = this.selection.list();
        const drawable = sel.find(o => o.kind === 'drawing');
        if (!drawable) return;
        const width = drawable.size?.width ?? 0;
        const height = drawable.size?.height ?? 0;
        // pixelData llega como Array de 0/1
        const pixels = new Uint8Array(width * height);
        const src = drawable.pixelData || [];
        for (let i = 0; i < src.length && i < pixels.length; i++) pixels[i] = src[i];
        const name = window.prompt('Nombre del dibujo:', drawable.name || 'Dibujo');
        if (!name) return;
        const res = await fetch('/Library/SaveDrawing', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': window.__antiforgery?.token || '',
            },
            body: JSON.stringify({
                name, width, height,
                pixels: Array.from(pixels),
                palette: (drawable.palette || []).map(c => ({ r: c.r, g: c.g, b: c.b })),
            }),
        });
        const data = await res.json();
        this.notify(data.success, data.success
            ? `Guardado en Mi biblioteca (${data.id})`
            : ('Error: ' + data.message));
    }

    // Notificación consolidada (success/warning/error) vía el HUD.
    notify(success, message) {
        if (this.hud) this.hud.notify(success ? 'success' : 'error', message);
    }

    // ----- biblioteca (modal en el editor) -----
    openLibrary() {
        const modal = document.getElementById('library-modal');
        if (modal && window.bootstrap) {
            bootstrap.Modal.getOrCreateInstance(modal).show();
        }
        this.loadLibraryTab('drawings');
    }

    // ----- icon picker integrado (spec 19) -----
    openIconPicker() {
        const modal = document.getElementById('icon-picker-modal');
        if (modal && window.bootstrap) {
            bootstrap.Modal.getOrCreateInstance(modal).show();
        }
        this.populateIconPicker('');
    }

    async populateIconPicker(query) {
        const grid = document.getElementById('icon-picker-grid');
        if (!grid) return;
        const res = await fetch('/Library/Icons');
        const data = await res.json();
        const icons = data.icons || [];
        const q = (query || '').trim().toLowerCase();
        const filtered = !q ? icons : icons.filter(i =>
            (i.name || '').toLowerCase().includes(q) ||
            (i.category || '').toLowerCase().includes(q) ||
            (i.aliases || []).some(a => a.toLowerCase().includes(q)));
        grid.innerHTML = '';
        if (filtered.length === 0) {
            grid.innerHTML = '<div class="col-12 text-muted">Sin resultados para esta búsqueda.</div>';
            return;
        }
        for (const icon of filtered) {
            grid.appendChild(this.iconPickerCard(icon));
        }
    }

    iconPickerCard(icon) {
        const col = document.createElement('div');
        col.className = 'col-auto';
        const w = icon.width, h = icon.height;
        col.innerHTML = `
            <div class="card bg-secondary text-light" style="width:96px">
                <div class="card-body p-2 text-center">
                    <canvas width="${w}" height="${h}" style="width:56px;height:56px;image-rendering:pixelated;background:#000"></canvas>
                    <div class="small mt-1 text-truncate" title="${this.esc(icon.name || '')}">${this.esc(icon.name || '')}</div>
                    <div class="small text-muted">${this.esc(icon.category || '')}</div>
                </div>
            </div>`;
        this.drawAssetPreview(col.querySelector('canvas'), icon);
        col.querySelector('.card').addEventListener('click', () => {
            this.insertIconAt(icon, this._pendingIconPos || { x: 0, y: 0 });
            this._pendingIconPos = null;
            bootstrap.Modal.getOrCreateInstance(document.getElementById('icon-picker-modal')).hide();
        });
        return col;
    }

    // Inserta un IconObject en una posición concreta (icon picker integrado).
    insertIconAt(icon, pos) {
        const scene = this.currentScene();
        if (!scene) return;
        this.history.captureOnce(this.project);
        const assetId = icon.id;
        this.project.embeddedAssets = this.project.embeddedAssets || {};
        this.project.embeddedAssets[assetId] = JSON.stringify({
            width: icon.width, height: icon.height,
            pixels: icon.pixels,
            palette: (icon.palette || []).map(c => ({ r: c.r, g: c.g, b: c.b })),
            ...(icon.transparentIndex != null && icon.transparentIndex >= 0
                ? { transparentIndex: icon.transparentIndex } : {}),
        });
        const obj = {
            id: this.newId(), kind: 'icon', name: icon.name || 'Icono',
            position: { x: pos.x, y: pos.y }, size: { width: icon.width, height: icon.height },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            assetId, paletteMode: 0, tint: { r: 255, g: 255, b: 255 },
        };
        this.layer().objects.push(obj);
        this.history.commitPending();
        this.markDirty();
        this.render();
        this.notify(true, `Icono "${icon.name}" insertado`);
    }

    loadLibraryTab(tab) {
        // marca la pestaña activa
        document.querySelectorAll('[data-lib-tab]').forEach(b =>
            b.classList.toggle('active', b.dataset.libTab === tab));
        const grid = document.getElementById('library-grid');
        grid.innerHTML = '<div class="col-12 text-muted">Cargando…</div>';
        if (tab === 'icons') this.loadIcons(grid);
        else if (tab === 'images') this.loadImages(grid);
        else this.loadDrawings(grid);
    }

    async loadDrawings(grid) {
        const res = await fetch('/Library/Drawings');
        const data = await res.json();
        grid.innerHTML = '';
        const items = data.drawings || [];
        if (items.length === 0) {
            grid.innerHTML = '<div class="col-12 text-muted">No hay dibujos guardados.</div>';
            return;
        }
        for (const d of items) {
            grid.appendChild(this.libraryCard(d, 'drawing'));
        }
    }

    async loadIcons(grid) {
        const res = await fetch('/Library/Icons');
        const data = await res.json();
        grid.innerHTML = '';
        const items = data.icons || [];
        for (const it of items) {
            grid.appendChild(this.libraryCard(it, 'icon'));
        }
    }

    async loadImages(grid) {
        const res = await fetch('/Library/Images');
        const data = await res.json();
        grid.innerHTML = '';
        const items = data.images || [];
        if (items.length === 0) {
            grid.innerHTML = '<div class="col-12 text-muted">No hay imágenes importadas.</div>';
            return;
        }
        for (const im of items) {
            grid.appendChild(this.libraryCard(im, 'image'));
        }
    }

    // Tarjeta de asset con preview canvas + botón Insertar.
    libraryCard(asset, kind) {
        const col = document.createElement('div');
        col.className = 'col-auto';
        const w = asset.width, h = asset.height;
        col.innerHTML = `
            <div class="card bg-secondary text-light" style="width:110px">
                <div class="card-body p-2 text-center">
                    <canvas width="${w}" height="${h}" style="width:64px;height:64px;image-rendering:pixelated;background:#000"></canvas>
                    <div class="small mt-1 text-truncate" title="${this.esc(asset.name || '')}">${this.esc(asset.name || '')}</div>
                    ${kind === 'icon' ? `<div class="small text-muted">${this.esc(asset.category || '')}</div>` : ''}
                    <button class="btn btn-sm btn-outline-light mt-1 w-100" type="button">Insertar</button>
                </div>
            </div>`;
        // preview
        const cv = col.querySelector('canvas');
        this.drawAssetPreview(cv, asset);
        // insertar
        col.querySelector('button').addEventListener('click', () => {
            if (kind === 'icon') this.insertIconAsset(asset);
            else if (kind === 'image') this.insertImageAsset(asset);
            else this.insertDrawingAsset(asset);
            bootstrap.Modal.getOrCreateInstance(document.getElementById('library-modal')).hide();
        });
        return col;
    }

    esc(s) {
        const d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    drawAssetPreview(canvas, asset) {
        const ctx = canvas.getContext('2d');
        const w = asset.width, h = asset.height;
        const pixels = this.decodeBase64Bytes(asset.pixels);
        const palette = asset.palette || [];
        for (let y = 0; y < h; y++)
            for (let x = 0; x < w; x++) {
                const idx = y * w + x;
                if (idx >= pixels.length) continue;
                const pi = pixels[idx];
                if (pi < 0 || pi >= palette.length) continue;
                const c = palette[pi];
                ctx.fillStyle = c ? `#${this.hx(c.r)}${this.hx(c.g)}${this.hx(c.b)}` : '#fff';
                ctx.fillRect(x, y, 1, 1);
            }
    }

    hx(n) { return Math.max(0, Math.min(255, n)).toString(16).padStart(2, '0'); }

    decodeBase64Bytes(b64) {
        if (typeof b64 !== 'string') return [];
        const bin = atob(b64);
        const out = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
        return Array.from(out);
    }

    // Inserta un IconObject: copia independiente + asset embebido en el proyecto.
    insertIconAsset(asset) {
        const scene = this.currentScene();
        if (!scene) return;
        this.history.captureOnce(this.project);
        const assetId = asset.id;
        this.project.embeddedAssets = this.project.embeddedAssets || {};
        this.project.embeddedAssets[assetId] = JSON.stringify({
            width: asset.width, height: asset.height,
            pixels: asset.pixels,
            palette: (asset.palette || []).map(c => ({ r: c.r, g: c.g, b: c.b })),
            ...(asset.transparentIndex != null && asset.transparentIndex >= 0
                ? { transparentIndex: asset.transparentIndex } : {}),
        });
        const obj = {
            id: this.newId(), kind: 'icon', name: asset.name || 'Icono',
            position: { x: 0, y: 0 }, size: { width: asset.width, height: asset.height },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            assetId, paletteMode: 0, tint: { r: 255, g: 255, b: 255 },
        };
        this.layer().objects.push(obj);
        this.history.commitPending();
        this.markDirty();
        this.render();
        this.notify(true, `Icono "${asset.name}" insertado`);
    }

    // Inserta un dibujo de la biblioteca como DrawingObject (copia independiente).
    insertDrawingAsset(asset) {
        const scene = this.currentScene();
        if (!scene) return;
        this.history.captureOnce(this.project);
        const pixels = this.decodeBase64Bytes(asset.pixels);
        const obj = {
            id: this.newId(), kind: 'drawing', name: asset.name || 'Dibujo',
            position: { x: 0, y: 0 }, size: { width: asset.width, height: asset.height },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            bitsPerPixel: 1, palette: (asset.palette || [{ r: 255, g: 255, b: 255 }]).map(c => ({ r: c.r, g: c.g, b: c.b })),
            pixelData: pixels,
            bounds: { origin: { x: 0, y: 0 }, size: { width: asset.width, height: asset.height } },
        };
        this.layer().objects.push(obj);
        this.history.commitPending();
        this.markDirty();
        this.render();
        this.notify(true, `Dibujo "${asset.name}" insertado`);
    }

    // Inserta una imagen importada de la biblioteca como ImageObject (copia independiente
    // + asset embebido). La imagen persistió en la biblioteca al importarse; aquí se
    // re-embebe su contenido dentro del proyecto para no depender del catálogo externo.
    insertImageAsset(asset) {
        const scene = this.currentScene();
        if (!scene) return;
        this.history.captureOnce(this.project);
        const assetId = asset.id;
        this.project.embeddedAssets = this.project.embeddedAssets || {};
        this.project.embeddedAssets[assetId] = JSON.stringify({
            width: asset.width, height: asset.height,
            pixels: asset.pixels,
            palette: (asset.palette || []).map(c => ({ r: c.r, g: c.g, b: c.b })),
        });
        const obj = {
            id: this.newId(), kind: 'image', name: asset.name || 'Imagen',
            position: { x: 0, y: 0 }, size: { width: asset.width, height: asset.height },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            assetId, conversionMetadata: asset.conversionMetadata || '',
        };
        this.layer().objects.push(obj);
        this.history.commitPending();
        this.markDirty();
        this.render();
        this.notify(true, `Imagen "${asset.name}" insertada`);
    }

    // Importa una imagen (spec 15): decode → preview → rasteriza (nearest-neighbor +
    // quantize + dither) vía /Library/RasterizeImage → inserta ImageObject con el asset
    // EMBEBIDO en el proyecto (nunca una ruta externa).
    async importImage(file) {
        try {
            const bmp = await this.decodeImageFile(file);
            if (!bmp) { this.notify(false, 'No se pudo decodificar la imagen.'); return; }

            // Target = tamaño del canvas lógico (la imagen se escala al lienzo; si excede,
            // se escala proporcionalmente para no exceder 256 px por lado).
            const cw = this.canvas.width, ch = this.canvas.height;
            let tw = cw, th = ch;

            const rgba = new Array(bmp.width * bmp.height * 4);
            for (let i = 0; i < bmp.data.length; i++) rgba[i] = bmp.data[i];

            const res = await fetch('/Library/RasterizeImage', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': window.__antiforgery?.token || '',
                },
                body: JSON.stringify({
                    name: file.name || 'Imagen',
                    format: file.type || 'png',
                    srcWidth: bmp.width, srcHeight: bmp.height,
                    targetWidth: tw, targetHeight: th,
                    rgba,
                    dither: true, maxColors: 16,
                }),
            });
            const data = await res.json();
            if (!res.ok || !data.success) {
                this.notify(false, 'Error al rasterizar: ' + (data.message || res.status));
                return;
            }

            // Embebe el asset en el proyecto y crea un ImageObject con copia independiente.
            const scene = this.currentScene();
            if (!scene) return;
            this.history.captureOnce(this.project);
            this.project.embeddedAssets = this.project.embeddedAssets || {};
            this.project.embeddedAssets[data.assetId] = data.assetJson;
            const parsed = JSON.parse(data.assetJson);
            const obj = {
                id: this.newId(), kind: 'image', name: file.name || 'Imagen',
                position: { x: 0, y: 0 }, size: { width: parsed.width, height: parsed.height },
                visible: true, locked: false, brightness: 255,
                timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
                assetId: data.assetId, conversionMetadata: parsed.conversionMetadata || '',
            };
            this.layer().objects.push(obj);
            this.history.commitPending();
            this.markDirty();
            this.render();
            this.notify(true, `Imagen "${file.name}" insertada`);
        } catch (err) {
            this.notify(false, 'Error importando imagen: ' + (err?.message || err));
        }
    }

    decodeImageFile(file) {
        return new Promise((resolve, reject) => {
            const url = URL.createObjectURL(file);
            const img = new Image();
            img.onload = () => {
                const c = document.createElement('canvas');
                c.width = img.naturalWidth || img.width;
                c.height = img.naturalHeight || img.height;
                const ctx = c.getContext('2d');
                ctx.drawImage(img, 0, 0);
                URL.revokeObjectURL(url);
                try { resolve(ctx.getImageData(0, 0, c.width, c.height)); }
                catch (e) { reject(e); }
            };
            img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('decode')); };
            img.src = url;
        });
    }

    updateSceneTimeLabel() {
        const scene = this.currentScene();
        if (!scene) return;
        document.getElementById('scene-time').textContent =
            `${(this.currentTime / 1000).toFixed(1)}s / ${(scene.duration / 1000).toFixed(1)}s`;
    }

    async save() {
        const res = await fetch('/Projects/Save', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': window.__antiforgery?.token || '',
            },
            body: JSON.stringify(this.projectForWire()),
        });
        const data = await res.json();
        if (data.success) {
            this.dirty = false;
            if (this.hud) this.hud.setDirty(false);
            this.notify(true, 'Guardado');
        } else {
            this.notify(false, 'Error: ' + (data.message || 'desconocido'));
        }
    }

    // ----- playback (rAF + tiempo real de reloj, loop modes) -----

    // Estado de reproducción centralizado (nunca Dataset como fuente de verdad).
    startPlayback() {
        const scene = this.currentScene();
        if (!scene) return;
        if (this._playback) return;              // ya reproduciendo
        const dur = scene.duration ?? 5000;
        const loopMode = scene.loopMode ?? 'loop';
        const startWall = performance.now();
        const startTime = this.currentTime;

        this._playback = { raf: null, startWall, startTime, dur, loopMode };

        const tick = (now) => {
            const p = this._playback;
            if (!p) return;                       // detenido
            const elapsed = now - p.startWall;
            let t = p.startTime + elapsed;

            if (t >= p.dur) {
                if (p.loopMode === 'once' || p.loopMode === 0) {
                    // Once: queda clavado en el final y se DETIENE de verdad.
                    this.currentTime = p.dur;
                    this.stopPlayback(false);
                    this.render();
                    this.updateSceneTimeLabel();
                    return;
                }
                if (p.loopMode === 'pingpong' || p.loopMode === 2) {
                    // PingPong: invertir dirección y rebotar dentro de [0, dur).
                    const trips = Math.floor(t / p.dur);
                    const rem = t % p.dur;
                    this.currentTime = (trips % 2 === 0) ? rem : (p.dur - rem);
                } else {
                    // Loop (default): envolver.
                    this.currentTime = t % p.dur;
                }
            } else {
                this.currentTime = t;
            }

            this.render();
            this.updateSceneTimeLabel();
            p.raf = requestAnimationFrame(tick);
        };

        this._playback.raf = requestAnimationFrame(tick);
    }

    stopPlayback(reset = true) {
        const p = this._playback;
        if (p && p.raf) cancelAnimationFrame(p.raf);
        this._playback = null;
        document.getElementById('btn-play')?.classList.remove('playing');
        document.getElementById('btn-play')?.setAttribute('aria-pressed', 'false');
        if (reset) this.currentTime = 0;
    }

    togglePlay() {
        if (this._playback) {
            // stop REAL: cancela el rAF, limpia estado, no deja nada residual.
            this.stopPlayback(false);
            this.render();
            this.updateSceneTimeLabel();
            return;
        }
        const btn = document.getElementById('btn-play');
        btn?.classList.add('playing');
        btn?.setAttribute('aria-pressed', 'true');
        if (this.currentTime <= 0) this.currentTime = 0;
        this.startPlayback();
    }

    // Pausa SIN resetear el playhead (spec 29): congela en el tiempo actual.
    pausePlayback() {
        if (!this._playback) return;
        this.stopPlayback(false);
        this.render();
        this.updateSceneTimeLabel();
    }

    // Reinicia el playhead a 0 (sin iniciar reproducción).
    restartPlayback() {
        this.stopPlayback(true);   // resetea currentTime a 0
        this.render();
        this.updateSceneTimeLabel();
    }
}
