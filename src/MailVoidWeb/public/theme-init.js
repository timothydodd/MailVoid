// Set the theme before Angular bootstraps to avoid a flash of the wrong theme (FOUC).
// External (not inline) so it complies with a strict script-src 'self' CSP.
(function () {
  try {
    var saved = localStorage.getItem('theme-preference');
    var prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    var theme = saved || (prefersDark ? 'dark' : 'light');
    document.documentElement.setAttribute('data-theme', theme);
    document.documentElement.setAttribute('data-bs-theme', theme);
  } catch (e) {
    document.documentElement.setAttribute('data-theme', 'dark');
    document.documentElement.setAttribute('data-bs-theme', 'dark');
  }
})();
