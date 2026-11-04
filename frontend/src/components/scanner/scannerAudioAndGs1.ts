import type { ParsedGs1Data } from './scannerTypes';

export function playScannerBeep(): void {
  try {
    const AudioCtxCtor =
      (window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext })
        .AudioContext ||
      (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AudioCtxCtor) return;
    const ctx = new AudioCtxCtor();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(880, ctx.currentTime);
    gain.gain.setValueAtTime(0.14, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.08);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.08);
    window.setTimeout(() => {
      void ctx.close().catch(() => {});
    }, 150);
  } catch {
    // Bỏ qua nếu trình duyệt chặn phát âm thanh tự động
  }
}

export function playScannerErrorBuzz(): void {
  try {
    if (typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function') {
      navigator.vibrate([200, 100, 200]);
    }
    const AudioCtxCtor =
      (window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext })
        .AudioContext ||
      (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AudioCtxCtor) return;
    const ctx = new AudioCtxCtor();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(220, ctx.currentTime);
    osc.frequency.setValueAtTime(180, ctx.currentTime + 0.12);
    gain.gain.setValueAtTime(0.22, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.26);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.26);
    window.setTimeout(() => {
      void ctx.close().catch(() => {});
    }, 320);
  } catch {
    // Bỏ qua nếu trình duyệt chặn phát âm thanh tự động
  }
}

export function parseGs1BarcodeClient(rawInput: string): ParsedGs1Data | null {
  const text = rawInput.replace(/^[ \t\r\n]+|[ \t\r\n]+$/g, '');
  if (!text) return null;
  const hasParens = /\((00|01|10|17|21)\)/.test(text);
  const hasRawStream =
    text.includes('\x1d') ||
    text.includes('\u001d') ||
    /^(?:\]C1)?(00\d{18}|01\d{14})(10|17|21)/.test(text);
  if (!hasParens && !hasRawStream) return null;

  let gtin = text.match(/\(01\)(\d{13,14})/)?.[1];
  let sscc = text.match(/\(00\)(\d{18})/)?.[1];
  let lotNumber = text.match(/\(10\)([^(^\x1d\u001d]+)/)?.[1]?.trim();
  let expRaw = text.match(/\(17\)(\d{6})/)?.[1];
  let serialNumber = text.match(/\(21\)([^(^\x1d\u001d]+)/)?.[1]?.trim();

  if (!hasParens && hasRawStream) {
    let idx = text.startsWith(']C1') ? 3 : 0;
    while (idx < text.length) {
      while (idx < text.length && (text[idx] === '\x1d' || text[idx] === '\u001d')) {
        idx += 1;
      }
      if (idx >= text.length) break;
      const ai2 = text.slice(idx, idx + 2);
      if (ai2 === '00' && /^\d{18}/.test(text.slice(idx + 2))) {
        sscc = text.slice(idx + 2, idx + 20);
        idx += 20;
        continue;
      }
      if (ai2 === '01' && /^\d{14}/.test(text.slice(idx + 2))) {
        gtin = text.slice(idx + 2, idx + 16);
        idx += 16;
        continue;
      }
      if (ai2 === '17' && /^\d{6}/.test(text.slice(idx + 2))) {
        expRaw = text.slice(idx + 2, idx + 8);
        idx += 8;
        continue;
      }
      if (ai2 === '10') {
        idx += 2;
        const start = idx;
        while (
          idx < text.length &&
          idx - start < 20 &&
          text[idx] !== '\x1d' &&
          text[idx] !== '\u001d' &&
          text[idx] !== '('
        ) {
          idx += 1;
        }
        lotNumber = text.slice(start, idx).trim();
        continue;
      }
      if (ai2 === '21') {
        idx += 2;
        const start = idx;
        while (
          idx < text.length &&
          idx - start < 20 &&
          text[idx] !== '\x1d' &&
          text[idx] !== '\u001d' &&
          text[idx] !== '('
        ) {
          idx += 1;
        }
        serialNumber = text.slice(start, idx).trim();
        continue;
      }
      idx += 1;
    }
  }

  let expiryDate: string | undefined;
  let expiryDisplay: string | undefined;
  if (expRaw) {
    const yy = expRaw.slice(0, 2);
    const mm = expRaw.slice(2, 4);
    const dd = expRaw.slice(4, 6);
    expiryDate = `20${yy}-${mm}-${dd}`;
    expiryDisplay = `${dd}/${mm}/20${yy}`;
  }

  if (!gtin && !sscc && !lotNumber && !expiryDate && !serialNumber) {
    return null;
  }

  return {
    raw: text,
    gtin,
    sscc,
    lotNumber,
    expiryDate,
    expiryDisplay,
    serialNumber,
  };
}

export function isScannerStationVisible(sectionEl: HTMLElement | null): boolean {
  if (!sectionEl) return false;
  let curr: HTMLElement | null = sectionEl;
  while (curr) {
    if (curr.hidden || curr.getAttribute('aria-hidden') === 'true') return false;
    const style = curr.style;
    if (
      style &&
      (style.display === 'none' ||
        style.visibility === 'hidden' ||
        style.pointerEvents === 'none' ||
        style.opacity === '0')
    ) {
      return false;
    }
    curr = curr.parentElement;
  }
  return true;
}

export function extractScannerChar(e: KeyboardEvent): string {
  if (
    e.key === '\x1d' ||
    e.key === '\u001d' ||
    e.key === 'GroupSeparator' ||
    e.keyCode === 29 ||
    e.which === 29
  ) {
    return '\x1d';
  }
  if (e.key && e.key.length === 1) {
    return e.key;
  }
  const code = e.keyCode || e.which || e.charCode || 0;
  if (code >= 32 && code < 127) {
    return String.fromCharCode(code);
  }
  return '';
}
