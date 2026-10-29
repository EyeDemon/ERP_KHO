interface BarcodeSvgProps {
  value: string;
  label?: string;
  sscc?: string;
  showQr?: boolean;
  show1D?: boolean;
  format?: '1D_AND_QR' | 'QR';
  qrCaption?: string;
  captionLabel?: string;
  showText?: boolean;
  height?: number;
}

function buildBars(seedText: string): number[] {
  const input = seedText || '008938505000000018';
  const bars: number[] = [2, 1, 1, 2, 3, 1];
  for (let i = 0; i < input.length; i++) {
    const code = input.charCodeAt(i);
    bars.push(((code >> 4) & 3) + 1);
    bars.push((code & 3) + 1);
    bars.push(((code >> 2) & 2) + 1);
    bars.push(((code + i) % 3) + 1);
  }
  bars.push(2, 3, 1, 1, 2);
  return bars;
}

function isFinderCell(r: number, c: number, size: number): boolean {
  const inTopLeft = r < 7 && c < 7;
  const inTopRight = r < 7 && c >= size - 7;
  const inBottomLeft = r >= size - 7 && c < 7;
  if (!inTopLeft && !inTopRight && !inBottomLeft) return false;
  const lr = inBottomLeft ? r - (size - 7) : r;
  const lc = inTopRight ? c - (size - 7) : c;
  if (lr === 0 || lr === 6 || lc === 0 || lc === 6) return true;
  if (lr >= 2 && lr <= 4 && lc >= 2 && lc <= 4) return true;
  return false;
}

function isFinderQuietZone(r: number, c: number, size: number): boolean {
  const inTopLeft = r < 8 && c < 8;
  const inTopRight = r < 8 && c >= size - 8;
  const inBottomLeft = r >= size - 8 && c < 8;
  return inTopLeft || inTopRight || inBottomLeft;
}

export function BarcodeSvg({
  value,
  label,
  sscc,
  showQr = true,
  show1D = true,
  format,
  qrCaption = 'QR GS1',
  captionLabel,
  showText = true,
  height = 44,
}: BarcodeSvgProps) {
  const bars = buildBars(sscc || value);
  const totalUnits = bars.reduce((sum, w) => sum + w, 0) + 12;
  const qrSize = 21;
  const qrSeed = `${value}|${sscc || ''}`;

  let cursor = 6;
  const rects: Array<{ x: number; w: number }> = [];
  bars.forEach((w, idx) => {
    if (idx % 2 === 0) {
      rects.push({ x: cursor, w });
    }
    cursor += w;
  });

  const qrCells: Array<{ r: number; c: number }> = [];
  for (let r = 0; r < qrSize; r++) {
    for (let c = 0; c < qrSize; c++) {
      if (isFinderQuietZone(r, c, qrSize)) {
        if (isFinderCell(r, c, qrSize)) qrCells.push({ r, c });
        continue;
      }
      const ch = qrSeed.charCodeAt((r * qrSize + c) % qrSeed.length);
      if (((ch + r * 7 + c * 13) & 3) !== 0) {
        qrCells.push({ r, c });
      }
    }
  }

  return (
    <div
      style={{
        display: 'inline-flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        gap: 14,
        padding: '10px 14px',
        borderRadius: 8,
        border: '1px solid #cbd5e1',
        background: '#ffffff',
      }}
    >
      {(format === 'QR' ? false : show1D) && (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
        <svg
          role="img"
          aria-label={`Mã vạch 1D GS1-128 ${label || ''}`.trim()}
          data-testid="barcode-1d-svg"
          width={Math.max(180, Math.min(280, totalUnits * 1.4))}
          height={height}
          viewBox={`0 0 ${totalUnits} ${height}`}
          style={{ display: 'block', background: '#ffffff' }}
        >
          <rect x="0" y="0" width={totalUnits} height={height} fill="#ffffff" />
          {rects.map((bar, idx) => (
            <rect key={idx} x={bar.x} y={2} width={bar.w} height={height - 4} fill="#0f172a" />
          ))}
        </svg>
        {showText && (
          <div style={{ fontSize: 11, fontFamily: 'monospace', fontWeight: 600, color: '#0f172a', letterSpacing: '0.04em' }}>
            {sscc ? `(00) ${sscc}` : label}
          </div>
        )}
      </div>
      )}

      {showQr && (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
          <svg
            role="img"
            aria-label={`Mã vuông QR định danh ${label || ''}`.trim()}
            data-testid="barcode-qr-svg"
            width={height + 12}
            height={height + 12}
            viewBox={`0 0 ${qrSize + 2} ${qrSize + 2}`}
            style={{ display: 'block', background: '#ffffff' }}
          >
            <rect x="0" y="0" width={qrSize + 2} height={qrSize + 2} fill="#ffffff" />
            {qrCells.map((cell, idx) => (
              <rect key={idx} x={cell.c + 1} y={cell.r + 1} width={1} height={1} fill="#0f172a" />
            ))}
          </svg>
          {showText && (
            <div style={{ fontSize: 10, color: '#475569', fontWeight: 600 }}>
              {captionLabel || qrCaption}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default BarcodeSvg;
