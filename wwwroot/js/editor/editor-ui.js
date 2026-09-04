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
        else if (key === 'color') { this.writeColor(obj, 'color', el.value); }
        else if (key === 'strokeColor') { obj.strokeColor = this.hexRgb(el.value); }
        else if (key === 'fillColor') { obj.fillColor = this.hexRgb(el.value); }
        else if (key === 'visible') { obj.visible = el.checked; }
        else if (key === 'locked') { obj.locked = el.checked; }
        else if (key === 'brightness') { obj.brightness = Math.max(0, Math.min(255, parseInt(el.value, 10) || 255)); }
        else if (key === 'timingStart') { obj.timing = obj.timing || {}; obj.timing.start = parseInt(el.value, 10) || 0; }
        else if (key === 'timingEnd') { obj.timing = obj.timing || {}; obj.timing.end = parseInt(el.value, 10) || 0; }

        this.state.markDirty();
        this.state.render();
    }

    // Precarga el valor actual del objeto en el control (no lo destruye).
    setValue(el, obj, key) {
        if (key === 'name') { el.value = obj.name ?? ''; }
        else if (key === 'x') { el.value = obj.position?.x ?? ''; }
        else if (key === 'y') { el.value = obj.position?.y ?? ''; }
        else if (key === 'width') { el.value = obj.size?.width ?? 1; }
        else if (key === 'height') { el.value = obj.size?.height ?? 1; }
        else if (key === 'text') { el.value = obj.text ?? ''; }
        else if (key === 'color' || key === 'fillColor' || key === 'strokeColor') {
            const c = this.readColor(obj, key);
            el.value = this.rgbHex(c);
        }
        else if (key === 'visible') { el.checked = obj.visible !== false; }
        else if (key === 'locked') { el.checked = obj.locked === true; }
        else if (key === 'brightness') { el.value = obj.brightness ?? 255; }
        else if (key === 'timingStart') { el.value = obj.timing?.start ?? 0; }
        else if (key === 'timingEnd') { el.value = obj.timing?.end ?? 0; }
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

        return fields;
    }

    colorField(field, label) {
        return {
            html: `<label class="form-label small mb-0">${label}</label>
                   <input class="form-control form-control-sm" type="color" data-field="${field}">`,
        };
    }
}