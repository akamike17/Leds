// Inspector contextual (derecha): edita las propiedades del objeto seleccionado.
// Carga los valores existentes (no los destruye) y cada edición actualiza el
// objeto → render inmediato → markDirty → Undo/Redo → Save/Open.
export class Inspector {
    constructor(state) {
        this.state = state;
        this._renderedKey = null;
    }

    render() {
        const selected = this.state.selection.list();
        const empty = document.getElementById('inspector-empty');
        const content = document.getElementById('inspector-content');

        if (selected.length === 0) {
            this._renderedKey = null;
            empty.classList.remove('d-none');
            content.classList.add('d-none');
            content.innerHTML = '';
            return;
        }
        if (selected.length > 1) {
            empty.classList.add('d-none');
            content.classList.remove('d-none');
            content.innerHTML = `<p class="small text-muted">${selected.length} objetos seleccionados</p>`;
            this._renderedKey = 'multi';
            return;
        }

        const obj = selected[0];
        // Re-renderiza sólo cuando cambia el objeto seleccionado; evita reconstruir
        // los inputs a cada frame (perdería el foco mientras el usuario escribe).
        const key = obj.id;
        if (key === this._renderedKey) return;
        this._renderedKey = key;

        empty.classList.add('d-none');
        content.classList.remove('d-none');
        this.renderFields(obj, content);
    }

    renderFields(obj, content) {
        content.innerHTML = '';
        const fields = this.fieldsFor(obj);
        for (const f of fields) {
            const wrap = document.createElement('div');
            wrap.className = 'mb-2';
            wrap.innerHTML = f.html;
            content.appendChild(wrap);
            for (const el of wrap.querySelectorAll('input, textarea, select')) {
                const key = el.dataset.field;
                this.setValue(el, obj, key);
                el.addEventListener('input', () => this.apply(obj, key, el, content));
                el.addEventListener('change', () => this.apply(obj, key, el, content));
            }
        }
    }

    // Lee el valor del control y lo escribe en el objeto sin destruir el resto.
    apply(obj, key, el, content) {
        if (key === 'name') { obj.name = el.value; }
        else if (key === 'x') { obj.position = obj.position || {}; obj.position.x = parseInt(el.value, 10) || 0; }
        else if (key === 'y') { obj.position = obj.position || {}; obj.position.y = parseInt(el.value, 10) || 0; }
        else if (key === 'width') { obj.size = obj.size || {}; obj.size.width = Math.max(1, parseInt(el.value, 10) || 1); }
        else if (key === 'height') { obj.size = obj.size || {}; obj.size.height = Math.max(1, parseInt(el.value, 10) || 1); }
        else if (key === 'text') {
            obj.text = el.value;
            // re-sincroniza el tamaño (ancho aprox 6px/char —1, alto 7).
            obj.size = obj.size || {};
            obj.size.width = Math.max(1, (el.value || '').length * 6 - 1);
            obj.size.height = 7;
        }
        else if (key === 'font') {
            // Fuente: 5x7 (6px/char, alto 7) o 3x5 (4px/char, alto 5).
            obj.fontId = el.value || '5x7';
            obj.size = obj.size || {};
            const advance = obj.fontId === '3x5' ? 4 : 6;
            const height = obj.fontId === '3x5' ? 5 : 7;
            obj.size.width = Math.max(1, (obj.text || '').length * advance - 1);
            obj.size.height = height;
        }
        else if (key === 'color') { this.writeColor(obj, 'color', el.value); }
        else if (key === 'strokeColor') { obj.strokeColor = this.hexRgb(el.value); }
        else if (key === 'fillColor') { obj.fillColor = this.hexRgb(el.value); }
        else if (key === 'visible') { obj.visible = el.checked; }
        else if (key === 'locked') { obj.locked = el.checked; }
        else if (key === 'brightness') { obj.brightness = Math.max(0, Math.min(255, parseInt(el.value, 10) || 255)); }
        else if (key === 'timingStart') { obj.timing = obj.timing || {}; obj.timing.start = parseInt(el.value, 10) || 0; }
        else if (key === 'timingEnd') { obj.timing = obj.timing || {}; obj.timing.end = parseInt(el.value, 10) || 0; }
        else if (key === 'animKind' || key === 'animSpeed' || key === 'animDir' || key === 'animSlot') {
            const a = this.ensureAnim(obj);
            const v = parseInt(el.value, 10) || 0;
            if (key === 'animKind') a.kind = v;
            else if (key === 'animSpeed') a.speedPreset = v;
            else if (key === 'animDir') a.direction = v;
            else if (key === 'animSlot') a.slot = v;
        }
        else if (key === 'animLoop') { this.ensureAnim(obj).loop = el.checked; }

        this.state.markDirty();
        this.state.render();
    }

    // Garantiza que el objeto tenga al menos una animación y devuelve la primera.
    ensureAnim(obj) {
        if (!obj.animations || obj.animations.length === 0) {
            obj.animations = [{ kind: 0, speedPreset: 1, direction: 0, loop: false, slot: 1 }];
        }
        return obj.animations[0];
    }

    // Precarga el valor actual del objeto en el control (no lo destruye).
    setValue(el, obj, key) {
        if (key === 'name') { el.value = obj.name ?? ''; }
        else if (key === 'x') { el.value = obj.position?.x ?? ''; }
        else if (key === 'y') { el.value = obj.position?.y ?? ''; }
        else if (key === 'width') { el.value = obj.size?.width ?? 1; }
        else if (key === 'height') { el.value = obj.size?.height ?? 1; }
        else if (key === 'text') { el.value = obj.text ?? ''; }
        else if (key === 'font') { el.value = obj.fontId ?? '5x7'; }
        else if (key === 'color' || key === 'fillColor' || key === 'strokeColor') {
            const c = this.readColor(obj, key);
            el.value = this.rgbHex(c);
        }
        else if (key === 'visible') { el.checked = obj.visible !== false; }
        else if (key === 'locked') { el.checked = obj.locked === true; }
        else if (key === 'brightness') { el.value = obj.brightness ?? 255; }
        else if (key === 'timingStart') { el.value = obj.timing?.start ?? 0; }
        else if (key === 'timingEnd') { el.value = obj.timing?.end ?? 0; }
        else if (key === 'animKind' || key === 'animSpeed' || key === 'animDir' || key === 'animSlot') {
            const a = obj.animations && obj.animations[0];
            if (key === 'animKind') el.value = a?.kind ?? 0;
            else if (key === 'animSpeed') el.value = a?.speedPreset ?? 1;
            else if (key === 'animDir') el.value = a?.direction ?? 0;
            else if (key === 'animSlot') el.value = a?.slot ?? 1;
        }
        else if (key === 'animLoop') { el.checked = !!(obj.animations && obj.animations[0] && obj.animations[0].loop); }
    }

    rgbHex(c) {
        const h = n => Math.max(0, Math.min(255, n)).toString(16).padStart(2, '0');
        return `#${h(c.r ?? 0)}${h(c.g ?? 0)}${h(c.b ?? 0)}`;
    }

    hexRgb(hex) {
        const m = /^#?([0-9a-f]{6})$/i.exec(hex || '');
        if (!m) return { r: 255, g: 255, b: 255 };
        const v = parseInt(m[1], 16);
        return { r: (v >> 16) & 0xff, g: (v >> 8) & 0xff, b: v & 0xff };
    }

    // Lectura/escritura de color coherente con el modelo: text/shape usan .color o
    // .strokeColor/.fillColor; drawing usa palette[0] (monocromo indexado).
    readColor(obj, key) {
        if (key === 'color' && obj.kind === 'drawing') {
            const p = (obj.palette && obj.palette[0]) || { r: 255, g: 255, b: 255 };
            return p;
        }
        return obj[key] ?? { r: 255, g: 255, b: 255 };
    }

    writeColor(obj, key, hexValue) {
        const c = this.hexRgb(hexValue);
        if (key === 'color' && obj.kind === 'drawing') {
            if (!obj.palette || obj.palette.length === 0) obj.palette = [c];
            else obj.palette[0] = c;
            return;
        }
        obj[key] = c;
    }

    fieldsFor(obj) {
        const num = (label, field) => ({
            html: `<label class="form-label small mb-0">${label}</label>
                   <input class="form-control form-control-sm" type="number" data-field="${field}">`,
        });

        const fields = [
            { html: `<label class="form-label small mb-0">Nombre</label>
                     <input class="form-control form-control-sm" data-field="name">` },
            num('X', 'x'),
            num('Y', 'y'),
        ];

        // width/height para tipos con tamaño editable (no texto, cuyo tamaño deriva del texto).
        if (obj.kind !== 'text') {
            fields.push(num('Ancho', 'width'));
            fields.push(num('Alto', 'height'));
        }

        if (obj.kind === 'text') {
            fields.push({ html: `<label class="form-label small mb-0">Texto</label>
                     <textarea class="form-control form-control-sm" rows="2" data-field="text"></textarea>` });
            fields.push({
                html: `<label class="form-label small mb-0">Fuente</label>
                       <select class="form-select form-select-sm" data-field="font">
                         <option value="5x7">5x7 (estándar)</option>
                         <option value="3x5">3x5 (compacta)</option>
                       </select>`,
            });
            fields.push(this.colorField('color', 'Color', true));
        } else if (obj.kind === 'shape') {
            fields.push(this.colorField('strokeColor', 'Borde', false));
            fields.push(this.colorField('fillColor', 'Relleno', false));
        } else if (obj.kind === 'drawing') {
            fields.push(this.colorField('color', 'Color', true));
        }

        // brillo
        fields.push(num('Brillo (0–255)', 'brightness'));

        // timing
        fields.push(num('Timing inicio (ms)', 'timingStart'));
        fields.push(num('Timing fin (ms)', 'timingEnd'));

        // visibilidad / bloqueo
        fields.push({
            html: `<div class="form-check form-switch mb-1">
                     <input class="form-check-input" type="checkbox" data-field="visible">
                     <label class="form-check-label">Visible</label>
                   </div>
                   <div class="form-check form-switch">
                     <input class="form-check-input" type="checkbox" data-field="locked">
                     <label class="form-check-label">Bloqueado</label>
                   </div>`,
        });

        // Animación (spec 6): tipo, velocidad, dirección, loop y slot (valores numéricos = enums C#).
        const anim = (obj.animations && obj.animations[0]) || null;
        fields.push({
            html: `<hr class="my-2">
                   <label class="form-label small mb-0 fw-bold">Animación</label>
                   <div class="row g-1">
                     <div class="col-6">
                       <label class="form-label small mb-0">Tipo</label>
                       <select class="form-select form-select-sm" data-field="animKind">
                         <option value="0">Fixed</option><option value="1">Blink</option>
                         <option value="2">Marquee</option><option value="3">Slide</option>
                         <option value="4">Pulse</option><option value="5">Wipe</option>
                         <option value="6">Frame</option>
                       </select>
                     </div>
                     <div class="col-6">
                       <label class="form-label small mb-0">Velocidad</label>
                       <select class="form-select form-select-sm" data-field="animSpeed">
                         <option value="0">Slow</option><option value="1">Normal</option>
                         <option value="2">Fast</option>
                       </select>
                     </div>
                     <div class="col-6">
                       <label class="form-label small mb-0">Dirección</label>
                       <select class="form-select form-select-sm" data-field="animDir">
                         <option value="0">Left</option><option value="1">Right</option>
                         <option value="2">Up</option><option value="3">Down</option>
                       </select>
                     </div>
                     <div class="col-6">
                       <label class="form-label small mb-0">Slot</label>
                       <select class="form-select form-select-sm" data-field="animSlot">
                         <option value="0">Entrance</option><option value="1">Main</option>
                         <option value="2">Exit</option>
                       </select>
                     </div>
                   </div>
                   <div class="form-check form-switch mt-1">
                     <input class="form-check-input" type="checkbox" data-field="animLoop">
                     <label class="form-check-label">Loop</label>
                   </div>`,
        });

        return fields;
    }

    colorField(field, label) {
        return {
            html: `<label class="form-label small mb-0">${label}</label>
                   <input class="form-control form-control-sm" type="color" data-field="${field}">`,
        };
    }
}