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
        window.addEventListener('online', () => this.refreshTargetNet());
        window.addEventListener('offline', () => this.setNet(false));
        // navigator.onLine solo describe la red del PC; no prueba que exista
        // un ESP32. El estado visible se establece al descubrir targets reales.
        this.setNet(false);

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

        // Navegación interna (spec 19): al hacer clic en un enlace del nav o en
        // `#btn-open` con cambios sin guardar, se ofrece Guardar/Descartar/Cancelar
        // (no sólo el beforeunload genérico del navegador).
        document.querySelectorAll('.navbar a[href]').forEach(a => {
            a.addEventListener('click', (e) => {
                const href = a.getAttribute('href');
                if (!href || href === '#') return;
                if (!this.state.dirty) return;   // sin cambios: navegación normal
                e.preventDefault();
                this.confirmNavigation(href);
            });
        });
        // `#btn-open` abre /Projects; con cambios sin guardar pide confirmación.
        const btnOpen = document.getElementById('btn-open');
        if (btnOpen) btnOpen.addEventListener('click', (e) => {
            e.preventDefault();
            if (this.state.dirty) this.confirmNavigation('/Projects');
            else window.location.href = '/Projects';
        });

        this.bindUnsavedModal();

        this.discover();
    }

    // Diálogo interno Guardar/Descartar/Cancelar para navegación controlada o
    // acción diferida (p.ej. abrir el modal de Nuevo proyecto). `after` es un href
    // (string → navegar) o una función a ejecutar tras guardar/descartar.
    confirmNavigation(after) {
        this._pendingAction = after;
        const modal = document.getElementById('unsaved-modal');
        if (modal && window.bootstrap) bootstrap.Modal.getOrCreateInstance(modal).show();
    }

    bindUnsavedModal() {
        const modalEl = document.getElementById('unsaved-modal');
        if (!modalEl) return;
        const close = () => { if (window.bootstrap) bootstrap.Modal.getOrCreateInstance(modalEl).hide(); };
        const run = (after) => {
            if (!after) return;
            if (typeof after === 'function') after();
            else window.location.href = after;
        };
        document.getElementById('unsaved-cancel').addEventListener('click', () => { this._pendingAction = null; close(); });
        document.getElementById('unsaved-discard').addEventListener('click', () => {
            const after = this._pendingAction; this._pendingAction = null;
            this.state.dirty = false;   // descartar = perder cambios intencionadamente
            close();
            run(after);
        });
        document.getElementById('unsaved-save').addEventListener('click', async () => {
            const after = this._pendingAction;
            // guardar primero; si falla, no navegar/actuar
            await this.state.save();
            if (this.state.dirty) { this._pendingAction = null; close(); return; }
            this._pendingAction = null;
            close();
            run(after);
        });
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
        this.refreshTargetNet();
    }

    refreshTargetNet() {
        this.setNet(Boolean(this.activeTarget && this.activeTarget.transport !== 'simulator' && this.activeTarget.online));
    }

    setNet(online) {
        const dot = document.getElementById('net-dot');
        const label = document.getElementById('net-label');
        if (dot) dot.className = 'net-dot ' + (online ? 'bg-success' : 'bg-danger');
        if (label) label.textContent = online ? 'ESP32 conectado' : 'Sin dispositivo';
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

        // NUNCA enviar una copia vieja de disco: primero se guarda el estado actual
        // (canvas B nunca puede quedar desincronizado del device que recibe A).
        if (this.state.dirty) {
            this.setSend('Guardando antes de enviar…');
            const saveRes = await fetch('/Projects/Save', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': window.__antiforgery?.token || '',
                },
                body: JSON.stringify(this.state.projectForWire()),
            });
            const saveData = await saveRes.json();
            if (!saveData.success) {
                this.setSend('Envío cancelado: ' + (saveData.message || 'no se pudo guardar'));
                return;
            }
            this.state.dirty = false;
            this.state.hud.setDirty(false);
        }
        this.setSend('Enviando…');

        // Escena actualmente seleccionada (no siempre la primera).
        const sceneSel = document.getElementById('scene-select');
        const sceneIndex = sceneSel ? parseInt(sceneSel.value, 10) || 0 : 0;

        try {
            const res = await fetch('/Deploy/Send', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': window.__antiforgery?.token || '',
                },
                body: JSON.stringify({ projectId, targetId, sceneIndex }),
            });
            const data = await res.json();
            this.setSend(data.success
                ? `Enviado (${data.phase}, escena ${(data.sceneIndex ?? sceneIndex) + 1})`
                : `Envío fallido: ${data.message || 'desconocido'}`);
        } catch (e) {
            // Offline/retry: conserva el trabajo, sin corromper nada.
            this.setSend('Envío fallido (sin conexión). Reintenta.');
        }
    }
}
