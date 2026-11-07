export interface QrMatrixResult {
  size: number;
  modules: boolean[][];
}

interface QrVersionSpec {
  version: number;
  totalData: number;
  ecPerBlock: number;
  numBlocks: number;
  align: number[];
}

// ISO/IEC 18004 Table 9 — Error Correction Level L (format indicator 0b01) for Versions 1..10
const VERSION_SPECS_L: readonly QrVersionSpec[] = [
  { version: 1, totalData: 19, ecPerBlock: 7, numBlocks: 1, align: [] },
  { version: 2, totalData: 34, ecPerBlock: 10, numBlocks: 1, align: [6, 18] },
  { version: 3, totalData: 55, ecPerBlock: 15, numBlocks: 1, align: [6, 22] },
  { version: 4, totalData: 80, ecPerBlock: 20, numBlocks: 1, align: [6, 26] },
  { version: 5, totalData: 108, ecPerBlock: 26, numBlocks: 1, align: [6, 30] },
  { version: 6, totalData: 136, ecPerBlock: 18, numBlocks: 2, align: [6, 34] },
  { version: 7, totalData: 156, ecPerBlock: 20, numBlocks: 2, align: [6, 22, 38] },
  { version: 8, totalData: 194, ecPerBlock: 24, numBlocks: 2, align: [6, 24, 42] },
  { version: 9, totalData: 232, ecPerBlock: 30, numBlocks: 2, align: [6, 26, 46] },
  { version: 10, totalData: 274, ecPerBlock: 18, numBlocks: 4, align: [6, 28, 50] },
];

function gfMul(x: number, y: number): number {
  let z = 0;
  for (let i = 7; i >= 0; i--) {
    z = (z << 1) ^ ((z >>> 7) * 0x11d);
    z ^= ((y >>> i) & 1) * x;
  }
  return z;
}

function computeRsDivisor(degree: number): Uint8Array {
  const result = new Uint8Array(degree);
  result[degree - 1] = 1;
  let root = 1;
  for (let i = 0; i < degree; i++) {
    for (let j = 0; j < degree; j++) {
      result[j] = gfMul(result[j], root);
      if (j + 1 < degree) result[j] ^= result[j + 1];
    }
    root = gfMul(root, 0x02);
  }
  return result;
}

function computeRsRemainder(data: Uint8Array, divisor: Uint8Array): Uint8Array {
  const result = new Uint8Array(divisor.length);
  for (const byte of data) {
    const factor = byte ^ result[0];
    result.copyWithin(0, 1);
    result[divisor.length - 1] = 0;
    for (let i = 0; i < divisor.length; i++) {
      result[i] ^= gfMul(divisor[i], factor);
    }
  }
  return result;
}

function appendBits(val: number, len: number, out: number[]): void {
  for (let i = len - 1; i >= 0; i--) {
    out.push((val >>> i) & 1);
  }
}

function getBit(val: number, idx: number): boolean {
  return ((val >>> idx) & 1) !== 0;
}

function encodeUtf8Bytes(text: string): Uint8Array {
  if (typeof TextEncoder !== 'undefined') {
    return new TextEncoder().encode(text);
  }
  return Uint8Array.from(Array.from(text).map(ch => ch.charCodeAt(0) & 0xff));
}

function buildFinalCodewords(payload: Uint8Array, spec: QrVersionSpec): Uint8Array {
  const countBits = spec.version <= 9 ? 8 : 16;
  const capacityBits = spec.totalData * 8;
  const bits: number[] = [];

  appendBits(0b0100, 4, bits); // Byte mode indicator
  appendBits(payload.length, countBits, bits);
  for (const b of payload) appendBits(b, 8, bits);

  appendBits(0, Math.min(4, capacityBits - bits.length), bits);
  while (bits.length % 8 !== 0) bits.push(0);

  const dataBytes = new Uint8Array(spec.totalData);
  const packedLen = bits.length >>> 3;
  for (let i = 0; i < packedLen; i++) {
    let byte = 0;
    for (let b = 0; b < 8; b++) byte = (byte << 1) | bits[i * 8 + b];
    dataBytes[i] = byte;
  }
  for (let i = packedLen, padIdx = 0; i < spec.totalData; i++, padIdx++) {
    dataBytes[i] = padIdx % 2 === 0 ? 0xec : 0x11;
  }

  const numBlocks = spec.numBlocks;
  const shortLen = Math.floor(spec.totalData / numBlocks);
  const numShort = numBlocks - (spec.totalData % numBlocks);
  const rsDiv = computeRsDivisor(spec.ecPerBlock);

  const dataBlocks: Uint8Array[] = [];
  const ecBlocks: Uint8Array[] = [];
  let offset = 0;
  for (let b = 0; b < numBlocks; b++) {
    const blockLen = shortLen + (b < numShort ? 0 : 1);
    const block = dataBytes.subarray(offset, offset + blockLen);
    offset += blockLen;
    dataBlocks.push(block);
    ecBlocks.push(computeRsRemainder(block, rsDiv));
  }

  const out = new Uint8Array(spec.totalData + numBlocks * spec.ecPerBlock);
  let pos = 0;
  for (let i = 0; i <= shortLen; i++) {
    for (let b = 0; b < numBlocks; b++) {
      if (i < dataBlocks[b].length) out[pos++] = dataBlocks[b][i];
    }
  }
  for (let i = 0; i < spec.ecPerBlock; i++) {
    for (let b = 0; b < numBlocks; b++) {
      out[pos++] = ecBlocks[b][i];
    }
  }
  return out;
}

function drawFormatBits(modules: boolean[][], isFunc: boolean[][], size: number, mask: number): void {
  // ECC Level L format bits = 0b01 = 1
  const data = (0b01 << 3) | mask;
  let rem = data;
  for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
  const bits = ((data << 10) | rem) ^ 0x5412;

  const setF = (r: number, c: number, dark: boolean) => {
    modules[r][c] = dark;
    isFunc[r][c] = true;
  };

  for (let i = 0; i <= 5; i++) setF(i, 8, getBit(bits, i));
  setF(7, 8, getBit(bits, 6));
  setF(8, 8, getBit(bits, 7));
  setF(8, 7, getBit(bits, 8));
  for (let i = 9; i < 15; i++) setF(8, 14 - i, getBit(bits, i));

  for (let i = 0; i < 8; i++) setF(8, size - 1 - i, getBit(bits, i));
  for (let i = 8; i < 15; i++) setF(size - 15 + i, 8, getBit(bits, i));
  setF(size - 8, 8, true); // Fixed dark module
}

function maskCondition(mask: number, r: number, c: number): boolean {
  switch (mask) {
    case 0: return (r + c) % 2 === 0;
    case 1: return r % 2 === 0;
    case 2: return c % 3 === 0;
    case 3: return (r + c) % 3 === 0;
    case 4: return (Math.floor(r / 2) + Math.floor(c / 3)) % 2 === 0;
    case 5: return ((r * c) % 2) + ((r * c) % 3) === 0;
    case 6: return (((r * c) % 2) + ((r * c) % 3)) % 2 === 0;
    default: return (((r + c) % 2) + ((r * c) % 3)) % 2 === 0;
  }
}

function computePenalty(m: boolean[][], size: number): number {
  let penalty = 0;
  const scoreLine = (getCell: (i: number) => boolean) => {
    let runColor = getCell(0);
    let runLen = 1;
    for (let i = 1; i < size; i++) {
      const cur = getCell(i);
      if (cur === runColor) {
        runLen++;
        if (runLen === 5) penalty += 3;
        else if (runLen > 5) penalty += 1;
      } else {
        runColor = cur;
        runLen = 1;
      }
    }
  };

  for (let r = 0; r < size; r++) scoreLine(c => m[r][c]);
  for (let c = 0; c < size; c++) scoreLine(r => m[r][c]);

  let darkCount = 0;
  for (let r = 0; r < size; r++) {
    for (let c = 0; c < size; c++) {
      if (m[r][c]) darkCount++;
      if (r < size - 1 && c < size - 1) {
        const v = m[r][c];
        if (v === m[r][c + 1] && v === m[r + 1][c] && v === m[r + 1][c + 1]) penalty += 3;
      }
    }
  }
  const total = size * size;
  const k = Math.ceil(Math.abs(darkCount * 20 - total * 10) / total) - 1;
  if (k > 0) penalty += k * 10;
  return penalty;
}

export function encodeQrMatrix(text: string): QrMatrixResult {
  const rawBytes = encodeUtf8Bytes(text || '');
  let spec = VERSION_SPECS_L[VERSION_SPECS_L.length - 1];
  for (const candidate of VERSION_SPECS_L) {
    const countBits = candidate.version <= 9 ? 8 : 16;
    const maxBytes = Math.floor((candidate.totalData * 8 - 4 - countBits) / 8);
    if (rawBytes.length <= maxBytes) {
      spec = candidate;
      break;
    }
  }
  const maxV10Bytes = 271;
  const payload = rawBytes.length > maxV10Bytes ? rawBytes.subarray(0, maxV10Bytes) : rawBytes;
  const size = 17 + 4 * spec.version;

  const baseModules: boolean[][] = Array.from({ length: size }, () => Array<boolean>(size).fill(false));
  const isFunc: boolean[][] = Array.from({ length: size }, () => Array<boolean>(size).fill(false));
  const setF = (r: number, c: number, dark: boolean) => {
    baseModules[r][c] = dark;
    isFunc[r][c] = true;
  };

  for (let i = 0; i < size; i++) {
    setF(6, i, i % 2 === 0);
    setF(i, 6, i % 2 === 0);
  }

  const drawFinder = (cr: number, cc: number) => {
    for (let dr = -4; dr <= 4; dr++) {
      for (let dc = -4; dc <= 4; dc++) {
        const r = cr + dr;
        const c = cc + dc;
        if (r >= 0 && r < size && c >= 0 && c < size) {
          const dist = Math.max(Math.abs(dr), Math.abs(dc));
          setF(r, c, dist !== 2 && dist !== 4);
        }
      }
    }
  };
  drawFinder(3, 3);
  drawFinder(3, size - 4);
  drawFinder(size - 4, 3);

  for (const ar of spec.align) {
    for (const ac of spec.align) {
      if ((ar === 6 && ac === 6) || (ar === 6 && ac === size - 7) || (ar === size - 7 && ac === 6)) continue;
      for (let dr = -2; dr <= 2; dr++) {
        for (let dc = -2; dc <= 2; dc++) {
          setF(ar + dr, ac + dc, Math.max(Math.abs(dr), Math.abs(dc)) !== 1);
        }
      }
    }
  }

  drawFormatBits(baseModules, isFunc, size, 0);

  if (spec.version >= 7) {
    let rem = spec.version;
    for (let i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1f25);
    const vBits = (spec.version << 12) | rem;
    for (let i = 0; i < 18; i++) {
      const dark = getBit(vBits, i);
      const a = size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      setF(b, a, dark);
      setF(a, b, dark);
    }
  }

  const codewords = buildFinalCodewords(payload, spec);
  const totalBits = codewords.length * 8;
  let bitIdx = 0;
  let upward = true;
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5;
    for (let v = 0; v < size; v++) {
      const r = upward ? size - 1 - v : v;
      for (let j = 0; j < 2; j++) {
        const c = right - j;
        if (!isFunc[r][c]) {
          baseModules[r][c] = bitIdx < totalBits && ((codewords[bitIdx >>> 3] >>> (7 - (bitIdx & 7))) & 1) === 1;
          bitIdx++;
        }
      }
    }
    upward = !upward;
  }

  let bestModules = baseModules;
  let bestScore = Number.POSITIVE_INFINITY;
  for (let mask = 0; mask < 8; mask++) {
    const candidate = baseModules.map((row, r) =>
      row.map((val, c) => (!isFunc[r][c] && maskCondition(mask, r, c) ? !val : val)),
    );
    drawFormatBits(candidate, isFunc, size, mask);
    const score = computePenalty(candidate, size);
    if (score < bestScore) {
      bestScore = score;
      bestModules = candidate;
    }
  }

  return { size, modules: bestModules };
}
