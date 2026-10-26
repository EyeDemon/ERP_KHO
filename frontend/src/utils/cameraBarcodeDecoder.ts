export interface DecodedCameraBarcode {
  code: string;
  format: 'ean_13' | 'ean_8' | 'upc_a' | 'code_128' | 'qr_code';
  scanlineMatches: number;
}

const L_PATTERNS: ReadonlyArray< readonly [number, number, number, number] > = [
  [3, 2, 1, 1], // 0
  [2, 2, 2, 1], // 1
  [2, 1, 2, 2], // 2
  [1, 4, 1, 1], // 3
  [1, 1, 3, 2], // 4
  [1, 2, 3, 1], // 5
  [1, 1, 1, 4], // 6
  [1, 3, 1, 2], // 7
  [1, 2, 1, 3], // 8
  [3, 1, 1, 2], // 9
];

const G_PATTERNS: ReadonlyArray< readonly [number, number, number, number] > = [
  [1, 1, 2, 3], // 0
  [1, 2, 2, 2], // 1
  [2, 2, 1, 2], // 2
  [1, 1, 4, 1], // 3
  [2, 3, 1, 1], // 4
  [1, 3, 2, 1], // 5
  [4, 1, 1, 1], // 6
  [2, 1, 3, 1], // 7
  [3, 1, 2, 1], // 8
  [2, 1, 1, 3], // 9
];

const PARITY_TO_FIRST_DIGIT: Record<string, number> = {
  LLLLLL: 0,
  LLGLGG: 1,
  LLGGLG: 2,
  LLGGGL: 3,
  LGLLGG: 4,
  LGGLLG: 5,
  LGGGLL: 6,
  LGLGLG: 7,
  LGLGGL: 8,
  LGGLGL: 9,
};

const FIRST_DIGIT_TO_PARITY: ReadonlyArray<string> = [
  'LLLLLL',
  'LLGLGG',
  'LLGGLG',
  'LLGGGL',
  'LGLLGG',
  'LGGLLG',
  'LGGGLL',
  'LGLGLG',
  'LGLGGL',
  'LGGLGL',
];

const CODE128_PATTERNS: ReadonlyArray<readonly [number, number, number, number, number, number]> = [
  [2, 1, 2, 2, 2, 2], [2, 2, 2, 1, 2, 2], [2, 2, 2, 2, 2, 1], [1, 2, 1, 2, 2, 3],
  [1, 2, 1, 3, 2, 2], [1, 3, 1, 2, 2, 2], [1, 2, 2, 2, 1, 3], [1, 2, 2, 3, 1, 2],
  [1, 3, 2, 2, 1, 2], [2, 2, 1, 2, 1, 3], [2, 2, 1, 3, 1, 2], [2, 3, 1, 2, 1, 2],
  [1, 1, 2, 2, 3, 2], [1, 2, 2, 1, 3, 2], [1, 2, 2, 2, 3, 1], [1, 1, 3, 2, 2, 2],
  [1, 2, 3, 1, 2, 2], [1, 2, 3, 2, 2, 1], [2, 2, 3, 2, 1, 1], [2, 2, 1, 1, 3, 2],
  [2, 2, 1, 2, 3, 1], [2, 1, 3, 2, 1, 2], [2, 2, 3, 1, 1, 2], [3, 1, 2, 1, 3, 1],
  [3, 1, 1, 2, 2, 2], [3, 2, 1, 1, 2, 2], [3, 2, 1, 2, 2, 1], [3, 1, 2, 2, 1, 2],
  [3, 2, 2, 1, 1, 2], [3, 2, 2, 2, 1, 1], [2, 1, 2, 1, 2, 3], [2, 1, 2, 3, 2, 1],
  [2, 3, 2, 1, 2, 1], [1, 1, 1, 3, 2, 3], [1, 3, 1, 1, 2, 3], [1, 3, 1, 3, 2, 1],
  [1, 1, 2, 3, 1, 3], [1, 3, 2, 1, 1, 3], [1, 3, 2, 3, 1, 1], [2, 1, 1, 3, 1, 3],
  [2, 3, 1, 1, 1, 3], [2, 3, 1, 3, 1, 1], [1, 1, 2, 1, 3, 3], [1, 1, 2, 3, 3, 1],
  [1, 3, 2, 1, 3, 1], [1, 1, 3, 1, 2, 3], [1, 1, 3, 3, 2, 1], [1, 3, 3, 1, 2, 1],
  [3, 1, 3, 1, 2, 1], [2, 1, 1, 3, 3, 1], [2, 3, 1, 1, 3, 1], [2, 1, 3, 1, 1, 3],
  [2, 1, 3, 3, 1, 1], [2, 1, 3, 1, 3, 1], [3, 1, 1, 1, 2, 3], [3, 1, 1, 3, 2, 1],
  [3, 3, 1, 1, 2, 1], [3, 1, 2, 1, 1, 3], [3, 1, 2, 3, 1, 1], [3, 3, 2, 1, 1, 1],
  [3, 1, 4, 1, 1, 1], [2, 2, 1, 4, 1, 1], [4, 3, 1, 1, 1, 1], [1, 1, 1, 2, 2, 4],
  [1, 1, 1, 4, 2, 2], [1, 2, 1, 1, 2, 4], [1, 2, 1, 4, 2, 1], [1, 4, 1, 1, 2, 2],
  [1, 4, 1, 2, 2, 1], [1, 1, 2, 2, 1, 4], [1, 1, 2, 4, 1, 2], [1, 2, 2, 1, 1, 4],
  [1, 2, 2, 4, 1, 1], [1, 4, 2, 1, 1, 2], [1, 4, 2, 2, 1, 1], [2, 4, 1, 2, 1, 1],
  [2, 2, 1, 1, 1, 4], [4, 1, 3, 1, 1, 1], [2, 4, 1, 1, 1, 2], [1, 3, 4, 1, 1, 1],
  [1, 1, 1, 2, 4, 2], [1, 2, 1, 1, 4, 2], [1, 2, 1, 2, 4, 1], [1, 1, 4, 2, 1, 2],
  [1, 2, 4, 1, 1, 2], [1, 2, 4, 2, 1, 1], [4, 1, 1, 2, 1, 2], [4, 2, 1, 1, 1, 2],
  [4, 2, 1, 2, 1, 1], [2, 1, 2, 1, 4, 1], [2, 1, 4, 1, 2, 1], [4, 1, 2, 1, 2, 1],
  [1, 1, 1, 1, 4, 3], [1, 1, 1, 3, 4, 1], [1, 3, 1, 1, 4, 1], [1, 1, 4, 1, 1, 3],
  [1, 1, 4, 3, 1, 1], [4, 1, 1, 1, 1, 3], [4, 1, 1, 3, 1, 1], [1, 1, 3, 1, 4, 1],
  [1, 1, 4, 1, 3, 1], [3, 1, 1, 1, 4, 1], [4, 1, 1, 1, 3, 1], [2, 1, 1, 4, 1, 2],
  [2, 1, 1, 2, 1, 4], [2, 1, 1, 2, 3, 2],
];

export interface RunSegment {
  bit: 0 | 1;
  length: number;
}

export function verifyEanModulo10Checksum(digits: string): boolean {
  if (!/^\d+$/.test(digits)) return false;
  const len = digits.length;
  if (len !== 13 && len !== 8 && len !== 12) return false;
  let sum = 0;
  for (let i = 0; i < len; i++) {
    const d = digits.charCodeAt(i) - 48;
    const posFromRight = len - 1 - i;
    const weight = posFromRight % 2 === 1 ? 3 : 1;
    sum += d * weight;
  }
  return sum % 10 === 0;
}

export function isRetailProductBarcode(rawCode: string): boolean {
  const clean = rawCode.trim();
  if (/^\d{13}$/.test(clean) || /^\d{12}$/.test(clean) || /^\d{8}$/.test(clean)) {
    return verifyEanModulo10Checksum(clean) || /^\d{12,13}$/.test(clean);
  }
  if (/\((01|10|17|21)\)/.test(clean) || /^01\d{14}/.test(clean)) {
    return true;
  }
  if (/^(SP-|SKU-|PRD-|ITEM-)/i.test(clean)) {
    return true;
  }
  return false;
}

export function describeEanPrefixCountry(code: string): string | null {
  if (!/^\d{12,13}$/.test(code)) return null;
  const prefix3 = code.slice(0, 3);
  if (prefix3 === '893') return '893 (Việt Nam)';
  const p = Number(prefix3);
  if (p >= 0 && p <= 139) return `${prefix3} (Mỹ / Canada)`;
  if (p >= 450 && p <= 459) return `${prefix3} (Nhật Bản)`;
  if (p >= 490 && p <= 499) return `${prefix3} (Nhật Bản)`;
  if (p >= 690 && p <= 699) return `${prefix3} (Trung Quốc)`;
  if (p === 880) return '880 (Hàn Quốc)';
  if (p === 885) return '885 (Thái Lan)';
  if (p === 888) return '888 (Singapore)';
  if (p >= 400 && p <= 440) return `${prefix3} (Đức)`;
  return prefix3;
}

/**
 * Sinh chuỗi 95 bit (0/1) chuẩn quốc tế GS1 cho mã EAN-13 (phục vụ kiểm thử & đối chiếu).
 */
export function encodeEan13Modules(code: string): Uint8Array {
  const clean = code.trim();
  if (!/^\d{13}$/.test(clean) || !verifyEanModulo10Checksum(clean)) {
    throw new Error(`Mã EAN-13 không hợp lệ: ${code}`);
  }
  const firstDigit = clean.charCodeAt(0) - 48;
  const parityPattern = FIRST_DIGIT_TO_PARITY[firstDigit];
  const modules = new Uint8Array(95);
  let offset = 0;

  const pushRuns = (runs: readonly number[], startBit: 0 | 1) => {
    let bit: 0 | 1 = startBit;
    for (const len of runs) {
      for (let k = 0; k < len; k++) {
        modules[offset++] = bit;
      }
      bit = bit === 1 ? 0 : 1;
    }
  };

  // Left Guard: 101
  pushRuns([1, 1, 1], 1);

  // Left 6 digits (digits 1..6)
  for (let i = 0; i < 6; i++) {
    const digit = clean.charCodeAt(i + 1) - 48;
    const isG = parityPattern[i] === 'G';
    const pattern = isG ? G_PATTERNS[digit] : L_PATTERNS[digit];
    pushRuns(pattern, 0);
  }

  // Center Guard: 01010
  pushRuns([1, 1, 1, 1, 1], 0);

  // Right 6 digits (digits 7..12)
  for (let i = 0; i < 6; i++) {
    const digit = clean.charCodeAt(i + 7) - 48;
    const pattern = L_PATTERNS[digit];
    pushRuns(pattern, 1);
  }

  // Right Guard: 101
  pushRuns([1, 1, 1], 1);

  return modules;
}

/**
 * Vẽ mã vạch EAN-13 thật (95 modules + Quiet Zone) lên thẻ <canvas>,
 * hỗ trợ cả trình duyệt thật lẫn môi trường Vitest/jsdom.
 */
export function renderEan13ToCanvas(
  canvas: HTMLCanvasElement,
  code: string,
  options?: {
    tiltDegrees?: number;
    noiseLevel?: number;
    glareStrength?: number;
  },
): ImageData {
  const width = canvas.width || 640;
  const height = canvas.height || 360;
  canvas.width = width;
  canvas.height = height;

  const modules = encodeEan13Modules(code);
  const quietModules = 11;
  const totalModules = modules.length + quietModules * 2; // 117 modules
  const barAreaWidth = width * 0.76;
  const moduleWidth = barAreaWidth / totalModules;
  const startX = (width - barAreaWidth) * 0.5;
  const barTop = height * 0.16;
  const barBottom = height * 0.84;
  const tiltRad = ((options?.tiltDegrees ?? 0) * Math.PI) / 180;
  const tanTilt = Math.tan(tiltRad);
  const noiseLevel = options?.noiseLevel ?? 6;
  const glareStrength = options?.glareStrength ?? 24;

  const rgba = new Uint8ClampedArray(width * height * 4);
  const centerY = height * 0.5;

  for (let y = 0; y < height; y++) {
    const dy = y - centerY;
    const xShift = dy * tanTilt;
    for (let x = 0; x < width; x++) {
      // Bóng lóa nhẹ mô phỏng ánh đèn kho chiếu lên vỏ hộp
      const glare = Math.sin((x / width) * Math.PI) * glareStrength;
      const deterministicNoise = (((x * 13 + y * 29) % 17) - 8) * (noiseLevel / 8);
      let lum = 242 + glare + deterministicNoise;

      if (y >= barTop && y <= barBottom) {
        const sampleX = x - xShift;
        const modIdx = Math.floor((sampleX - startX) / moduleWidth) - quietModules;
        if (modIdx >= 0 && modIdx < modules.length) {
          if (modules[modIdx] === 1) {
            lum = 22 + glare * 0.35 + deterministicNoise;
          }
        }
      }

      const clamped = Math.max(0, Math.min(255, Math.round(lum)));
      const idx = (y * width + x) * 4;
      rgba[idx] = clamped;
      rgba[idx + 1] = clamped;
      rgba[idx + 2] = clamped;
      rgba[idx + 3] = 255;
    }
  }

  const imageData: ImageData =
    typeof ImageData !== 'undefined'
      ? new ImageData(rgba, width, height)
      : ({ data: rgba, width, height, colorSpace: 'srgb' } as ImageData);

  (canvas as HTMLCanvasElement & { __barcodeImageData?: ImageData }).__barcodeImageData = imageData;

  const isJsdom = typeof navigator !== 'undefined' && /jsdom/i.test(navigator.userAgent || '');
  if (!isJsdom) {
    try {
      const ctx = canvas.getContext('2d');
      if (ctx && typeof ctx.putImageData === 'function') {
        ctx.putImageData(imageData, 0, 0);
      }
    } catch {
      // Trong jsdom không có native canvas, lưu trực tiếp trên thuộc tính phụ trợ
    }
  }

  return imageData;
}

function matchFourRunDigit(
  r0: number,
  r1: number,
  r2: number,
  r3: number,
  table: ReadonlyArray<readonly [number, number, number, number]>,
): { digit: number; distance: number } | null {
  const total = r0 + r1 + r2 + r3;
  if (total <= 3) return null;
  const scale = 7 / total;
  const n0 = r0 * scale;
  const n1 = r1 * scale;
  const n2 = r2 * scale;
  const n3 = r3 * scale;

  let bestDigit = -1;
  let bestDist = Infinity;

  for (let d = 0; d < 10; d++) {
    const p = table[d];
    const d0 = Math.abs(n0 - p[0]);
    const d1 = Math.abs(n1 - p[1]);
    const d2 = Math.abs(n2 - p[2]);
    const d3 = Math.abs(n3 - p[3]);
    if (d0 > 1.05 || d1 > 1.05 || d2 > 1.05 || d3 > 1.05) continue;
    const dist = d0 + d1 + d2 + d3;
    if (dist < bestDist) {
      bestDist = dist;
      bestDigit = d;
    }
  }

  if (bestDigit === -1 || bestDist > 2.35) return null;
  return { digit: bestDigit, distance: bestDist };
}

function tryDecodeEan13AtRun(runs: ReadonlyArray<RunSegment>, startIdx: number): string | null {
  if (startIdx + 59 > runs.length) return null;
  if (runs[startIdx].bit !== 1) return null;

  // 1. Kiểm tra Left Guard (1-1-1)
  const lg0 = runs[startIdx].length;
  const lg1 = runs[startIdx + 1].length;
  const lg2 = runs[startIdx + 2].length;
  const unitEst = (lg0 + lg1 + lg2) / 3;
  if (unitEst < 0.75) return null;
  if (
    Math.abs(lg0 - unitEst) > unitEst * 0.85 ||
    Math.abs(lg1 - unitEst) > unitEst * 0.85 ||
    Math.abs(lg2 - unitEst) > unitEst * 0.85
  ) {
    return null;
  }

  // 2. Kiểm tra Center Guard (0-1-0-1-0 tại startIdx + 27 .. startIdx + 31)
  const cgIdx = startIdx + 27;
  const cgTotal =
    runs[cgIdx].length +
    runs[cgIdx + 1].length +
    runs[cgIdx + 2].length +
    runs[cgIdx + 3].length +
    runs[cgIdx + 4].length;
  const cgUnit = cgTotal / 5;
  if (Math.abs(cgUnit - unitEst) > unitEst * 0.65) return null;

  // 3. Kiểm tra Right Guard (1-0-1 tại startIdx + 56 .. startIdx + 58)
  const rgIdx = startIdx + 56;
  const rgTotal = runs[rgIdx].length + runs[rgIdx + 1].length + runs[rgIdx + 2].length;
  const rgUnit = rgTotal / 3;
  if (Math.abs(rgUnit - unitEst) > unitEst * 0.75) return null;

  // 4. Giải mã 6 chữ số bên trái (L-code hoặc G-code)
  let parity = '';
  let leftDigits = '';
  for (let i = 0; i < 6; i++) {
    const base = startIdx + 3 + i * 4;
    const r0 = runs[base].length;
    const r1 = runs[base + 1].length;
    const r2 = runs[base + 2].length;
    const r3 = runs[base + 3].length;

    const lMatch = matchFourRunDigit(r0, r1, r2, r3, L_PATTERNS);
    const gMatch = matchFourRunDigit(r0, r1, r2, r3, G_PATTERNS);

    if (!lMatch && !gMatch) return null;
    if (lMatch && (!gMatch || lMatch.distance <= gMatch.distance)) {
      parity += 'L';
      leftDigits += String(lMatch.digit);
    } else if (gMatch) {
      parity += 'G';
      leftDigits += String(gMatch.digit);
    }
  }

  const firstDigit = PARITY_TO_FIRST_DIGIT[parity];
  if (firstDigit === undefined) return null;

  // 5. Giải mã 6 chữ số bên phải (R-code, cùng độ dài run với L_PATTERNS)
  let rightDigits = '';
  for (let i = 0; i < 6; i++) {
    const base = startIdx + 32 + i * 4;
    const r0 = runs[base].length;
    const r1 = runs[base + 1].length;
    const r2 = runs[base + 2].length;
    const r3 = runs[base + 3].length;

    const rMatch = matchFourRunDigit(r0, r1, r2, r3, L_PATTERNS);
    if (!rMatch) return null;
    rightDigits += String(rMatch.digit);
  }

  const fullCode = `${firstDigit}${leftDigits}${rightDigits}`;
  if (!verifyEanModulo10Checksum(fullCode)) return null;
  return fullCode;
}

function tryDecodeEan8AtRun(runs: ReadonlyArray<RunSegment>, startIdx: number): string | null {
  if (startIdx + 43 > runs.length) return null;
  if (runs[startIdx].bit !== 1) return null;

  const lg0 = runs[startIdx].length;
  const lg1 = runs[startIdx + 1].length;
  const lg2 = runs[startIdx + 2].length;
  const unitEst = (lg0 + lg1 + lg2) / 3;
  if (unitEst < 0.8) return null;
  if (
    Math.abs(lg0 - unitEst) > unitEst * 0.8 ||
    Math.abs(lg1 - unitEst) > unitEst * 0.8 ||
    Math.abs(lg2 - unitEst) > unitEst * 0.8
  ) {
    return null;
  }

  const cgIdx = startIdx + 19;
  const cgUnit =
    (runs[cgIdx].length +
      runs[cgIdx + 1].length +
      runs[cgIdx + 2].length +
      runs[cgIdx + 3].length +
      runs[cgIdx + 4].length) /
    5;
  if (Math.abs(cgUnit - unitEst) > unitEst * 0.6) return null;

  const rgIdx = startIdx + 40;
  const rgUnit = (runs[rgIdx].length + runs[rgIdx + 1].length + runs[rgIdx + 2].length) / 3;
  if (Math.abs(rgUnit - unitEst) > unitEst * 0.7) return null;

  // Quiet zone check trước và sau mã EAN-8 để tránh khớp nhầm tập con của EAN-13
  if (startIdx > 0 && runs[startIdx - 1].length < unitEst * 4.5) return null;
  if (rgIdx + 3 < runs.length && runs[rgIdx + 3].length < unitEst * 4.5) return null;

  let leftDigits = '';
  for (let i = 0; i < 4; i++) {
    const base = startIdx + 3 + i * 4;
    const lMatch = matchFourRunDigit(
      runs[base].length,
      runs[base + 1].length,
      runs[base + 2].length,
      runs[base + 3].length,
      L_PATTERNS,
    );
    if (!lMatch) return null;
    leftDigits += String(lMatch.digit);
  }

  let rightDigits = '';
  for (let i = 0; i < 4; i++) {
    const base = startIdx + 24 + i * 4;
    const rMatch = matchFourRunDigit(
      runs[base].length,
      runs[base + 1].length,
      runs[base + 2].length,
      runs[base + 3].length,
      L_PATTERNS,
    );
    if (!rMatch) return null;
    rightDigits += String(rMatch.digit);
  }

  const fullCode = `${leftDigits}${rightDigits}`;
  if (!verifyEanModulo10Checksum(fullCode)) return null;
  return fullCode;
}

function matchCode128Symbol(runs: ReadonlyArray<RunSegment>, base: number): number {
  let total = 0;
  for (let i = 0; i < 6; i++) total += runs[base + i].length;
  if (total <= 5) return -1;
  const scale = 11 / total;
  let bestCode = -1;
  let bestDist = Infinity;

  for (let idx = 0; idx < CODE128_PATTERNS.length; idx++) {
    const pat = CODE128_PATTERNS[idx];
    let dist = 0;
    let valid = true;
    for (let k = 0; k < 6; k++) {
      const d = Math.abs(runs[base + k].length * scale - pat[k]);
      if (d > 0.9) {
        valid = false;
        break;
      }
      dist += d;
    }
    if (valid && dist < bestDist) {
      bestDist = dist;
      bestCode = idx;
    }
  }
  return bestDist <= 2.1 ? bestCode : -1;
}

function tryDecodeCode128AtRun(runs: ReadonlyArray<RunSegment>, startIdx: number): string | null {
  if (startIdx + 19 > runs.length) return null;
  if (runs[startIdx].bit !== 1) return null;

  const startSym = matchCode128Symbol(runs, startIdx);
  if (startSym !== 104 && startSym !== 105) return null; // Start B (104) hoặc Start C (105)

  let mode: 'B' | 'C' = startSym === 105 ? 'C' : 'B';
  let checksum = startSym;
  let weight = 1;
  let pos = startIdx + 6;
  let output = '';

  while (pos + 6 <= runs.length && weight <= 48) {
    // Kiểm tra Stop pattern (13 modules: 2-3-3-1-1-1-2)
    if (pos + 7 <= runs.length) {
      let stopTotal = 0;
      for (let i = 0; i < 7; i++) stopTotal += runs[pos + i].length;
      const stopScale = 13 / stopTotal;
      const stopPat = [2, 3, 3, 1, 1, 1, 2];
      let stopDist = 0;
      for (let i = 0; i < 7; i++) {
        stopDist += Math.abs(runs[pos + i].length * stopScale - stopPat[i]);
      }
      if (stopDist <= 2.2 && output.length >= 2) {
        // Code symbol trước Stop là checksum
        return null;
      }
    }

    const sym = matchCode128Symbol(runs, pos);
    if (sym < 0) return null;

    // Kiểm tra nếu symbol kế tiếp là Stop (2-3-3-1-1-1-2) thì sym hiện tại là Checksum Modulo 103
    if (pos + 13 <= runs.length) {
      let stopTotal = 0;
      for (let i = 0; i < 7; i++) stopTotal += runs[pos + 6 + i].length;
      const stopScale = 13 / stopTotal;
      const stopPat = [2, 3, 3, 1, 1, 1, 2];
      let stopDist = 0;
      for (let i = 0; i < 7; i++) {
        stopDist += Math.abs(runs[pos + 6 + i].length * stopScale - stopPat[i]);
      }
      if (stopDist <= 2.3) {
        if (checksum % 103 === sym && output.length >= 3) {
          return output;
        }
        return null;
      }
    }

    checksum += sym * weight;
    weight++;
    pos += 6;

    if (mode === 'C') {
      if (sym === 100) {
        mode = 'B';
      } else if (sym < 100) {
        output += sym < 10 ? `0${sym}` : String(sym);
      }
    } else {
      if (sym === 99) {
        mode = 'C';
      } else if (sym >= 0 && sym <= 94) {
        output += String.fromCharCode(sym + 32);
      }
    }
  }

  return null;
}

/**
 * Trích xuất độ sáng Luminance Y, làm mượt 3-tap box blur,
 * nhị phân hóa Adaptive Local Thresholding và chuyển sang Run-Length Encoding (RLE).
 */
export function extractScanlineRuns(
  rgba: Uint8ClampedArray,
  imgWidth: number,
  imgHeight: number,
  yRatio: number,
  angleDeg: number,
): RunSegment[] {
  const length = imgWidth;
  if (length < 95) return [];

  const lum = new Float32Array(length);
  const smoothed = new Float32Array(length);
  const centerY = yRatio * imgHeight;
  const centerX = imgWidth * 0.5;
  const tanAngle = Math.tan((angleDeg * Math.PI) / 180);

  for (let x = 0; x < length; x++) {
    const y = Math.round(centerY + (x - centerX) * tanAngle);
    const clampedY = y < 0 ? 0 : y >= imgHeight ? imgHeight - 1 : y;
    const idx = (clampedY * imgWidth + x) * 4;
    lum[x] = 0.299 * rgba[idx] + 0.587 * rgba[idx + 1] + 0.114 * rgba[idx + 2];
  }

  // 3-tap box blur giảm nhiễu hạt từ webcam
  smoothed[0] = lum[0];
  for (let x = 1; x < length - 1; x++) {
    smoothed[x] = (lum[x - 1] + 2 * lum[x] + lum[x + 1]) * 0.25;
  }
  smoothed[length - 1] = lum[length - 1];

  let globalMin = 255;
  let globalMax = 0;
  for (let x = 0; x < length; x++) {
    const v = smoothed[x];
    if (v < globalMin) globalMin = v;
    if (v > globalMax) globalMax = v;
  }
  if (globalMax - globalMin < 22) {
    return [];
  }

  // Adaptive Local Thresholding (cửa sổ trượt tối thiểu 28px, mở rộng theo độ phân giải ảnh)
  const halfWin = Math.max(14, Math.round(length / 18));
  const prefixSum = new Float64Array(length + 1);
  for (let x = 0; x < length; x++) {
    prefixSum[x + 1] = prefixSum[x] + smoothed[x];
  }

  const globalMid = (globalMin + globalMax) * 0.5;
  const bits = new Uint8Array(length);

  for (let x = 0; x < length; x++) {
    const left = x - halfWin < 0 ? 0 : x - halfWin;
    const right = x + halfWin >= length ? length - 1 : x + halfWin;
    const localMean = (prefixSum[right + 1] - prefixSum[left]) / (right - left + 1);
    const threshold = localMean * 0.65 + globalMid * 0.35;
    bits[x] = smoothed[x] < threshold - 3 ? 1 : 0;
  }

  const runs: RunSegment[] = [];
  let currentBit: 0 | 1 = bits[0] as 0 | 1;
  let runLen = 1;
  for (let x = 1; x < length; x++) {
    const b = bits[x] as 0 | 1;
    if (b === currentBit) {
      runLen++;
    } else {
      runs.push({ bit: currentBit, length: runLen });
      currentBit = b;
      runLen = 1;
    }
  }
  runs.push({ bit: currentBit, length: runLen });
  return runs;
}

function decodeRunsOneDirection(runs: ReadonlyArray<RunSegment>): {
  code: string;
  format: 'ean_13' | 'ean_8' | 'upc_a' | 'code_128';
} | null {
  for (let i = 0; i <= runs.length - 59; i++) {
    if (runs[i].bit !== 1) continue;
    const ean13 = tryDecodeEan13AtRun(runs, i);
    if (ean13) {
      return {
        code: ean13,
        format: ean13.startsWith('0') ? 'upc_a' : 'ean_13',
      };
    }
  }

  for (let i = 0; i <= runs.length - 43; i++) {
    if (runs[i].bit !== 1) continue;
    const ean8 = tryDecodeEan8AtRun(runs, i);
    if (ean8) {
      return { code: ean8, format: 'ean_8' };
    }
  }

  for (let i = 0; i <= runs.length - 19; i++) {
    if (runs[i].bit !== 1) continue;
    const c128 = tryDecodeCode128AtRun(runs, i);
    if (c128) {
      return { code: c128, format: 'code_128' };
    }
  }

  return null;
}

const SCANLINE_RATIOS = [0.2, 0.3, 0.38, 0.44, 0.5, 0.56, 0.62, 0.7, 0.8] as const;
const SCANLINE_ANGLES = [0, -6, 6, -12, 12] as const;

/**
 * Giải mã đa dòng quét (Multi-Scanline 1D Barcode Decoder) trực tiếp trên ImageData.
 * Bắt buộc đạt Consensus >= 2 scanlines khớp cùng một mã hợp lệ để chống nhận nhầm.
 */
export function decodeBarcodeFromImageData(imageData: ImageData): DecodedCameraBarcode | null {
  const { data, width, height } = imageData;
  if (!data || width < 95 || height < 10) return null;

  const votes = new Map<string, { count: number; format: DecodedCameraBarcode['format'] }>();

  for (const angle of SCANLINE_ANGLES) {
    for (const yRatio of SCANLINE_RATIOS) {
      const runs = extractScanlineRuns(data, width, height, yRatio, angle);
      if (runs.length < 19) continue;

      const forward = decodeRunsOneDirection(runs);
      const hit = forward ?? decodeRunsOneDirection([...runs].reverse());
      if (hit) {
        const prev = votes.get(hit.code);
        const nextCount = (prev?.count ?? 0) + 1;
        votes.set(hit.code, { count: nextCount, format: hit.format });
        if (nextCount >= 3) {
          return {
            code: hit.code,
            format: hit.format,
            scanlineMatches: nextCount,
          };
        }
      }
    }
  }

  let bestCode: string | null = null;
  let bestEntry: { count: number; format: DecodedCameraBarcode['format'] } | null = null;
  for (const [code, entry] of votes.entries()) {
    if (entry.count >= 2 && (!bestEntry || entry.count > bestEntry.count)) {
      bestCode = code;
      bestEntry = entry;
    }
  }

  if (bestCode && bestEntry) {
    return {
      code: bestCode,
      format: bestEntry.format,
      scanlineMatches: bestEntry.count,
    };
  }

  return null;
}

/**
 * Giải mã mã vạch trực tiếp từ thẻ <canvas>.
 */
export function decodeBarcodeFromCanvas(canvas: HTMLCanvasElement): DecodedCameraBarcode | null {
  const fallbackImageData = (canvas as HTMLCanvasElement & { __barcodeImageData?: ImageData })
    .__barcodeImageData;
  const isJsdom = typeof navigator !== 'undefined' && /jsdom/i.test(navigator.userAgent || '');

  if (!isJsdom) {
    try {
      const ctx = canvas.getContext('2d', { willReadFrequently: true });
      if (ctx && typeof ctx.getImageData === 'function' && canvas.width > 0 && canvas.height > 0) {
        const imgData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        let hasNonZero = false;
        for (let i = 3; i < Math.min(imgData.data.length, 400); i += 4) {
          if (imgData.data[i] !== 0) {
            hasNonZero = true;
            break;
          }
        }
        if (hasNonZero) {
          return decodeBarcodeFromImageData(imgData);
        }
      }
    } catch {
      // Fallback sang __barcodeImageData nếu trong môi trường test jsdom
    }
  }

  if (fallbackImageData) {
    return decodeBarcodeFromImageData(fallbackImageData);
  }
  return null;
}

interface FrameConsensusState {
  lastCode: string;
  lastTimestampMs: number;
}

const frameConsensusState: FrameConsensusState = {
  lastCode: '',
  lastTimestampMs: 0,
};

/**
 * Chụp vùng trung tâm khung ngắm của <video> (crop 85% width × 65% height) lên <canvas>
 * và kết hợp cả window.BarcodeDetector (nếu có) cùng bộ giải mã Multi-Scanline Canvas 1D.
 */
export async function decodeBarcodeFromVideoFrame(
  video: HTMLVideoElement,
  canvas: HTMLCanvasElement,
): Promise<DecodedCameraBarcode | null> {
  if (!video || video.readyState < 2 || video.videoWidth <= 0 || video.videoHeight <= 0) {
    return null;
  }

  // 1. Thử window.BarcodeDetector trước nếu trình duyệt có hỗ trợ phần cứng
  const BarcodeDetectorCtor = (
    window as unknown as {
      BarcodeDetector?: new (opts?: { formats?: string[] }) => {
        detect: (el: HTMLVideoElement | HTMLCanvasElement) => Promise<Array<{ rawValue?: string; format?: string }>>;
      };
    }
  ).BarcodeDetector;

  if (BarcodeDetectorCtor) {
    try {
      const detector = new BarcodeDetectorCtor({
        formats: ['ean_13', 'ean_8', 'upc_a', 'code_128', 'qr_code', 'code_39', 'itf'],
      });
      const nativeHits = await detector.detect(video);
      const firstNative = nativeHits.find(c => Boolean(c.rawValue?.trim()));
      if (firstNative?.rawValue) {
        return {
          code: firstNative.rawValue.trim(),
          format: (firstNative.format as DecodedCameraBarcode['format']) || 'ean_13',
          scanlineMatches: 3,
        };
      }
    } catch {
      // Trên Windows Chrome Desktop, BarcodeDetector không hỗ trợ hoặc ném lỗi -> chuyển xuống giải mã Canvas
    }
  }

  // 2. Vẽ vùng trung tâm (crop 85% width × 65% height) của <video> lên <canvas>
  const srcW = Math.round(video.videoWidth * 0.85);
  const srcH = Math.round(video.videoHeight * 0.65);
  const srcX = Math.round((video.videoWidth - srcW) * 0.5);
  const srcY = Math.round((video.videoHeight - srcH) * 0.5);

  const targetW = Math.min(960, Math.max(480, srcW));
  const targetH = Math.min(540, Math.max(260, srcH));

  if (canvas.width !== targetW) canvas.width = targetW;
  if (canvas.height !== targetH) canvas.height = targetH;

  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) return null;

  ctx.drawImage(video, srcX, srcY, srcW, srcH, 0, 0, targetW, targetH);
  const imageData = ctx.getImageData(0, 0, targetW, targetH);
  const decoded = decodeBarcodeFromImageData(imageData);

  if (decoded) {
    const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
    if (decoded.scanlineMatches >= 2) {
      frameConsensusState.lastCode = decoded.code;
      frameConsensusState.lastTimestampMs = now;
      return decoded;
    }
    if (
      frameConsensusState.lastCode === decoded.code &&
      now - frameConsensusState.lastTimestampMs <= 450
    ) {
      return {
        ...decoded,
        scanlineMatches: 2,
      };
    }
    frameConsensusState.lastCode = decoded.code;
    frameConsensusState.lastTimestampMs = now;
  }

  return null;
}
