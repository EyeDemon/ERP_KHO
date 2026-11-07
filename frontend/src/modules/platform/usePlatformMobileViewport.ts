import { useEffect, useState } from 'react';

const checkMaxViewport = (maxWidthPx: number): boolean => {
  if (typeof window === 'undefined') return false;
  if (typeof window.matchMedia === 'function' && window.matchMedia(`(max-width: ${maxWidthPx}px)`).matches) {
    return true;
  }
  return window.innerWidth > 0 && window.innerWidth <= maxWidthPx;
};

export function usePlatformMobileViewport(maxWidthPx = 768): boolean {
  const [isMobile, setIsMobile] = useState<boolean>(() => checkMaxViewport(maxWidthPx));

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const syncViewport = () => setIsMobile(checkMaxViewport(maxWidthPx));
    const mq = typeof window.matchMedia === 'function' ? window.matchMedia(`(max-width: ${maxWidthPx}px)`) : null;
    syncViewport();
    window.addEventListener('resize', syncViewport);
    mq?.addEventListener?.('change', syncViewport);
    return () => {
      window.removeEventListener('resize', syncViewport);
      mq?.removeEventListener?.('change', syncViewport);
    };
  }, [maxWidthPx]);

  return isMobile;
}

export function useIsPlatformMobile768(): boolean {
  return usePlatformMobileViewport(768);
}
