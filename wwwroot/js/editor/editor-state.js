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
    }

    async loadProject(projectId) {
        const res = await fetch(`/Editor/Load?id=${projectId}`);
        const data = await res.json();
        if (!data.success) throw new Error(data.message || 'No se pudo cargar el proyecto');
        this.project = JSON.parse(data.project);
        document.getElementById('project-name').textContent = this.project.name || 'Sin título';
        this.setupCanvas();
        this.populateSceneSelect();
        this.updateSceneTimeLabel();
        this.render();
    }

    setupCanvas() {
        const canvas = document.getElementById('led-canvas');
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        canvas.width = this.project.canvas.width;
        canvas.height = this.project.canvas.height;
        canvas.style.width = (this.project.canvas.width * this.pixelScale) + 'px';
        canvas.style.height = (this.project.canvas.height * this.pixelScale) + 'px';
        this.renderer = new Renderer(canvas, this.ctx);
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
    }

    render() {
        if (!this.renderer) return;
        const scene = this.currentScene();
        if (scene) this.renderer.renderScene(scene, this.currentTime);
        this.drawSelectionOverlay();
        this.drawRectSelect();
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

    currentLayer(scene) {
        return (scene.layers || []).sort((a, b) => (a.order ?? 0) - (b.order ?? 0))[0] || null;
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
        } else if (this.tools.activeTool === 'pencil' || this.tools.activeTool === 'eraser') {
            this.history.captureOnce(this.project);
            this.startDrawingSession(logical, this.tools.activeTool);
        } else if (this.tools.activeTool === 'rect' || this.tools.activeTool === 'ellipse' || this.tools.activeTool === 'line') {
            this.history.captureOnce(this.project);
            this.startShapeSession(scene, logical, this.tools.activeTool);
        }
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
            this.tools.endRectSelect();
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
        const obj = {
            id: this.newId(), kind: 'text', name: text.slice(0, 8), text,
            position: { x: pos.x, y: pos.y }, size: { width: text.length * 6 - 1, height: 7 },
            visible: true, locked: false, brightness: 255,
            timing: { start: 0, end: scene.duration ?? 5000 }, animations: [],
            fontId: '5x7', color: { r: 255, g: 255, b: 255 },
            horizontalAlignment: 0, verticalAlignment: 0, layoutMode: 0,
        };
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
            minX: logical.x, minY: logical.y, maxX: logical.x, maxY: logical.y,
            points: [],
        };
    }

    continueDrawing(logical) {
        const s = this.drawingSession;
        s.points.push([logical.x - s.obj.position.x, logical.y - s.obj.position.y]);
        s.minX = Math.min(s.minX, logical.x); s.maxX = Math.max(s.maxX, logical.x);
        s.minY = Math.min(s.minY, logical.y); s.maxY = Math.max(s.maxY, logical.y);
        // redibujar incremental
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
        const data = new Uint8Array(w * h);
        for (const [dx, dy] of s.points) data[dy * w + dx] = 1;
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

    previewShape(s) {
        const ctx = this.ctx;
        ctx.fillStyle = '#fff';
        const x = s.x0, y = s.y0, w = s.x1 - s.x0, h = s.y1 - s.y0;
        ctx.strokeStyle = '#fff';
        ctx.strokeRect(x, y, w, h);
    }

    endShapeSession() {
        const s = this.shapeSession;
        if (!s) return;
        const x = Math.min(s.x0, s.x1), y = Math.min(s.y0, s.y1);
        const w = Math.abs(s.x1 - s.x0), h = Math.abs(s.y1 - s.y0);
        const obj = {
            id: this.newId(), kind: 'shape', name: s.kind,
            shape: s.kind === 'line' ? 1 : s.kind === 'ellipse' ? 2 : 0,
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
        return [...scene.layers].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))[0];
    }

    drawSelectionOverlay() {
        const ctx = this.ctx;
        ctx.strokeStyle = '#0d6efd';
        ctx.lineWidth = 1;
        for (const obj of this.selection.list()) {
            const x = obj.position?.x ?? 0, y = obj.position?.y ?? 0;
            const w = this.selection.guessSize(obj).w, h = this.selection.guessSize(obj).h;
            ctx.strokeRect(x - 0.5, y - 0.5, w + 1, h + 1);
        }
    }

    drawRectSelect() {
        const s = this.tools.rectSelect;
        if (!s) return;
        const ctx = this.ctx;
        ctx.strokeStyle = '#0d6efd';
        ctx.strokeRect(Math.min(s.x0, s.x1), Math.min(s.y0, s.y1), Math.abs(s.x1 - s.x0), Math.abs(s.y1 - s.y0));
    }

    newId() {
        // ObjectId N (32 hex)
        const b = new Uint8Array(16);
        crypto.getRandomValues(b);
        return Array.from(b).map(x => x.toString(16).padStart(2, '0')).join('');
    }

    markDirty() {
        this.dirty = true;
        const bar = document.getElementById('status-bar');
        if (bar) bar.textContent = 'Cambios sin guardar';
        if (this.hud) this.hud.setDirty(true);
    }

    // ----- acciones de UI -----
    bindUi() {
        document.querySelectorAll('.tool-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.tool-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.tools.setTool(btn.dataset.tool);
            });
        });
        document.getElementById('btn-save').addEventListener('click', () => this.save());
        document.getElementById('btn-play').addEventListener('click', () => this.togglePlay());
        document.getElementById('btn-save-library').addEventListener('click', () => this.saveToLibrary());
        document.getElementById('scene-select').addEventListener('change', () => {
            this.updateSceneTimeLabel();
            this.render();
        });

        // teclado: borrar/duplicar/undo/redo
        window.addEventListener('keydown', e => this.onKeyDown(e));
    }

    onKeyDown(e) {
        if (e.key === 'Delete' || e.key === 'Backspace') {
            const sel = this.selection.list();
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
        }
    }

    removeObjects(ids) {
        for (const scene of this.project.scenes || [])
            for (const layer of scene.layers || [])
                layer.objects = (layer.objects || []).filter(o => !ids.has(o.id));
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
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name, width, height,
                pixels: Array.from(pixels),
                palette: (drawable.palette || []).map(c => ({ r: c.r, g: c.g, b: c.b })),
            }),
        });
        const data = await res.json();
        const bar = document.getElementById('status-bar');
        if (bar) bar.textContent = data.success
            ? `Guardado en Mi biblioteca (${data.id})`
            : ('Error: ' + data.message);
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
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(this.project),
        });
        const data = await res.json();
        const bar = document.getElementById('status-bar');
        if (bar) bar.textContent = data.success ? 'Guardado' : ('Error: ' + (data.message || 'desconocido'));
        if (data.success) {
            this.dirty = false;
            if (this.hud) this.hud.setDirty(false);
        }
    }

    togglePlay() {
        this.currentTime = 0;
        const scene = this.currentScene();
        if (!scene) return;
        const btn = document.getElementById('btn-play');
        const dur = scene.duration ?? 5000;
        const step = 50;
        const renderLoop = () => {
            this.currentTime += step;
            if (this.currentTime >= dur) {
                this.currentTime = 0;
                this.render();
                this.updateSceneTimeLabel();
                return;
            }
            this.render();
            this.updateSceneTimeLabel();
            btn.dataset.timer = setTimeout(renderLoop, step);
        };
        if (btn.dataset.playing === 'true') {
            clearTimeout(btn.dataset.timer);
            btn.dataset.playing = 'false';
        } else {
            btn.dataset.playing = 'true';
            renderLoop();
        }
    }
}