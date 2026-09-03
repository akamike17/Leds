// Playwright config for DSLetras E2E (spec 20.9).
// Assumes the app is running at BASE_URL (default http://127.0.0.1:5107).
import { defineConfig } from '@playwright/test';

const baseURL = process.env.BASE_URL || 'http://127.0.0.1:5107';

export default defineConfig({
  testDir: './specs',
  timeout: 30_000,
  retries: 0,
  use: {
    baseURL,
    headless: true,
    viewport: { width: 1280, height: 800 },
    trace: 'retain-on-failure',
  },
  reporter: [['list']],
});