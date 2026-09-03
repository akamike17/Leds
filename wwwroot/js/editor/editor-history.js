// Historial de operaciones (Undo/Redo). Cada snapshot captura el árbol de proyecto.
// Drag/sliders = UNA operación histórica (spec sección 12); una sesión de dibujo = un Undo.
export class History {
    constructor(limit = 100) {
        this.undoStack = [];
        this.redoStack = [];
        this.limit = limit;
        this.coalescing = false;
    }

    // Captura el estado ANTES de una operación atómica.
    snapshot(state) {
        this.undoStack.push(JSON.stringify(state));
        if (this.undoStack.length > this.limit) this.undoStack.shift();
        this.redoStack = [];
    }

    begin() { this.coalescing = true; this._pending = null; }
    end() { this.coalescing = false; this._pending = null; }

    // Para drag: solo la marca final cuenta.
    captureOnce(state) {
        if (!this._pending) {
            this._pending = JSON.stringify(state);
        }
    }
    commitPending() {
        if (this._pending) {
            this.undoStack.push(this._pending);
            if (this.undoStack.length > this.limit) this.undoStack.shift();
            this.redoStack = [];
            this._pending = null;
        }
    }

    undo(state) {
        if (this.undoStack.length === 0) return null;
        const cur = JSON.stringify(state);
        const prev = this.undoStack.pop();
        this.redoStack.push(cur);
        return JSON.parse(prev);
    }

    redo(state) {
        if (this.redoStack.length === 0) return null;
        const cur = JSON.stringify(state);
        const next = this.redoStack.pop();
        this.undoStack.push(cur);
        return JSON.parse(next);
    }

    get canUndo() { return this.undoStack.length > 0; }
    get canRedo() { return this.redoStack.length > 0; }
}