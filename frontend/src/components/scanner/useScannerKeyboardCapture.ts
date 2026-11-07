import { useEffect, useRef, type MutableRefObject, type RefObject } from 'react';
import { sanitizeScannerCode } from '../../pages/productCatalog';
import {
  extractScannerChar,
  isScannerStationVisible,
  parseGs1BarcodeClient,
} from './scannerAudioAndGs1';
import type { ParsedGs1Data } from './scannerTypes';

interface UseScannerKeyboardCaptureOptions {
  disabled: boolean;
  sectionRef: RefObject<HTMLElement | null>;
  inputRef: RefObject<HTMLInputElement | null>;
  bufferRef: MutableRefObject<string>;
  inputValueRef: MutableRefObject<string>;
  triggerRef: MutableRefObject<(rawCode: string) => Promise<void>>;
  setInput: (val: string) => void;
  setParsedGs1: (val: ParsedGs1Data) => void;
}

export function useScannerKeyboardCapture({
  disabled,
  sectionRef,
  inputRef,
  bufferRef,
  inputValueRef,
  triggerRef,
  setInput,
  setParsedGs1,
}: UseScannerKeyboardCaptureOptions) {
  const lastKeyTimeRef = useRef(0);
  const lastHandledKeydownTimeRef = useRef(0);

  useEffect(() => {
    if (disabled) return;
    const processScannerKeyEvent = (e: KeyboardEvent, isKeypressFallback: boolean) => {
      if (!isScannerStationVisible(sectionRef.current)) return;
      if (e.isTrusted && (e.ctrlKey || e.altKey || e.metaKey)) return;

      const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
      const deltaMs = now - lastKeyTimeRef.current;
      const isRapidHidScan = lastKeyTimeRef.current > 0 && deltaMs <= 80;
      const activeEl = (typeof document !== 'undefined' ? document.activeElement : null) as HTMLElement | null;
      const targetEl = (e.target as HTMLElement | null) ?? activeEl;
      const isScannerInput = targetEl === inputRef.current || activeEl === inputRef.current;
      const isOtherEditableField =
        !isScannerInput &&
        Boolean(
          targetEl &&
          (targetEl.tagName === 'INPUT' ||
            targetEl.tagName === 'TEXTAREA' ||
            targetEl.tagName === 'SELECT' ||
            targetEl.isContentEditable),
        );

      if (e.isTrusted && isOtherEditableField && !isRapidHidScan) return;

      const isEnter = e.key === 'Enter' || e.code === 'Enter' || e.keyCode === 13 || e.which === 13 || e.charCode === 13;
      if (isEnter) {
        lastKeyTimeRef.current = now;
        if (!isKeypressFallback) lastHandledKeydownTimeRef.current = now;
        const rawCandidate = bufferRef.current || inputValueRef.current;
        const candidate = sanitizeScannerCode(rawCandidate);
        if (candidate) {
          e.preventDefault();
          e.stopPropagation();
          bufferRef.current = '';
          inputValueRef.current = '';
          setInput('');
          void triggerRef.current(rawCandidate);
        }
        return;
      }

      const ch = extractScannerChar(e);
      if (!ch) return;
      lastKeyTimeRef.current = now;
      if (!isKeypressFallback) lastHandledKeydownTimeRef.current = now;

      if (!e.isTrusted || !isScannerInput || isRapidHidScan) {
        bufferRef.current += ch;
        inputValueRef.current = bufferRef.current;
        setInput(bufferRef.current);
        const decoded = parseGs1BarcodeClient(bufferRef.current);
        if (decoded) setParsedGs1(decoded);
      }
    };

    const onWindowKeyDown = (e: KeyboardEvent) => processScannerKeyEvent(e, false);
    const onWindowKeyPress = (e: KeyboardEvent) => {
      const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
      if (now - lastHandledKeydownTimeRef.current < 25) return;
      processScannerKeyEvent(e, true);
    };

    window.addEventListener('keydown', onWindowKeyDown);
    window.addEventListener('keypress', onWindowKeyPress);
    return () => {
      window.removeEventListener('keydown', onWindowKeyDown);
      window.removeEventListener('keypress', onWindowKeyPress);
    };
  }, [disabled, sectionRef, inputRef, bufferRef, inputValueRef, triggerRef, setInput, setParsedGs1]);
}
