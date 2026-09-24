// Clipboard with a fallback for browsers without navigator.clipboard (or non-secure contexts).
import { toast } from './dom.js';

export async function copyText(text, message = 'Copied') {
  let ok = false;
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      ok = true;
    }
  } catch { ok = false; }

  if (!ok) {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.className = 'sr-only';
    document.body.append(ta);
    ta.select();
    try { ok = document.execCommand('copy'); } catch { ok = false; }
    ta.remove();
  }

  toast(ok ? message : 'Copy failed — select the text and copy manually');
  return ok;
}

/** Flash a button as "copied" without changing layout. */
export function flashCopied(button, label = 'Copied ✓') {
  const original = button.textContent;
  button.textContent = label;
  button.classList.add('copied');
  setTimeout(() => { button.textContent = original; button.classList.remove('copied'); }, 1400);
}
