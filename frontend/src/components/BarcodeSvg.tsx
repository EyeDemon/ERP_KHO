import { encodeQrMatrix } from './qr/qrMatrixEncoder';

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

function buildQrSvgPath(modules: boolean[][], quietZone: number): string {
  const segments: string[] = [];
  for (let r = 0; r < modules.length; r++) {
    const row = modules[r];
    let c = 0;
    while (c < row.length) {
      if (!row[c]) {
        c++;
        continue;
      }
      let run = 1;
      while (c + run < row.length && row[c + run]) run++;
      segments.push(`M${c + quietZone} ${r + quietZone}h${run}v1h-${run}z`);
      c += run;
    }
  }
  return segments.join('');
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
  const render1D = format === 'QR' ? false : show1D;
  const bars = render1D ? buildBars(sscc || value) : [];
  const totalUnits = bars.reduce((sum, w) => sum + w, 0) + 12;

  let cursor = 6;
  const rects: Array<{ x: number; w: number }> = [];
  bars.forEach((w, idx) => {
    if (idx % 2 === 0) {
      rects.push({ x: cursor, w });
    }
    cursor += w;
  });

  const quietZone = 4;
  const qrPayload = format === 'QR' ? (value || '') : `${value || ''}|${sscc || ''}`;
  const qr = showQr ? encodeQrMatrix(qrPayload) : null;
  const qrViewSize = qr ? qr.size + quietZone * 2 : 29;
  const qrPath = qr ? buildQrSvgPath(qr.modules, quietZone) : '';

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
      {render1D && (
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

      {showQr && qr && (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
          <svg
            role="img"
            aria-label={`Mã vuông QR định danh ${label || ''}`.trim()}
            data-testid="barcode-qr-svg"
            data-qr-size={qr.size}
            width={height + 12}
            height={height + 12}
            viewBox={`0 0 ${qrViewSize} ${qrViewSize}`}
            shapeRendering="crispEdges"
            style={{ display: 'block', background: '#ffffff' }}
          >
            <rect x="0" y="0" width={qrViewSize} height={qrViewSize} fill="#ffffff" />
            <path d={qrPath} fill="#0f172a" />
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
