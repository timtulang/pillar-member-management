document.querySelectorAll('[data-print]').forEach(button => button.addEventListener('click', () => window.print()));
document.querySelectorAll('.sidebar nav a').forEach(link => {
  const path = window.location.pathname;
  if (link.pathname === '/' ? path === '/' : path.startsWith(link.pathname)) {
    link.classList.add('active'); link.setAttribute('aria-current', 'page');
  }
});
