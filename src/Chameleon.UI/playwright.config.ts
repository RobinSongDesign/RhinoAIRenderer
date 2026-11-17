import { defineConfig, devices } from '@playwright/test';

const port = 5199;
const token = 'e2e';

/**
 * End-to-end tests run the built UI against the real application (bridge server, services and
 * core) hosted by tools/Chameleon.DevHost, with a fake Rhino host and fake backends.
 * Run `pnpm build` first (the `e2e` script does).
 */
export default defineConfig({
  testDir: 'e2e',
  outputDir: 'test-results',
  // One host process holds state (settings, jobs), so tests run one at a time.
  workers: 1,
  fullyParallel: false,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${port}/${token}/`,
    trace: 'off',
    viewport: { width: 1280, height: 820 },
    launchOptions: process.env.PW_CHROMIUM_PATH ? { executablePath: process.env.PW_CHROMIUM_PATH } : {},
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 820 } } },
  ],
  webServer: {
    command:
      `dotnet run --project ../../tools/Chameleon.DevHost/Chameleon.DevHost.csproj -c Release -- ` +
      `--ui dist --port ${port} --token ${token} --delay 300 --fake-providers --out e2e/.out`,
    url: `http://127.0.0.1:${port}/${token}/`,
    reuseExistingServer: false,
    timeout: 180_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
