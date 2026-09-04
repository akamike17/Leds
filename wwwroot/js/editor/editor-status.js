// Barra de estado consolidada (spec 19): cambios sin guardar, escena/tiempo,
// selección, target, online/offline y estado de envío. Además gestiona el
// guardado ante cierre (Guardar/Descartar/Cancelar vía beforeunload) y la red.

export class StatusHud {
    constructor(state) {
        this.state = state;
        this.targets = [];
        this.activeTarget = null;
    }

    bind() {
        // Red: indicador online/offline (offline → retry sin perder trabajo).
        window.addEventListener('online', () => this.setNet(true));
        window.addEventListener('offline', () => this.setNet(false));
        this.setNet(navigator.onLine);

        // Cerrar modificado → aviso nativo (Guardar/Descartar/Cancelar).
        window.addEventListener('beforeunload', (e) => {
            if (this.state.dirty) {
                e.preventDefault();
                e.returnValue = '';
            }
        });

        // Enlace al botón de envío.
        const btnSend = document.getElementById('btn-send');
        if (btnSend) btnSend.addEventListener('click', () => this.send());

        this.discover();
    }

    async discover() {
        try {
            const res = await fetch('/Deploy/Discover');
            const data = await res.json();
            this.targets = data.targets || [];
            this.populateTargetSelect();
        } catch {
            this.targets = [];
            this.populateTargetSelect();
        }
    }

    populateTargetSelect() {
        const sel = document.getElementById('device-select');
        if (!sel) return;
        sel.innerHTML = '';
        if (this.targets.length === 0) {
            const opt = document.createElement('option');
            opt.value = '';
            opt.textContent = 'Sin dispositivos';
            sel.appendChild(opt);
            this.setTarget(null);
            return;
        }
        this.targets.forEach(t => {
            const opt = document.createElement('option');
            opt.value = t.serial || t.id;
            opt.textContent = `${t.name} (${t.transport})`;
            sel.appendChild(opt);
        });
        sel.addEventListener('change', () => {
            const chosen = this.targets.find(t => (t.serial || t.id) === sel.value);
            this.setTarget(chosen || null);
        });
        this.setTarget(this.targets[0]);
    }

    setTarget(t) {
        this.activeTarget = t;
        const el = document.getElementById('stat-target');
        if (el) el.textContent = t ? `Target: ${t.name}` : 'Target: —';
    }

    setNet(online) {
        const dot = document.getElementById('net-dot');
        const label = document.getElementById('net-label');
        if (dot) dot.className = 'net-dot ' + (online ? 'bg-success' : 'bg-danger');
        if (label) label.textContent = online ? 'Conectado' : 'Sin conexión';
    }

    setDirty(dirty) {
        const el = document.getElementById('stat-dirty');
        if (el) el.textContent = dirty ? '● Cambios sin guardar' : 'Sin cambios';
    }

    setSelection(count) {
        const el = document.getElementById('stat-selection');
        if (el) el.textContent = `${count} seleccionado${count === 1 ? '' : 's'}`;
    }

    setSend(message) {
        const el = document.getElementById('stat-send');
        if (el) el.textContent = message;
    }

    // Notificación consolidada (success/warning/error) en un único mecanismo UI.
    notify(kind, message) {
        const el = document.getElementById('stat-notify');
        if (!el) return;
        el.textContent = message;
        el.className = 'small ' + (
            kind === 'error' ? 'text-danger' :
            kind === 'warning' ? 'text-warning' : 'text-success');
        // auto-ocultar tras unos segundos (sin borrar dirty/selection/send).
        if (this._notifyTimer) clearTimeout(this._notifyTimer);
        this._notifyTimer = setTimeout(() => { el.textContent = ''; }, 4000);
    }

    async send() {
        const projectId = document.getElementById('project-id')?.value;
        const targetId = this.activeTarget ? (this.activeTarget.serial || this.activeTarget.id) : null;
        this.setSend('Enviando…');

        try {
            const res = await fetch('/Deploy/Send', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': window.__antiforgery?.token || '',
                },
                body: JSON.stringify({ projectId, targetId }),
            });
            const data = await res.json();
            this.setSend(data.success
                ? `Enviado (${data.phase})`
                : `Envío fallido: ${data.message || 'desconocido'}`);
        } catch (e) {
            // Offline/retry: conserva el trabajo, sin corromper nada.
            this.setSend('Envío fallido (sin conexión). Reintenta.');
        }
    }
}