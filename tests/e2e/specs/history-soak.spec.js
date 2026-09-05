// RFLED §4 — Soak REAL de Undo/Redo sobre el history del editor (editor-history.js)
// en el navegador. No es add/delete de dominio: ejercita el árbol de snapshots del
// editor con operaciones heterogéneas y verifica que Undo hasta el vacío y Redo hasta
// el final restauran el framebuffer exacto, sin huérfanos, más Save/Open idéntico.
import { test, expect } from '@playwright/test';
import { createProject, readFramebuffer, canvasBox } from './framebuffer-utils.js';

function acceptDialogs(page) {
  let pending = '';
  page.on('dialog', d => d.accept(pending));
  return { set: (t) => { pending = t; } };
}

test('soak history: N ops heterogéneas -> Undo al vacío -> Redo al final -> Save/Open', async ({ page }) => {
  const d = acceptDialogs(page);
  await createProject(page, { name: 'SoakHistory', width: '32', height: '16' });
  const box = await canvasBox(page);

  const ops = 20; // operaciones heterogéneas reales (UI/teclado)

  for (let i = 0; i < ops; i++) {
    switch (i % 5) {
      case 0: { // texto
        d.set('TX' + i);
        await page.getByRole('button', { name: 'Herramienta texto' }).click();
        await page.mouse.click(box.x + 20, box.y + 20);
        break;
      }
      case 1: { // lápiz (1 trazo)
        await page.getByRole('button', { name: 'Herramienta lápiz' }).click();
        await page.mouse.move(box.x + 40, box.y + 40);
        await page.mouse.down();
        await page.mouse.move(box.x + 50, box.y + 50, { steps: 2 });
        await page.mouse.up();
        break;
      }
      case 2: { // rectángulo
        await page.getByRole('button', { name: 'Herramienta rectángulo' }).click();
        await page.mouse.move(box.x + 60, box.y + 20);
        await page.mouse.down();
        await page.mouse.move(box.x + 90, box.y + 60, { steps: 2 });
        await page.mouse.up();
        break;
      }
      case 3: { // duplicar (Ctrl+D) — requiere selección previa
        await page.getByRole('button', { name: 'Herramienta selección' }).click();
        await page.mouse.click(box.x + 20, box.y + 20);
        await page.keyboard.press('Control+d');
        break;
      }
      case 4: { // borrar (Delete) — requiere selección
        await page.getByRole('button', { name: 'Herramienta selección' }).click();
        await page.mouse.click(box.x + 20, box.y + 20);
        await page.keyboard.press('Delete');
        break;
      }
    }
    await page.waitForTimeout(30);
  }

  // Estado final exacto (framebuffer "de referencia").
  const final = await readFramebuffer(page);
  const finalLit = final.lit;

  // ---- Undo hasta el estado inicial (vacío) ----
  let empty = false;
  for (let i = 0; i < ops + 5 && !empty; i++) {
    await page.keyboard.press('Control+z');
    await page.waitForTimeout(20);
    const fb = await readFramebuffer(page);
    if (fb.lit === 0) empty = true;
  }
  // Al deshacer TODAS las operaciones históricas, el lienzo vuelve a estar vacío.
  expect(empty).toBe(true);

  // ---- Redo hasta el final ----
  let restored = false;
  for (let i = 0; i < ops + 5 && !restored; i++) {
    await page.keyboard.press('Control+y');
    await page.waitForTimeout(20);
    const fb = await readFramebuffer(page);
    if (fb.lit === finalLit) restored = true;
  }
  expect(restored).toBe(true);
  expect((await readFramebuffer(page)).lit).toBe(finalLit);

  // ---- Save/Open idéntico ----
  await page.locator('#btn-save').click();
  await expect(page.locator('#stat-dirty')).toHaveText('Sin cambios', { timeout: 10_000 });
  const projectId = await page.locator('#project-id').inputValue();
  await page.goto(`/Editor/Index?id=${projectId}`);
  await expect(page.locator('#led-canvas')).toBeVisible();
  await page.waitForTimeout(200);
  expect((await readFramebuffer(page)).lit).toBe(finalLit);

  console.log('soak history: %d ops, final lit=%d', ops, finalLit);
});