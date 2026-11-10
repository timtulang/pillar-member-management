document.querySelectorAll('[data-print]').forEach(button => button.addEventListener('click', () => window.print()));
document.querySelectorAll('.sidebar nav a').forEach(link => {
  const path = window.location.pathname;
  if (link.pathname === '/' ? path === '/' : path.startsWith(link.pathname)) {
    link.classList.add('active'); link.setAttribute('aria-current', 'page');
  }
});

// Native browser validation complements the same rules enforced on the server.
document.querySelectorAll('input[required]:not([type="password"]), textarea[required]').forEach(input => {
  if (!['text', 'search', 'tel', 'email', 'textarea'].includes(input.type)) return;
  const validate = () => input.setCustomValidity(input.value && !input.value.trim() ? 'Enter a value, not just spaces.' : '');
  input.addEventListener('input', validate);
});

document.querySelectorAll('[data-event-form]').forEach(form => {
  const repeat = form.querySelector('[data-repeat]');
  const recurrence = form.querySelector('[data-recurrence]');
  const interval = form.elements.namedItem('Input.Interval');
  const count = form.elements.namedItem('Input.Occurrences');
  const start = form.elements.namedItem('Input.StartsAt');
  const end = form.elements.namedItem('Input.EndsAt');
  const unit = form.querySelector('[data-repeat-unit]');
  const summary = form.querySelector('[data-schedule-summary]');
  const button = form.querySelector('[data-create-event]');
  const update = () => {
    const recurring = repeat.value !== 'none';
    recurrence.hidden = !recurring;
    recurrence.querySelectorAll('input').forEach(input => { input.disabled = !recurring; });
    const units = { daily: 'days', weekly: 'weeks', monthly: 'months' };
    const pluralUnit = units[repeat.value] || 'weeks';
    unit.textContent = Number(interval.value) === 1 ? pluralUnit.slice(0, -1) : pluralUnit;
    end.setCustomValidity(start.value && end.value && end.value <= start.value ? 'The end must be after the start.' : '');
    button.textContent = recurring ? 'Create recurring events' : 'Create event';
    summary.textContent = !recurring ? 'One event with its own attendance list.'
      : interval.validity.valid && count.validity.valid
        ? `${count.value} occurrences, every ${interval.value} ${unit.textContent}, including the first event.`
        : 'Choose an interval from 1 to 12 and between 2 and 52 occurrences.';
  };
  form.addEventListener('input', update);
  form.addEventListener('change', update);
  update();
});

document.querySelectorAll('[data-member-form]').forEach(form => {
  const birth = form.elements.namedItem('Input.BirthDate');
  const death = form.elements.namedItem('Input.DeceasedOn');
  const membership = form.elements.namedItem('Input.MembershipDate');
  const validate = () => {
    death.setCustomValidity(birth.value && death.value && death.value < birth.value ? 'The date of death cannot be before the birthday.' : '');
    membership.setCustomValidity(birth.value && membership.value && membership.value < birth.value ? 'Membership date cannot be before the birthday.' : '');
  };
  form.addEventListener('input', validate);
  validate();
});

document.querySelectorAll('[data-roster]').forEach(roster => {
  const search = roster.querySelector('[data-roster-search]');
  if (!search) return;
  const rows = [...roster.querySelectorAll('[data-roster-person]')];
  search.addEventListener('input', () => {
    const query = search.value.trim().toLocaleLowerCase();
    rows.forEach(row => { row.hidden = !row.textContent.toLocaleLowerCase().includes(query); });
    roster.querySelector('[data-roster-empty]').hidden = rows.some(row => !row.hidden);
  });
});

document.querySelectorAll('[data-catalog-form]').forEach(form => {
  const category = form.elements.namedItem('kind');
  const name = form.elements.namedItem('name');
  const update = () => { name.maxLength = category.value === 'role' ? 100 : 150; };
  category.addEventListener('change', update);
  update();
});

// Tabs progressively enhance the two complete lists; both remain available without JS.
document.querySelectorAll('[data-catalog]').forEach(catalog => {
  const tablist = catalog.querySelector('[role="tablist"]');
  const tabs = [...tablist.querySelectorAll('[role="tab"]')];
  const activate = tab => {
    tabs.forEach(item => {
      const selected = item === tab;
      item.setAttribute('aria-selected', String(selected));
      item.tabIndex = selected ? 0 : -1;
      const section = document.getElementById(item.getAttribute('aria-controls'));
      section.hidden = !selected;
      section.setAttribute('role', 'tabpanel');
      section.setAttribute('aria-labelledby', item.id);
    });
  };
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activate(tab));
    tab.addEventListener('keydown', event => {
      let next;
      if (event.key === 'ArrowRight') next = tabs[(index + 1) % tabs.length];
      if (event.key === 'ArrowLeft') next = tabs[(index + tabs.length - 1) % tabs.length];
      if (event.key === 'Home') next = tabs[0];
      if (event.key === 'End') next = tabs[tabs.length - 1];
      if (!next) return;
      event.preventDefault();
      activate(next);
      next.focus();
    });
  });
  catalog.classList.add('catalog-ready');
  tablist.hidden = false;
  activate(tabs[0]);
});

const sidebar = document.querySelector('.sidebar');
const navToggle = sidebar?.querySelector('.nav-toggle');
if (navToggle) {
  sidebar.classList.add('nav-ready');
  navToggle.hidden = false;
  const setOpen = open => {
    sidebar.classList.toggle('is-open', open);
    navToggle.setAttribute('aria-expanded', String(open));
    navToggle.textContent = open ? 'Close menu' : 'Menu';
  };
  navToggle.addEventListener('click', () => setOpen(navToggle.getAttribute('aria-expanded') !== 'true'));
  sidebar.addEventListener('keydown', event => {
    if (event.key === 'Escape' && sidebar.classList.contains('is-open')) {
      setOpen(false);
      navToggle.focus();
    }
  });
}

document.querySelectorAll('.table-wrap').forEach(region => {
  region.tabIndex = 0;
  region.setAttribute('role', 'region');
  if (!region.hasAttribute('aria-label')) region.setAttribute('aria-label', 'Table; scroll horizontally to see all columns');
});
