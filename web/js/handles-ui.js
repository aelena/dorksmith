// Username / handle discovery: category filters, expansion request, grouped profile cards.
import { $, $$, el, clear, show, setText } from './dom.js';
import { api, ApiError, googleSearchUrl } from './api.js';
import { copyText, flashCopied } from './clipboard.js';
import { renderQuery } from './query-format.js';

const state = { categories: [], last: null };

function renderCategories(catalog) {
  const root = clear($('#handle-categories'));
  for (const c of catalog.categories) {
    root.append(el('label', {}, [
      el('input', { type: 'checkbox', name: 'categories', value: c.id }),
      c.label,
    ]));
  }
  state.categories = catalog.categories;
}

function selectedCategories() {
  return $$('#handle-categories input:checked').map(i => i.value);
}

function renderProfile(p) {
  const card = $('#tpl-profile').content.firstElementChild.cloneNode(true);
  setText('.platform-name', p.platformName, card);
  const status = $('.status', card);
  status.textContent = p.status === 'not-checked' ? 'not checked' : p.status;
  status.title = 'Dorksmith never probes accounts; open the URL yourself to verify.';
  const link = $('.profile-url', card);
  if (p.url) { link.href = p.url; link.textContent = p.url; }
  else { link.removeAttribute('href'); link.textContent = 'No reliable profile URL for this handle'; link.classList.add('muted'); }
  renderQuery($('code', card), p.searchQuery);
  const caveat = $('.caveat', card);
  caveat.textContent = p.caveat || '';
  const copyUrl = $('.copy-url', card);
  if (p.url) copyUrl.addEventListener('click', async () => { if (await copyText(p.url)) flashCopied(copyUrl); });
  else copyUrl.disabled = true;
  const copyQuery = $('.copy-query', card);
  copyQuery.addEventListener('click', async () => { if (await copyText(p.searchQuery)) flashCopied(copyQuery); });
  const open = $('.open', card);
  open.href = googleSearchUrl(p.searchQuery);
  open.setAttribute('aria-label', `Search Google for the handle on ${p.platformName} (new tab)`);
  return card;
}

function renderQueryCard(q, index) {
  const card = $('#tpl-variant').content.firstElementChild.cloneNode(true);
  setText('.variant-label', `${index + 1}. ${q.label}`, card);
  setText('.variant-id', q.id, card);
  renderQuery($('code', card), q.query);
  setText('.explanation', q.explanation, card);
  $('.chips', card).remove();
  $('.warnings', card).remove();
  const copyBtn = $('.copy', card);
  copyBtn.addEventListener('click', async () => { if (await copyText(q.query)) flashCopied(copyBtn); });
  $('.open', card).href = googleSearchUrl(q.query);
  return card;
}

function renderResults(res) {
  state.last = res;
  const root = clear($('#handles-results'));
  show($('#handles-results-error'), false);

  root.append(el('p', { class: 'notice notice-defensive', text: res.notice }));
  for (const w of res.warnings || []) root.append(el('p', { class: 'warnings', text: w }));

  const byCat = new Map();
  for (const p of res.profiles) {
    if (!byCat.has(p.category)) byCat.set(p.category, []);
    byCat.get(p.category).push(p);
  }
  for (const c of state.categories) {
    const items = byCat.get(c.id);
    if (!items) continue;
    root.append(el('h3', { class: 'group-title', text: `${c.label} · ${items.length}` }));
    const list = el('ol', { class: 'cards' });
    items.forEach(p => list.append(renderProfile(p)));
    root.append(list);
  }

  if (res.queries?.length) {
    root.append(el('h3', { class: 'group-title', text: 'Search-engine queries' }));
    const list = el('ol', { class: 'cards' });
    res.queries.forEach((q, i) => list.append(renderQueryCard(q, i)));
    root.append(list);
  }

  show($('#handles-tools'), res.profiles.length > 0);
  const rl = res.rateLimit ? ` · ${res.rateLimit.remaining}/${res.rateLimit.limit} requests left` : '';
  setText('#handles-status', `${res.profiles.length} platform${res.profiles.length === 1 ? '' : 's'} for “${res.normalizedUsername}” · catalog ${res.catalogVersion}${rl}`);
}

function renderError(err) {
  const box = clear($('#handles-results-error'));
  show($('#handles-tools'), false);
  if (err.status === 429) {
    const mins = err.retryAfterSeconds ? Math.ceil(err.retryAfterSeconds / 60) : null;
    box.append(el('strong', { text: 'Rate limit reached. ' }), err.message);
    if (mins) box.append(el('span', { class: 'retry', text: `Try again in about ${mins} minute${mins === 1 ? '' : 's'}.` }));
  } else {
    box.append(el('strong', { text: `Error ${err.status || ''}. ` }), err.message);
  }
  show(box);
  setText('#handles-status', 'Expansion failed.');
}

async function expand() {
  const errorBox = $('#handles-error');
  show(errorBox, false);
  const username = $('#handle').value.trim();
  if (!username) { errorBox.textContent = 'Enter a handle first.'; show(errorBox); $('#handle').focus(); return; }
  const btn = $('#expand');
  btn.disabled = true;
  setText('#handles-status', 'Expanding…');
  try {
    const res = await api.expandHandle({ username, categories: selectedCategories(), maxPlatforms: Number($('#handle-max').value) || 30 });
    renderResults(res);
  } catch (e) {
    if (e instanceof ApiError && (e.status === 400 || e.status === 422)) {
      errorBox.textContent = e.message;
      show(errorBox);
      setText('#handles-status', 'Fix the input and try again.');
    } else {
      renderError(e instanceof ApiError ? e : new ApiError(0, { message: String(e) }));
    }
  } finally {
    btn.disabled = false;
  }
}

export async function initHandlesUi() {
  const form = $('#handles-form');
  form.addEventListener('submit', (e) => { e.preventDefault(); expand(); });
  form.addEventListener('reset', () => setTimeout(() => {
    clear($('#handles-results'));
    show($('#handles-tools'), false);
    show($('#handles-error'), false);
    show($('#handles-results-error'), false);
    setText('#handles-status', 'Enter a handle and expand.');
  }, 0));
  form.addEventListener('keydown', (e) => { if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); expand(); } });
  $('#handle').addEventListener('input', () => {
    const v = $('#handle').value.trim();
    setText('#handle-hint', v.startsWith('@') ? 'The leading @ is removed for URLs and kept as a quoted "@handle" variant.' : v && /\s/.test(v) ? 'Handles cannot contain spaces.' : '');
  });
  $('#handles-copy-urls').addEventListener('click', async () => {
    const urls = (state.last?.profiles || []).map(p => p.url).filter(Boolean);
    if (urls.length && await copyText(urls.join('\n'), `Copied ${urls.length} URLs`)) flashCopied($('#handles-copy-urls'));
  });
  $('#handles-copy-queries').addEventListener('click', async () => {
    const qs = (state.last?.profiles || []).map(p => p.searchQuery);
    if (qs.length && await copyText(qs.join('\n'), `Copied ${qs.length} queries`)) flashCopied($('#handles-copy-queries'));
  });
  try {
    renderCategories(await api.platforms());
  } catch (e) {
    setText('#handles-status', `Could not load the platform catalog: ${e.message}`);
  }
  api.config().then(cfg => { $('#handle-max').max = String(cfg.maxPlatforms); $('#handle').maxLength = cfg.maxUsernameLength; }).catch(() => {});
}
