import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Calendar, ChevronLeft, ChevronRight } from 'lucide-react';

export interface UiVnDateInputProps {
  id?: string;
  value: string;
  onChange: (value: string) => void;
  mode?: 'date' | 'datetime';
  placeholder?: string;
  verbalPrefix?: string;
  emptyHelperText?: string;
  ariaLabel?: string;
  disabled?: boolean;
  required?: boolean;
  testId?: string;
  minYear?: number;
  maxYear?: number;
}

const pad2 = (n: number): string => String(n).padStart(2, '0');
const WEEKDAYS = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'] as const;

export function isoToVnDate(isoOrVn: string): string {
  const s = (isoOrVn ?? '').trim();
  if (!s) return '';
  if (/^\d{2}\/\d{2}\/\d{4}(?:\s+\d{2}:\d{2})?$/.test(s)) return s;
  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  if (dateOnly) return `${dateOnly[3]}/${dateOnly[2]}/${dateOnly[1]}`;
  const isoShortDt = /^(\d{4})-(\d{2})-(\d{2})[T\s](\d{2}):(\d{2})$/.exec(s);
  if (isoShortDt) return `${isoShortDt[3]}/${isoShortDt[2]}/${isoShortDt[1]} ${isoShortDt[4]}:${isoShortDt[5]}`;
  const isoFullDt = /^(\d{4})-(\d{2})-(\d{2})[T\s](\d{2}):(\d{2}):\d{2}/.exec(s);
  if (isoFullDt) {
    const base = `${isoFullDt[3]}/${isoFullDt[2]}/${isoFullDt[1]}`;
    return isoFullDt[4] === '00' && isoFullDt[5] === '00' ? base : `${base} ${isoFullDt[4]}:${isoFullDt[5]}`;
  }
  return s;
}

export function vnDateToIso(vnOrIso: string): string {
  const s = (vnOrIso ?? '').trim();
  if (!s) return '';
  if (/^\d{4}-\d{2}-\d{2}/.test(s)) return s;
  const dMatch = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(s);
  if (dMatch) return `${dMatch[3]}-${dMatch[2]}-${dMatch[1]}`;
  const dtMatch = /^(\d{2})\/(\d{2})\/(\d{4})\s+(\d{2}):(\d{2})$/.exec(s);
  if (dtMatch) return `${dtMatch[3]}-${dtMatch[2]}-${dtMatch[1]}T${dtMatch[4]}:${dtMatch[5]}`;
  return s;
}

function parseVnParts(val: string, mode: 'date' | 'datetime', minYear = 1900, maxYear = 2100) {
  const re = mode === 'datetime' ? /^(\d{2})\/(\d{2})\/(\d{4})\s+(\d{2}):(\d{2})$/ : /^(\d{2})\/(\d{2})\/(\d{4})$/;
  const m = re.exec((val ?? '').trim());
  if (!m) return null;
  const [day, month, year] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const [hour, minute] = mode === 'datetime' ? [Number(m[4]), Number(m[5])] : [0, 0];
  if (year < minYear || year > maxYear || month < 1 || month > 12 || day < 1 || hour < 0 || hour > 23 || minute < 0 || minute > 59) return null;
  const check = new Date(year, month - 1, day);
  if (check.getFullYear() !== year || check.getMonth() !== month - 1 || check.getDate() !== day) return null;
  return { day, month, year, hour, minute, dd: m[1], mm: m[2], yyyy: m[3], hh: pad2(hour), min: pad2(minute) };
}

export function isValidVnCalendarDate(val: string, mode: 'date' | 'datetime' = 'date', minYear = 1900, maxYear = 2100): boolean {
  return parseVnParts(val, mode, minYear, maxYear) !== null;
}

function normalizeForMode(raw: string, mode: 'date' | 'datetime'): string {
  const s = (raw ?? '').trim();
  if (!s) return '';
  if (mode === 'datetime') {
    const isoDt = /^(\d{4})-(\d{2})-(\d{2})[T\s](\d{2}):(\d{2})/.exec(s);
    if (isoDt) return `${isoDt[3]}/${isoDt[2]}/${isoDt[1]} ${isoDt[4]}:${isoDt[5]}`;
  }
  const vn = isoToVnDate(s);
  return mode === 'date' && /^\d{2}\/\d{2}\/\d{4}\s+/.test(vn) ? vn.slice(0, 10) : vn;
}

function maskVnDigits(raw: string, mode: 'date' | 'datetime'): string {
  const trimmed = raw.trim();
  if (!trimmed) return '';
  if (/^\d{4}-\d{2}-\d{2}/.test(trimmed)) return normalizeForMode(trimmed, mode);
  const digits = raw.replace(/\D/g, '').slice(0, mode === 'datetime' ? 12 : 8);
  if (!digits) return trimmed;
  if (digits.length <= 2) return digits;
  if (digits.length <= 4) return `${digits.slice(0, 2)}/${digits.slice(2)}`;
  if (digits.length <= 8) return `${digits.slice(0, 2)}/${digits.slice(2, 4)}/${digits.slice(4, 8)}`;
  if (digits.length <= 10) return `${digits.slice(0, 2)}/${digits.slice(2, 4)}/${digits.slice(4, 8)} ${digits.slice(8, 10)}`;
  return `${digits.slice(0, 2)}/${digits.slice(2, 4)}/${digits.slice(4, 8)} ${digits.slice(8, 10)}:${digits.slice(10, 12)}`;
}

export const UiVnDateInput: React.FC<UiVnDateInputProps> = ({
  id, value, onChange, mode = 'date', placeholder, verbalPrefix, emptyHelperText,
  ariaLabel, disabled = false, required = false, testId, minYear = 1900, maxYear = 2100,
}) => {
  const rootRef = useRef<HTMLDivElement>(null);
  const displayValue = useMemo(() => normalizeForMode(value, mode), [value, mode]);
  const parsed = useMemo(() => parseVnParts(displayValue, mode, minYear, maxYear), [displayValue, mode, minYear, maxYear]);
  const now = useMemo(() => new Date(), []);
  const [open, setOpen] = useState(false);
  const [viewYear, setViewYear] = useState<number>(() => parsed?.year ?? now.getFullYear());
  const [viewMonth, setViewMonth] = useState<number>(() => (parsed ? parsed.month - 1 : now.getMonth()));
  const [selHour, setSelHour] = useState<number>(() => parsed?.hour ?? now.getHours());
  const [selMinute, setSelMinute] = useState<number>(() => parsed?.minute ?? now.getMinutes());

  useEffect(() => {
    if (!parsed) return;
    setViewYear(parsed.year);
    setViewMonth(parsed.month - 1);
    if (mode === 'datetime') {
      setSelHour(parsed.hour);
      setSelMinute(parsed.minute);
    }
  }, [parsed, mode]);

  useEffect(() => {
    if (!open) return;
    const onMouseDown = (e: MouseEvent) => {
      if (rootRef.current && e.target instanceof Node && !rootRef.current.contains(e.target)) setOpen(false);
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onMouseDown);
    window.addEventListener('keydown', onKeyDown, true);
    return () => {
      document.removeEventListener('mousedown', onMouseDown);
      window.removeEventListener('keydown', onKeyDown, true);
    };
  }, [open]);

  const emitDate = (y: number, mZero: number, d: number, h = selHour, min = selMinute, closeOnDate = true) => {
    const base = `${pad2(d)}/${pad2(mZero + 1)}/${y}`;
    onChange(mode === 'datetime' ? `${base} ${pad2(h)}:${pad2(min)}` : base);
    setViewYear(y);
    setViewMonth(mZero);
    if (mode === 'date' && closeOnDate) setOpen(false);
  };

  const shiftMonth = (delta: number) => {
    const next = new Date(viewYear, viewMonth + delta, 1);
    setViewYear(next.getFullYear());
    setViewMonth(next.getMonth());
  };

  const applyPlusOneYear = () => {
    const baseYear = (parsed?.year ?? new Date().getFullYear()) + 1;
    const baseMonth = parsed ? parsed.month - 1 : new Date().getMonth();
    const maxDay = new Date(baseYear, baseMonth + 1, 0).getDate();
    const baseDay = Math.min(parsed?.day ?? new Date().getDate(), maxDay);
    emitDate(baseYear, baseMonth, baseDay);
  };

  const firstDayOffset = (new Date(viewYear, viewMonth, 1).getDay() + 6) % 7;
  const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
  const resolvedPlaceholder = placeholder ?? (mode === 'datetime' ? 'dd/MM/yyyy HH:mm' : 'dd/MM/yyyy');
  const defaultEmptyHelper = mode === 'datetime'
    ? 'Định dạng: Ngày/Tháng/Năm Giờ:Phút (VD: 02/11/2026 18:15)'
    : 'Định dạng: Ngày/Tháng/Năm (VD: 31/12/2026)';
  const isInvalidNonEmpty = displayValue.trim().length > 0 && !parsed;
  const verbalText = !displayValue.trim()
    ? (emptyHelperText ?? defaultEmptyHelper)
    : parsed
      ? mode === 'datetime'
        ? `${verbalPrefix ? `${verbalPrefix}: ` : ''}${parsed.hh}:${parsed.min} — Ngày ${parsed.dd} tháng ${parsed.mm} năm ${parsed.yyyy}`
        : `${verbalPrefix ? `${verbalPrefix}: ` : ''}Ngày ${parsed.dd} tháng ${parsed.mm} năm ${parsed.yyyy}`
      : 'Ngày không hợp lệ theo lịch dương (dd/MM/yyyy)';

  return (
    <div ref={rootRef} className="ui-vn-date-root">
      <div className="ui-vn-date-control">
        <input
          id={id}
          type="text"
          lang="vi"
          inputMode="numeric"
          autoComplete="off"
          className="ui-vn-date-input"
          value={displayValue}
          placeholder={resolvedPlaceholder}
          aria-label={ariaLabel}
          aria-invalid={isInvalidNonEmpty ? 'true' : undefined}
          disabled={disabled}
          required={required}
          data-testid={testId}
          onChange={(e) => onChange(maskVnDigits(e.target.value, mode))}
        />
        <button
          type="button"
          className="ui-vn-date-trigger"
          aria-label="Mở lịch chọn ngày"
          title="Mở lịch chọn ngày"
          aria-haspopup="dialog"
          aria-expanded={open}
          disabled={disabled}
          onClick={() => !disabled && setOpen((prev) => !prev)}
        >
          <Calendar size={16} strokeWidth={2} aria-hidden="true" />
        </button>
      </div>

      {open && !disabled && (
        <div role="dialog" aria-label="Lịch chọn ngày" data-testid="vn-date-popover" className="ui-vn-date-popover">
          <div className="ui-vn-date-header">
            <button type="button" className="ui-vn-date-nav-btn" aria-label="Tháng trước" title="Tháng trước" onClick={() => shiftMonth(-1)}>
              <ChevronLeft size={14} strokeWidth={2} aria-hidden="true" />
            </button>
            <strong className="tabular-nums">{`Tháng ${pad2(viewMonth + 1)} / ${viewYear}`}</strong>
            <button type="button" className="ui-vn-date-nav-btn" aria-label="Tháng sau" title="Tháng sau" onClick={() => shiftMonth(1)}>
              <ChevronRight size={14} strokeWidth={2} aria-hidden="true" />
            </button>
          </div>
          <div className="ui-vn-date-grid ui-vn-date-weekdays">
            {WEEKDAYS.map((w) => <span key={w}>{w}</span>)}
          </div>
          <div className="ui-vn-date-grid">
            {Array.from({ length: firstDayOffset }, (_, idx) => <span key={`e-${idx}`} />)}
            {Array.from({ length: daysInMonth }, (_, idx) => {
              const d = idx + 1;
              const isSelected = Boolean(parsed && parsed.year === viewYear && parsed.month - 1 === viewMonth && parsed.day === d);
              return (
                <button
                  key={d}
                  type="button"
                  className={'ui-vn-date-day tabular-nums' + (isSelected ? ' is-selected' : '')}
                  onClick={() => emitDate(viewYear, viewMonth, d)}
                >
                  {d}
                </button>
              );
            })}
          </div>
          {mode === 'datetime' && (
            <div className="ui-vn-date-time-row">
              <label>
                <span>Giờ</span>
                <select aria-label="Giờ" value={selHour} onChange={(e) => { const h = Number(e.target.value); setSelHour(h); if (parsed) emitDate(parsed.year, parsed.month - 1, parsed.day, h, selMinute, false); }}>
                  {Array.from({ length: 24 }, (_, h) => <option key={h} value={h}>{pad2(h)}</option>)}
                </select>
              </label>
              <label>
                <span>Phút</span>
                <select aria-label="Phút" value={selMinute} onChange={(e) => { const m = Number(e.target.value); setSelMinute(m); if (parsed) emitDate(parsed.year, parsed.month - 1, parsed.day, selHour, m, false); }}>
                  {Array.from({ length: 60 }, (_, m) => <option key={m} value={m}>{pad2(m)}</option>)}
                </select>
              </label>
            </div>
          )}
          <div className="ui-vn-date-quick-row">
            <button type="button" onClick={() => { const t = new Date(); emitDate(t.getFullYear(), t.getMonth(), t.getDate(), t.getHours(), t.getMinutes()); }}>Hôm nay</button>
            <button type="button" onClick={() => emitDate(2026, 11, 31)}>Cuối năm 2026</button>
            <button type="button" onClick={applyPlusOneYear}>+1 năm</button>
          </div>
        </div>
      )}

      <div
        data-testid="vn-date-verbal-helper"
        role={isInvalidNonEmpty ? 'alert' : undefined}
        className={'ui-vn-date-helper' + (isInvalidNonEmpty ? ' is-invalid' : parsed ? ' is-valid' : '')}
      >
        {verbalText}
      </div>
    </div>
  );
};

export default UiVnDateInput;
