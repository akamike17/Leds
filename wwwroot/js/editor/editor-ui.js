// Inspector contextual (derecha): edita las propiedades del objeto seleccionado.
export class Inspector {
    constructor(state) {
        this.state = state;
    }

    render() {
        const selected = this.state.selection.list();
        const empty = document.getElementById('inspector-empty');
        const content = document.getElementById('inspector-content');
        if (selected.length === 0) {
            empty.classList.remove('d-none');
            content.classList.add('d-none');
            return;
        }
        empty.classList.add('d-none');
        content.classList.remove('d-none');

        const obj = selected[0];
        content.innerHTML = '';

        if (selected.length > 1) {
            content.innerHTML = `<p class="small text-muted">${selected.length} objetos seleccionados</p>`;
            return;
        }

        const fields = this.fieldsFor(obj);
        for (const f of fields) {
            const wrap = document.createElement('div');
            wrap.className = 'mb-2';
            wrap.innerHTML = f.html;
            content.appendChild(wrap);
            for (const binding of f.bindings) {
                const el = wrap.querySelector(binding.selector);
                el.addEventListener(binding.event, () => {
                    binding.apply(obj, el);
                    this.state.markDirty();
                    this.state.render();
                });
            }
        }
    }

    fieldsFor(obj) {
        const fields = [
            {
                html: `<label class="form-label small mb-0">Nombre</label>
                       <input class="form-control form-control-sm" data-name>`,
                bindings: [{ selector: '[data-name]', event: 'input',
                    apply: (o, el) => { o.name = el.value; } }],
            },
            {
                html: `<label class="form-label small mb-0">X</label>
                       <input class="form-control form-control-sm" type="number" data-x>`,
                bindings: [{ selector: '[data-x]', event: 'input',
                    apply: (o, el) => { o.position.x = parseInt(el.value, 10) || 0; } }],
            },
            {
                html: `<label class="form-label small mb-0">Y</label>
                       <input class="form-control form-control-sm" type="number" data-y>`,
                bindings: [{ selector: '[data-y]', event: 'input',
                    apply: (o, el) => { o.position.y = parseInt(el.value, 10) || 0; } }],
            },
        ];

        const extras = [];

        if (obj.kind === 'text') {
            extras.push({
                html: `<label class="form-label small mb-0">Texto</label>
                       <textarea class="form-control form-control-sm" data-text></textarea>`,
                bindings: [{ selector: '[data-text]', event: 'input',
                    apply: (o, el) => { o.text = el.value; } }],
            });
        }

        // visibilidad
        extras.push({
            html: `<div class="form-check form-switch">
                     <input class="form-check-input" type="checkbox" data-visible>
                     <label class="form-check-label">Visible</label>
                   </div>`,
            bindings: [{ selector: '[data-visible]', event: 'change',
                apply: (o, el) => { o.visible = el.checked; } }],
        });

        return fields.concat(extras);
    }

    populate(fields, obj) {
        return fields;
    }

    bindValues(obj, content) {
        content.querySelector('[data-name]').value = obj.name || '';
        content.querySelector('[data-x]').value = obj.position?.x ?? 0;
        content.querySelector('[data-y]').value = obj.position?.y ?? 0;
        if (obj.kind === 'text') content.querySelector('[data-text]').value = obj.text || '';
        const vis = content.querySelector('[data-visible]');
        if (vis) vis.checked = obj.visible !== false;
    }
}