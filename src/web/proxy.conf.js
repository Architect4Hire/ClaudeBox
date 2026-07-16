// Dev-server proxy: forwards same-origin `/api/*` calls to the ASP.NET Core API.
//
// Aspire's `AddJavaScriptApp(...).WithReference(api)` injects the API endpoint into THIS Node
// process (not the browser) as service-discovery env vars of the form `services__api__<scheme>__0`.
// We read them here and proxy to whichever the API exposes, preferring https. This is the only
// place the real API host lives — the Angular app never hardcodes it.
//
// If neither var is present (e.g. `ng serve` run outside Aspire), we fall back to the local API
// dev port so the frontend is still usable standalone.
const target =
  process.env['services__api__https__0'] ??
  process.env['services__api__http__0'] ??
  'https://localhost:7001';

if (!process.env['services__api__https__0'] && !process.env['services__api__http__0']) {
  // eslint-disable-next-line no-console
  console.warn(
    `[proxy] No Aspire-injected API endpoint found; falling back to ${target}. ` +
      'Run the app via `aspire run` so the API URL is wired through service discovery.',
  );
}

module.exports = {
  '/api': {
    target,
    changeOrigin: true,
    // The API uses a dev HTTPS cert that isn't in Node's trust store.
    secure: false,
  },
};
