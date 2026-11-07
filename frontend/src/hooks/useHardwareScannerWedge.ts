import { useEffect, useRef } from 'react';

export interface HardwareScannerWedgeOptions {
  onScan: (barcode: string) => void;
  enabled?: boolean;
  maxInterKeyIntervalMs?: number;
  minLength?: number;
}

const isEditableElement = (element: Element | null): boolean => {
  if (!element) return false;
  const tagName = element.tagName?.toUpperCase();
  if (tagName === 'INPUT' || tagName === 'TEXTAREA' || tagName === 'SELECT') {
    return true;
  }
  const htmlEl = element as HTMLElement;
  if (htmlEl.isContentEditable) return true;
  const contentEditableAttr = htmlEl.getAttribute?.('contenteditable');
  return contentEditableAttr === '' || contentEditableAttr === 'true';
};

const extractScannerChar = (event: KeyboardEvent): string | null => {
  if (
    event.key === '\x1d' ||
    event.key === '\u001d' ||
    event.key === 'GroupSeparator' ||
    event.keyCode === 29 ||
    event.which === 29
  ) {
    return '\x1d';
  }
  if (event.ctrlKey || event.altKey || event.metaKey) {
    return null;
  }
  if (typeof event.key === 'string' && event.key.length === 1) {
    return event.key;
  }
  return null;
};

/**
 * Global USB/Bluetooth HID Hardware Barcode Scanner Wedge Hook.
 * Captures high-speed scanner bursts (<= 35ms inter-key interval) when no input is focused,
 * preserving GS1-128 FNC1 (ASCII 29 / \x1d) group separators.
 */
export function useHardwareScannerWedge({
  onScan,
  enabled = true,
  maxInterKeyIntervalMs = 35,
  minLength = 4,
}: HardwareScannerWedgeOptions): void {
  const onScanRef = useRef(onScan);
  onScanRef.current = onScan;

  const bufferRef = useRef<string>('');
  const lastKeyTimeRef = useRef<number>(0);
  const resetTimerRef = useRef<number | null>(null);

  useEffect(() => {
    if (!enabled || typeof window === 'undefined') {
      return undefined;
    }

    const clearBuffer = () => {
      bufferRef.current = '';
      lastKeyTimeRef.current = 0;
      if (resetTimerRef.current !== null) {
        window.clearTimeout(resetTimerRef.current);
        resetTimerRef.current = null;
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (isEditableElement(document.activeElement)) {
        return;
      }

      const now = performance.now();
      const elapsed = lastKeyTimeRef.current > 0 ? now - lastKeyTimeRef.current : 0;

      if (event.key === 'Enter' || event.key === 'Tab') {
        if (bufferRef.current.length >= minLength && elapsed <= maxInterKeyIntervalMs * 4) {
          const captured = bufferRef.current;
          clearBuffer();
          event.preventDefault();
          event.stopPropagation();
          onScanRef.current(captured);
          return;
        }
        clearBuffer();
        return;
      }

      const char = extractScannerChar(event);
      if (char === null) {
        return;
      }

      if (lastKeyTimeRef.current > 0 && elapsed > maxInterKeyIntervalMs) {
        bufferRef.current = '';
      }

      bufferRef.current += char;
      lastKeyTimeRef.current = now;

      if (resetTimerRef.current !== null) {
        window.clearTimeout(resetTimerRef.current);
      }
      resetTimerRef.current = window.setTimeout(() => {
        bufferRef.current = '';
        lastKeyTimeRef.current = 0;
        resetTimerRef.current = null;
      }, Math.max(120, maxInterKeyIntervalMs * 4));
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      clearBuffer();
    };
  }, [enabled, maxInterKeyIntervalMs, minLength]);
}

export default useHardwareScannerWedge;
