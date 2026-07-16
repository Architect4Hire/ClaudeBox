/**
 * Frontend configuration.
 *
 * `apiBaseUrl` is a SAME-ORIGIN relative prefix — never a host or `localhost:port`. The real API
 * endpoint is injected by Aspire (`WithReference(api)`) into the dev-server's Node process and
 * consumed by `proxy.conf.js`, which forwards `/api/*` to it. Keeping the value relative here means
 * the browser talks to its own origin and the injected URL stays the single source of truth.
 */
export const environment = {
  apiBaseUrl: '/api',
} as const;
