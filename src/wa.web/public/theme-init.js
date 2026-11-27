// T-015-01 (F-TRF-015, FR-015-3): resolve data-theme before first paint so dark never flashes light — no API call needed.
// Order: account theme from the session mirror written by App on sign-in (mirrors GET /auth/me exactly; SESSION_KEY is
// 'protoDrop.session' in core/auth/session.ts, NOT the HttpOnly wa.session cookie) → OS prefers-color-scheme → light default.
// ALWAYS sets data-theme="dark"|"light": guests get an explicit pre-paint dark resolution here (AC-015-2), and tokens.css's
// [data-theme] scopes are the only token source — there is no @media fallback to fall back onto.
// CHORE-deploy-hardening: external file so CSP script-src 'self' holds (no inline script).
try {
  var p = JSON.parse(localStorage.getItem('protoDrop.session') || 'null');
  if (p && typeof p.theme === 'string' && (p.theme === 'dark' || p.theme === 'light')) {
    document.documentElement.setAttribute('data-theme', p.theme); // explicit account choice wins over the OS in both directions (EC-015-1)
  } else if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
    document.documentElement.setAttribute('data-theme', 'dark');  // system/absent → follow the OS preference
  } else {
    document.documentElement.setAttribute('data-theme', 'light'); // no match → light default
  }
} catch (e) {}
