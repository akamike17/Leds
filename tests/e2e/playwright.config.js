// Playwright config for DSLetras E2E (spec 20.9).
// Assumes the app is running at BASE_URL (default http://127.0.0.1:5107).
//
// workers:1 es deliberado: el simulador (SimulatorTarget) es un singleton global
// en la app, y varios tests comparten su estado (escena activa). Correr en un solo
// worker evita carreras sobre /Deploy/SimulatorFrame y el estado del simulador.
import { defineConfig } from '@playwright/test';

const baseURL = process.env.BASE_URL || 'http://127.0.0.1:5107';

export default defineConfig({
  testDir: './specs',
  timeout: 30_000,
  retries: 0,
  workers: 1,
  fullyParallel: false,
  use: {
    baseURL,
    headless: true,
    viewport: { width: 1280, height: 800 },
    trace: 'retain-on-failure',
  },
  reporter: [['list']],
});