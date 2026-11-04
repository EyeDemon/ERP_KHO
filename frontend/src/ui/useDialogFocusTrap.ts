import { useEffect, useRef, type RefObject } from 'react';

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"]):not(.sr-only)',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'summary',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    (el) => !el.hasAttribute('disabled') && el.getAttribute('aria-hidden') !== 'true',
  );
}

export interface DialogFocusTrapOptions {
  open: boolean;
  onEscape?: () => void;
  escapeDisabled?: boolean;
  returnFocusTo?: HTMLElement | null;
}

/**
 * WCAG 2.2 AA dialog/drawer keyboard focus trap & Escape focus restoration hook.
 * - Captures the trigger element (activeElement) before moving focus inside the modal.
 * - Cycles Tab and Shift+Tab strictly inside the dialog container.
 * - Closes on Escape (unless escapeDisabled) and restores focus to the trigger element on close.
 */
export function useDialogFocusTrap(
  containerRef: RefObject<HTMLElement | null>,
  options: DialogFocusTrapOptions,
): void {
  const { open, onEscape, escapeDisabled = false, returnFocusTo } = options;
  const onEscapeRef = useRef(onEscape);
  const escapeDisabledRef = useRef(escapeDisabled);
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const wasOpenRef = useRef(false);

  if (open && !wasOpenRef.current) {
    previousFocusRef.current =
      returnFocusTo ??
      (typeof document !== 'undefined' && document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null);
  }
  wasOpenRef.current = open;

  useEffect(() => {
    onEscapeRef.current = onEscape;
    escapeDisabledRef.current = escapeDisabled;
  }, [onEscape, escapeDisabled]);

  useEffect(() => {
    if (!open) return undefined;

    const container = containerRef.current;
    if (returnFocusTo) {
      previousFocusRef.current = returnFocusTo;
    } else if (
      (!previousFocusRef.current || container?.contains(previousFocusRef.current)) &&
      typeof document !== 'undefined' &&
      document.activeElement instanceof HTMLElement &&
      !container?.contains(document.activeElement)
    ) {
      previousFocusRef.current = document.activeElement;
    }

    if (container && !container.contains(document.activeElement)) {
      const preferred =
        container.querySelector<HTMLElement>('[data-initial-focus], [autofocus]') ??
        getFocusableElements(container)[0] ??
        container;
      preferred.focus();
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      const dialogEl = containerRef.current;
      if (!dialogEl) return;

      if (event.key === 'Escape') {
        if (!escapeDisabledRef.current && onEscapeRef.current) {
          event.preventDefault();
          event.stopPropagation();
          onEscapeRef.current();
        }
        return;
      }

      if (event.key !== 'Tab') return;

      const activeScope =
        dialogEl.querySelector<HTMLElement>('[data-focus-scope="true"], .approval-dialog') ?? dialogEl;
      const focusable = getFocusableElements(activeScope);
      if (focusable.length === 0) {
        event.preventDefault();
        activeScope.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement as HTMLElement | null;

      if (event.shiftKey) {
        if (!active || active === first || !activeScope.contains(active)) {
          event.preventDefault();
          last.focus();
        }
      } else if (!active || active === last || !activeScope.contains(active)) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown, true);
    return () => {
      document.removeEventListener('keydown', handleKeyDown, true);
      const target = returnFocusTo ?? previousFocusRef.current;
      if (target && target.isConnected && typeof target.focus === 'function') {
        target.focus();
      }
    };
  }, [open, containerRef, returnFocusTo]);
}
