// Herramientas: conversión pointer→coordenada lógica LED y operaciones de dibujo.
export class Tools {
    constructor(state) {
        this.state = state;
        this.activeTool = 'select';
        this.rectSelect = null;       // {x0,y0,x1,y1} lógico
    }

    setTool(name) { this.activeTool = name; }

    // pointer event → coordenada lógica LED (entera, origen arriba-izq)
    toLogical(clientX, clientY) {
        const rect = this.state.canvas.getBoundingClientRect();
        const sx = this.state.canvas.width / rect.width;
        const sy = this.state.canvas.height / rect.height;
        const x = Math.floor((clientX - rect.left) * sx);
        const y = Math.floor((clientY - rect.top) * sy);
        return { x: Math.max(0, Math.min(this.state.canvas.width - 1, x)),
                 y: Math.max(0, Math.min(this.state.canvas.height - 1, y)) };
    }

    // Inicia rectángulo de selección
    beginRectSelect(logical) { this.rectSelect = { x0: logical.x, y0: logical.y, x1: logical.x, y1: logical.y }; }
    updateRectSelect(logical) {
        if (this.rectSelect) { this.rectSelect.x1 = logical.x; this.rectSelect.y1 = logical.y; }
    }
    endRectSelect() {
        if (!this.rectSelect) return { x0: 0, y0: 0, x1: 0, y1: 0 };
        const r = this.rectSelect;
        this.rectSelect = null;
        return {
            x0: Math.min(r.x0, r.x1), y0: Math.min(r.y0, r.y1),
            x1: Math.max(r.x0, r.x1), y1: Math.max(r.y0, r.y1),
        };
    }

    // Dibujo de lápiz: devuelve el PixelData (monocromo 1bpp)
    static newDrawing(w, h) {
        return { width: w, height: h, data: new Uint8Array(w * h) };
    }
}