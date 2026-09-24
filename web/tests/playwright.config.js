// @ts-check
const { defineConfig, devices } = require('@playwright/test');

// The API serves the SPA itself in development (SERVE_STATIC=true), so one process is enough.
// `dotnet run` honours src/Dorksmith.Api/Properties/launchSettings.json → http://localhost:5080.
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
    command: 'dotnet run --project ../../src/Dorksmith.Api',
    url: `${baseURL}/health/ready`,
    reuseExistingServer: true,
    timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      RATE_LIMIT_PERMIT_LIMIT: '10000',
      SQLITE_PATH: ':memory:',
      IP_HMAC_SECRET: 'playwright-only-secret-not-for-production',
    },
  },
});
