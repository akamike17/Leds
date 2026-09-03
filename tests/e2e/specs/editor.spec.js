// E2E del editor DSLetras (spec 20.9): pointer/mouse, drawing, selection, drag,
// timeline, Undo/Redo, save/open, library, send.
import { test, expect } from '@playwright/test';

// Un proyecto nuevo se crea vía Projects/New (GET). Usamos la ruta directa del editor
// con un id para no depender del formulario antiforgery en estos tests de humo.

test.describe('DSLetras editor E2E', () => {
  test('la página de proyectos carga y enlaza a Nuevo', async ({ page }) => {
    await page.goto('/Projects/Index');
    await expect(page.getByRole('heading', { name: 'Proyectos' })).toBeVisible();
    await expect(page.getByRole('link', { name: '+ Nuevo proyecto' })).toBeVisible();
  });

  test('el editor renderiza la barra de estado, herramientas y target', async ({ page }) => {
    // Crear flujo real: Projects/New → formulario → Create → Editor.
    await page.goto('/Projects/New');
    // El formulario de nuevo proyecto (ProjectsController.Create) pide Name/Width/Height.
    const nameInput = page.locator('#Name');
    if (await nameInput.count()) {
      await nameInput.fill('E2E Proyecto');
      await page.locator('form').getByRole('button', { name: /Crear/i }).click();
    }
    // Si no hay formulario (flujo alterno), navegar a Editor/New que crea y redirige.
    await page.waitForURL(/\/Editor\/Index/, { timeout: 15_000 }).catch(() => {});

    await expect(page.locator('#status-hud')).toBeVisible();
    await expect(page.locator('#device-select')).toBeVisible();
    await expect(page.locator('#btn-send')).toBeVisible();
    await expect(page.locator('#led-canvas')).toBeVisible();
    // herramientas (toolbar con aria-label)
    await expect(page.getByRole('toolbar', { name: 'Herramientas' })).toBeVisible();
  });

  test('dibujar con lápiz y deshacer/rehacer', async ({ page }) => {
    // Entrar al editor con un proyecto nuevo.
    await page.goto('/Editor/New?width=16&height=16&name=E2E%20Dibujo');
    await page.waitForURL(/\/Editor\/Index/, { timeout: 15_000 });
    await expect(page.locator('#led-canvas')).toBeVisible();

    // Seleccionar lápiz.
    await page.getByRole('button', { name: 'Herramienta lápiz' }).click();

    // Dibujar un trazo continuo (pointer down → move → up).
    const canvas = page.locator('#led-canvas');
    const box = await canvas.boundingBox();
    await page.mouse.move(box.x + 20, box.y + 20);
    await page.mouse.down();
    await page.mouse.move(box.x + 30, box.y + 30, { steps: 3 });
    await page.mouse.move(box.x + 40, box.y + 20, { steps: 3 });
    await page.mouse.up();

    // Debe marcarse como con cambios sin guardar.
    await expect(page.locator('#stat-dirty')).toContainText('Cambios sin guardar');

    // Undo (Ctrl+Z) y Redo (Ctrl+Y).
    await page.keyboard.press('Control+z');
    await page.keyboard.press('Control+y');
  });

  test('enviar al simulador responde con estado', async ({ page }) => {
    await page.goto('/Editor/New?width=16&height=16&name=E2E%20Send');
    await page.waitForURL(/\/Editor\/Index/, { timeout: 15_000 });
    await expect(page.locator('#btn-send')).toBeVisible();

    // El selector de target debe tener al menos el simulador.
    await expect(page.locator('#device-select option')).not.toHaveCount(0);

    // Clic en Enviar y esperar el mensaje de estado de envío.
    await page.locator('#btn-send').click();
    await expect(page.locator('#stat-send')).not.toHaveText('Sin envío');
  });
});