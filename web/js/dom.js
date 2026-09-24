// Small DOM helpers. All user/catalog data is written with textContent — never innerHTML.

export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

/** Create an element with attributes and text/children. `text` is set via textContent. */
export function el(tag, attrs = {}, children = []) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'text') node.textContent = String(v);
    else if (k === 'class') node.className = v;
    else if (k === 'dataset') Object.assign(node.dataset, v);
    else if (k.startsWith('on') && typeof v === 'function') node.addEventListener(k.slice(2).toLowerCase(), v);
    else node.setAttribute(k, v === true ? '' : String(v));
  }
  for (const c of [].concat(children)) {
    if (c === null || c === undefined || c === false) continue;
    node.append(typeof c === 'string' ? document.createTextNode(c) : c);
  }
  return node;
}

export function clear(node) {
  while (node.firstChild) node.removeChild(node.firstChild);
  return node;
}

export function show(node, visible = true) {
  if (visible) node.removeAttribute('hidden'); else node.setAttribute('hidden', '');
  return node;
}

export function setText(sel, text, root = document) {
  const n = typeof sel === 'string' ? $(sel, root) : sel;
  if (n) n.textContent = text ?? '';
  return n;
}

let toastTimer = null;
/** Non-blocking status toast; also announced through the aria-live region. */
export function toast(message, ms = 1800) {
  const t = $('#toast');
  if (!t) return;
  t.textContent = message;
  show(t);
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => show(t, false), ms);
}

/** Debounce for input handlers. */
export function debounce(fn, wait = 120) {
  let id;
  return (...args) => { clearTimeout(id); id = setTimeout(() => fn(...args), wait); };
}

export function isTypingContext(target) {
  if (!target) return false;
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
}
