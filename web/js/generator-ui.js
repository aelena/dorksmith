// Generator screen: form state, input-type inference, intent filtering, request, result cards.
import { $, $$, el, clear, show, setText, toast } from './dom.js';
import { api, ApiError, googleSearchUrl } from './api.js';
import { storage } from './storage.js';
import { copyText, flashCopied } from './clipboard.js';
import { renderQuery } from './query-format.js';
import { operatorByToken } from './operators-ui.js';
import { analyzeInput } from './validator.js';

const state = {
  config: null,
  intents: [],
  groups: [],
  fileTypes: null,
  typeTouched: false,
  lastResponse: null,
  lastPayload: null,
};

const CONTEXT_TYPES = new Set(['person', 'username']);

/* ------------------------------------------------------------------ inference */
const EMAIL_RE = /^[^\s@"'<>()[\],;:\\]+@(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;
const DOMAIN_RE = /^(?:\*\.)?(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})\.?$/i;
const URL_RE = /^(?:https?:\/\/)?(?:[a-z0-9-]+\.)+[a-z]{2,63}(?::\d+)?\/\S*$/i;
const FILENAME_RE = /^[^\s/\\]+\.([a-z0-9]{1,12})$/i;

/** Deterministic, local input-type suggestion. The user can always override. */
export function inferInputType(raw, knownExtensions = null) {
  const s = (raw || '').trim().replace(/^"(.*)"$/, '$1');
  if (!s) return null;
  if (/^@[^\s@]+$/.test(s)) return 'username';
  if (EMAIL_RE.test(s)) return 'email';
  if (/^https?:\/\//i.test(s) || URL_RE.test(s)) return 'url';
  if (DOMAIN_RE.test(s)) {
    const m = FILENAME_RE.exec(s);
    if (m && knownExtensions && knownExtensions.has(m[1].toLowerCase()) && s.split('.').length === 2) return 'filename';
    return 'domain';
  }
  const fm = FILENAME_RE.exec(s);
  if (fm && knownExtensions && knownExtensions.has(fm[1].toLowerCase())) return 'filename';
  const words = s.split(/\s+/);
  if (words.length >= 2 && words.length <= 3 && words.every(w => /^\p{Lu}[\p{L}'’.-]+$/u.test(w))) return 'person';
  return 'keyword';
}

/* ------------------------------------------------------------------ intents */
function compatibleIntents(type) {
  return state.intents.filter(i => i.compatibleInputTypes.includes(type));
}

function populateIntents(type, preferred = null) {
  const select = $('#intent');
  const current = preferred || select.value;
  clear(select);
  const compatible = compatibleIntents(type);
  for (const g of state.groups) {
    const items = compatible.filter(i => i.group === g.id);
    if (!items.length) continue;
    const og = el('optgroup', { label: g.label });
    for (const i of items) og.append(el('option', { value: i.id, text: i.label }));
    select.append(og);
  }
  if (compatible.some(i => i.id === current)) select.value = current;
  else if (compatible.length) select.value = compatible[0].id;
  onIntentChange();
}

function currentIntent() {
  return state.intents.find(i => i.id === $('#intent').value) || null;
}

function onIntentChange() {
  const intent = currentIntent();
  const type = $('#input-type').value;
  setText('#intent-description', intent ? intent.description : '');
  show($('#intent-safety'), !!intent && intent.safety === 'defensive-exposure');
  show($('#context-options'), CONTEXT_TYPES.has(type) || (intent && (intent.requiresOptions || []).some(o => ['organization', 'location', 'role', 'displayName'].includes(o))));

  const required = new Set([...(intent?.requiresOptions || []), ...((intent?.requiresOptionsForInputTypes || {})[type] || [])]);
  const map = { site: '#site', organization: '#organization', location: '#location', role: '#role', displayName: '#display-name', dates: '#after' };
  for (const [name, sel] of Object.entries(map)) {
    const input = $(sel);
    const label = input.closest('.field')?.querySelector('label .optional');
    if (label) label.textContent = required.has(name) ? 'required for this intent' : name === 'dates' ? 'optional' : 'optional';
    input.toggleAttribute('aria-required', required.has(name));
  }
  if (required.has('dates')) $('#before').closest('.field').querySelector('.optional').textContent = 'or after';
  if (required.size) $('#advanced').open = true;

  const hint = $('#input-hint');
  if (intent && intent.defaultFileTypes?.length && !selectedFileTypes().length && $$('#filetypes input').length) {
    hint.dataset.defaults = `Default file types for this intent: ${intent.defaultFileTypes.join(', ')}`;
  } else { delete hint.dataset.defaults; }
  updateHint();
}

/* ------------------------------------------------------------------ file types */
function renderFileTypes(catalog) {
  const root = clear($('#filetypes'));
  const byExt = new Map(catalog.extensions.map(e => [e.ext, e]));
  for (const g of catalog.groups) {
    const exts = g.extensions.filter(x => byExt.has(x));
    if (!exts.length) continue;
    const wrap = el('div', { class: 'chip-row' });
    wrap.append(el('span', { class: 'meta chip-row-label', text: g.label }));
    for (const x of exts) {
      if (wrap.querySelector(`input[value="${x}"]`)) continue;
      const def = byExt.get(x);
      wrap.append(el('label', { title: def.label }, [
        el('input', { type: 'checkbox', name: 'fileTypes', value: x, 'data-defensive': def.defensive ? 'true' : null }),
        x,
      ]));
    }
    root.append(wrap);
  }
  root.addEventListener('change', updateHint);
}

function selectedFileTypes() {
  return $$('#filetypes input:checked').map(i => i.value);
}

/* ------------------------------------------------------------------ form <-> payload */
function updateHint() {
  const hint = $('#input-hint');
  const parts = [];
  if (hint.dataset.detected) parts.push(hint.dataset.detected);
  if (hint.dataset.defaults && !selectedFileTypes().length) parts.push(hint.dataset.defaults);
  hint.textContent = parts.join(' · ');
}

function onInputChanged() {
  const raw = $('#input').value;
  const hint = $('#input-hint');
  if (!raw.trim()) { state.typeTouched = false; delete hint.dataset.detected; updateHint(); return; }
  const inferred = inferInputType(raw, state.fileTypes ? new Set(state.fileTypes.extensions.map(e => e.ext)) : null);
  if (inferred && !state.typeTouched && inferred !== $('#input-type').value) {
    $('#input-type').value = inferred;
    populateIntents(inferred);
  }
  hint.dataset.detected = inferred ? `Detected: ${labelForType(inferred)}${state.typeTouched ? ' (using your selection)' : ''}` : '';
  updateHint();
  renderInputWarnings(raw);
}

let warnTimer = null;
function renderInputWarnings(raw) {
  clearTimeout(warnTimer);
  warnTimer = setTimeout(() => {
    const list = clear($('#input-warnings'));
    for (const w of analyzeInput(raw)) list.append(el('li', { text: w }));
  }, 150);
}

function labelForType(type) {
  const opt = $(`#input-type option[value="${type}"]`);
  return opt ? opt.textContent : type;
}

function readPayload() {
  const list = (v) => v.split(',').map(s => s.trim()).filter(Boolean);
  const val = (sel) => $(sel).value.trim() || null;
  const variants = Number($('#variants').value) || state.config?.defaultVariants || 6;
  return {
    input: $('#input').value.trim(),
    inputType: $('#input-type').value,
    intent: $('#intent').value,
    engine: 'google',
    options: {
      fileTypes: selectedFileTypes(),
      excludeTerms: list($('#exclude').value),
      after: val('#after'),
      before: val('#before'),
      site: val('#site'),
      maxVariants: Math.min(Math.max(1, variants), state.config?.maxVariants || 12),
      organization: val('#organization'),
      location: val('#location'),
      role: val('#role'),
      displayName: val('#display-name'),
    },
  };
}

const FIELD_MAP = {
  input: '#input', inputType: '#input-type', intent: '#intent', 'options.site': '#site', 'options.after': '#after',
  'options.before': '#before', 'options.excludeTerms': '#exclude', 'options.maxVariants': '#variants',
  'options.organization': '#organization', 'options.location': '#location', 'options.role': '#role',
  'options.displayName': '#display-name', 'options.fileTypes': '#filetypes', 'options.dates': '#after',
};

function showFormError(message, field) {
  const box = $('#form-error');
  box.textContent = message;
  show(box);
  $$('[aria-invalid]').forEach(n => n.removeAttribute('aria-invalid'));
  const target = FIELD_MAP[field] && $(FIELD_MAP[field]);
  if (target) {
    target.setAttribute('aria-invalid', 'true');
    if (target.closest('details')) target.closest('details').open = true;
    target.focus?.();
  }
}

function clearFormError() {
  show($('#form-error'), false);
  $$('[aria-invalid]').forEach(n => n.removeAttribute('aria-invalid'));
}

/* ------------------------------------------------------------------ results */
function renderVariant(v, index) {
  const card = $('#tpl-variant').content.firstElementChild.cloneNode(true);
  setText('.variant-label', `${index + 1}. ${v.label}`, card);
  setText('.variant-id', v.id, card);
  $('.variant-id', card).title = v.rankReason || '';
  renderQuery($('code', card), v.query);
  setText('.explanation', v.explanation, card);

  const chips = clear($('.chips', card));
  for (const token of v.operators || []) {
    const op = operatorByToken(token);
    chips.append(el('span', { class: 'chip', text: token, title: op ? `${op.name} · ${op.support}` : token, dataset: { support: op?.support || 'unknown' } }));
  }
  const warnings = clear($('.warnings', card));
  for (const w of v.warnings || []) warnings.append(el('li', { text: w }));

  const copyBtn = $('.copy', card);
  copyBtn.addEventListener('click', async () => { if (await copyText(v.query)) flashCopied(copyBtn); });
  const open = $('.open', card);
  open.href = googleSearchUrl(v.query);
  open.setAttribute('aria-label', `Open variant ${index + 1} in Google (new tab)`);

  $('.query', card).addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'c' && !window.getSelection()?.toString()) { e.preventDefault(); copyText(v.query); }
  });
  return card;
}

function renderResults(res) {
  state.lastResponse = res;
  const list = clear($('#results'));
  res.variants.forEach((v, i) => list.append(renderVariant(v, i)));
  show($('#results-error'), false);
  show($('#results-tools'), res.variants.length > 0);
  setText('#results-status', `${res.variants.length} variant${res.variants.length === 1 ? '' : 's'} for ${res.inputType} “${res.normalizedInput}” · intent ${res.intent}`);
  const meta = $('#results-meta');
  const rl = res.rateLimit ? ` · ${res.rateLimit.remaining}/${res.rateLimit.limit} requests left this window` : '';
  meta.textContent = `catalog ${res.catalogVersion} · request ${res.requestId}${rl}`;
  show(meta);
}

function renderError(err) {
  const box = clear($('#results-error'));
  show($('#results-tools'), false);
  if (err.status === 429) {
    const mins = err.retryAfterSeconds ? Math.ceil(err.retryAfterSeconds / 60) : null;
    box.append(el('strong', { text: 'Rate limit reached. ' }), err.message || 'Hourly request limit reached.');
    if (mins) box.append(el('span', { class: 'retry', text: `Try again in about ${mins} minute${mins === 1 ? '' : 's'}.` }));
    setText('#results-status', 'Rate limited.');
  } else if (err.status === 503) {
    box.append(el('strong', { text: 'Service not ready. ' }), err.message);
    setText('#results-status', 'The API reported it is not ready.');
  } else if (err.status === 0) {
    box.append(el('strong', { text: 'Network error. ' }), err.message);
    setText('#results-status', 'Could not reach the API.');
  } else {
    box.append(el('strong', { text: `Error ${err.status}. ` }), err.message);
    setText('#results-status', 'Generation failed.');
  }
  show(box);
}

/* ------------------------------------------------------------------ actions */
async function generate() {
  clearFormError();
  const payload = readPayload();
  if (!payload.input) { showFormError('Enter a target first.', 'input'); return; }
  const btn = $('#generate');
  btn.disabled = true;
  setText('#results-status', 'Generating…');
  try {
    const res = await api.generate(payload);
    state.lastPayload = payload;
    renderResults(res);
    storage.pushHistory({ input: payload.input, inputType: payload.inputType, intent: payload.intent });
    document.dispatchEvent(new CustomEvent('dorksmith:generated', { detail: { payload, response: res } }));
  } catch (e) {
    if (e instanceof ApiError && (e.status === 400 || e.status === 422)) {
      showFormError(e.message, e.field);
      setText('#results-status', e.status === 422 ? 'This combination cannot be generated. Adjust the intent or options.' : 'Fix the highlighted field and try again.');
      clear($('#results'));
      show($('#results-tools'), false);
    } else {
      renderError(e instanceof ApiError ? e : new ApiError(0, { message: String(e) }));
    }
  } finally {
    btn.disabled = false;
  }
}

function morePrecise() {
  const type = $('#input-type').value;
  const intentSel = $('#intent');
  const compatible = compatibleIntents(type).map(i => i.id);
  if (compatible.includes('exact-phrase') && intentSel.value !== 'exact-phrase') intentSel.value = 'exact-phrase';
  else if (compatible.includes('title-focused') && intentSel.value === 'exact-phrase') intentSel.value = 'title-focused';
  const intent = currentIntent();
  if (intent?.defaultFileTypes?.length && !selectedFileTypes().length && (intent.id === 'documents' || intent.id === 'public-documents')) {
    const first = $(`#filetypes input[value="${intent.defaultFileTypes[0]}"]`);
    if (first) first.checked = true;
  }
  const n = $('#variants');
  n.value = String(Math.max(1, Math.min(Number(n.value) || 6, 4)));
  onIntentChange();
  generate();
}

function broader() {
  const type = $('#input-type').value;
  const compatible = compatibleIntents(type).map(i => i.id);
  if (compatible.includes('general-discovery')) $('#intent').value = 'general-discovery';
  $('#exclude').value = '';
  $('#after').value = '';
  $('#before').value = '';
  if (type !== 'domain') $('#site').value = '';
  $$('#filetypes input:checked').forEach(i => { i.checked = false; });
  $('#variants').value = String(state.config?.maxVariants || 12);
  onIntentChange();
  generate();
}

function resetForm() {
  setTimeout(() => {
    state.typeTouched = false;
    $('#input-type').value = 'keyword';
    populateIntents('keyword', 'general-discovery');
    $$('#filetypes input:checked').forEach(i => { i.checked = false; });
    clearFormError();
    delete $('#input-hint').dataset.detected;
    updateHint();
    clear($('#results'));
    show($('#results-tools'), false);
    show($('#results-error'), false);
    show($('#results-meta'), false);
    setText('#results-status', 'Enter a target, pick an intent and generate.');
    $('#input').focus();
  }, 0);
}

/* ------------------------------------------------------------------ init */
export async function initGeneratorUi() {
  const form = $('#generator-form');
  form.addEventListener('submit', (e) => { e.preventDefault(); generate(); });
  form.addEventListener('reset', resetForm);
  form.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); generate(); }
  });
  $('#input').addEventListener('input', onInputChanged);
  $('#input-type').addEventListener('change', () => { state.typeTouched = true; populateIntents($('#input-type').value); onInputChanged(); });
  $('#intent').addEventListener('change', onIntentChange);
  $('#copy-all').addEventListener('click', async () => {
    if (!state.lastResponse) return;
    const text = state.lastResponse.variants.map(v => v.query).join('\n');
    if (await copyText(text, `Copied ${state.lastResponse.variants.length} queries`)) flashCopied($('#copy-all'));
  });
  $('#more-precise').addEventListener('click', morePrecise);
  $('#broader').addEventListener('click', broader);

  api.config().then((cfg) => {
    state.config = cfg;
    $('#variants').max = String(cfg.maxVariants);
    $('#variants').value = String(cfg.defaultVariants);
  }).catch(() => { /* notices already report the outage */ });

  try {
    const [intents, fileTypes] = await Promise.all([api.intents(), api.fileTypes()]);
    state.intents = intents.intents;
    state.groups = intents.groups;
    state.fileTypes = fileTypes;
    renderFileTypes(fileTypes);
    populateIntents($('#input-type').value, 'general-discovery');
  } catch (e) {
    showFormError(`Could not load catalogs: ${e.message}`);
  }
}

/** Exposed for autocomplete: apply a suggestion to the form. */
export const generatorApi = {
  get intents() { return state.intents; },
  get fileTypes() { return state.fileTypes; },
  get inputType() { return $('#input-type').value; },
  setIntent(id) { const s = $('#intent'); if ($(`option[value="${id}"]`, s)) { s.value = id; onIntentChange(); } },
  setInputType(type) { state.typeTouched = true; $('#input-type').value = type; populateIntents(type); onInputChanged(); },
  toggleFileType(ext) { const box = $(`#filetypes input[value="${ext}"]`); if (box) { box.checked = true; updateHint(); $('#advanced').open = true; } },
  generate,
};
