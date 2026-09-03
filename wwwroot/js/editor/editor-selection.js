// Selección y manipulación de objetos en el editor.
export class Selection {
    constructor(state) {
        this.state = state;
        this.selected = new Set();   // ObjectId (string)
        this.dragStart = null;
        this.dragOrigin = null;
    }

    deselectAll() { this.selected.clear(); }

    // click simple → selección única; ctrl/meta → toggle; shift → add.
    clickOn(obj, opts = {}) {
        if (opts.ctrl || opts.meta) {
            if (this.selected.has(obj.id)) this.selected.delete(obj.id);
            else this.selected.add(obj.id);
        } else if (opts.shift) {
            this.selected.add(obj.id);
        } else {
            this.selected.clear();
            this.selected.add(obj.id);
        }
    }

    clearIfEmpty(ev) {
        if (ev.target === ev.currentTarget) this.selected.clear();
    }

    list() {
        const result = [];
        for (const scene of this.state.project.scenes || [])
            for (const layer of scene.layers || [])
                for (const obj of layer.objects || [])
                    if (this.selected.has(obj.id)) result.push(obj);
        return result;
    }

    // Devuelve el objeto en la coordenada lógica (el de mayor order/más tarde).
    hitTest(px, py, scene) {
        let found = null;
        const layers = [...(scene.layers || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
        for (const layer of layers) {
            for (const obj of layer.objects || []) {
                const x = obj.position?.x ?? 0, y = obj.position?.y ?? 0;
                const w = obj.size?.width ?? this.guessSize(obj).w;
                const h = obj.size?.height ?? this.guessSize(obj).h;
                if (px >= x && px < x + Math.max(w, 1) && py >= y && py < y + Math.max(h, 1))
                    found = obj;
            }
        }
        return found;
    }

    guessSize(obj) {
        if (obj.kind === 'text') {
            // ancho aproximado con fuente 5x7
            const len = (obj.text || '').length;
            return { w: Math.max(len * 6 - 1, 1), h: 7 };
        }
        return { w: obj.size?.width || 1, h: obj.size?.height || 1 };
    }

    // Inicia un drag sobre los objetos seleccionados.
    beginDrag(logical) {
        this.dragStart = logical;
        this.dragOrigin = this.list().map(o => ({ id: o.id, x: o.position?.x ?? 0, y: o.position?.y ?? 0 }));
    }

    dragTo(logical) {
        if (!this.dragStart || !this.dragOrigin) return;
        const dx = logical.x - this.dragStart.x;
        const dy = logical.y - this.dragStart.y;
        const byId = Object.fromEntries(this.dragOrigin.map(o => [o.id, o]));
        for (const obj of this.list()) {
            const orig = byId[obj.id];
            if (!orig) continue;
            obj.position = { x: orig.x + dx, y: orig.y + dy };
        }
    }

    endDrag() {
        this.dragStart = null;
        this.dragOrigin = null;
    }
}