// @ts-check
const { defineConfig, devices } = require('@playwright/test');

// The SPA is static and runs the engine in the browser, so the tests only need a file server.
// `npm run build` at the repository root must have copied the engine into web/js/engine first.
const baseURL = process.env.DORKSMITH_BASE_URL || 'http://localhost:5080';

module.exports = defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  timeout: 30_000,
  use: {
    baseURL,
    trace: 'retain-on-failure',
    permissions: ['clipboard-read', 'clipboard-write'],
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: process.env.DORKSMITH_BASE_URL ? undefined : {
    command: 'node ../../scripts/serve.mjs --port 5080',
    url: `${baseURL}/`,
    reuseExistingServer: true,
    timeout: 30_000,
  },
});
