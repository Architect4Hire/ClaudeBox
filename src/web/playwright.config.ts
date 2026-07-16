import { execFileSync } from 'node:child_process';
import * as path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

/**
 * Aspire assigns the Angular app's host port at run time — `AddJavaScriptApp("web", …)` in the
 * AppHost declares `.WithHttpEndpoint(env: "PORT")` with no fixed port, so it changes on every
 * `aspire run`. There is nothing stable to hardcode, and a wrong guess only surfaces as a
 * connection refusal, so the URL is discovered from live Aspire state instead.
 *
 * Just start the system and run the tests — no port to look up:
 *
 *   aspire run
 *   npm run e2e
 *
 * Set APP_WEB_URL to skip discovery and point at an already-known origin (CI, a standalone
 * `ng serve`, or a deployed environment).
 */

const APPHOST = path.resolve(__dirname, '../RecipeBox.AppHost/RecipeBox.AppHost.csproj');

/** The AppHost's name for the Angular resource. Aspire suffixes the *instance* name per session
 *  (`web-subpkfmr`), so `displayName` is the only stable way to find it. */
const WEB_RESOURCE = 'web';

function discoverWebUrl(): string {
  const override = process.env.APP_WEB_URL;
  if (override) return override;

  let raw: string;
  try {
    // --apphost is required: the tests run from src/web, which contains no AppHost, and
    // `aspire describe` would otherwise try to prompt for one and fail in a non-interactive shell.
    raw = execFileSync('aspire', ['describe', '--apphost', APPHOST, '--format', 'Json'], {
      encoding: 'utf8',
      timeout: 120_000,
      // Aspire logs progress to stderr; only stdout carries the JSON document.
      stdio: ['ignore', 'pipe', 'ignore'],
    });
  } catch {
    throw new Error(
      `Could not read Aspire state for the "${WEB_RESOURCE}" resource. Start the system with ` +
        '`aspire run`, or set APP_WEB_URL to target an app that is already running.',
    );
  }

  // Note: this document also carries generated credentials for the backing resources — extract the
  // one URL, never log or dump it.
  const resources: AspireResource[] = JSON.parse(raw).resources ?? [];
  const web = resources.find((r) => r.displayName === WEB_RESOURCE);
  const url = web?.urls?.find((u) => u.name === 'http')?.url;

  if (!url) {
    throw new Error(
      `Aspire reports no http endpoint for the "${WEB_RESOURCE}" resource (state: ${web?.state ?? 'not found'}). ` +
        'Wait for it to reach Running, then retry.',
    );
  }
  return url;
}

interface AspireResource {
  displayName?: string;
  state?: string;
  urls?: { name?: string; url?: string }[];
}

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: discoverWebUrl(),
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
