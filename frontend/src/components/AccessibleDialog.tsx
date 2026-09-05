import { useEffect, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

type Props = { titleId:string; busy?:boolean; onClose:()=>void; returnFocusTo?:HTMLElement|null; children:ReactNode };

export default function AccessibleDialog({titleId,busy=false,onClose,returnFocusTo,children}:Props) {
  const dialog=useRef<HTMLDivElement>(null);
  const options=useRef({busy,onClose});
  useEffect(()=>{options.current={busy,onClose}},[busy,onClose]);
  useEffect(()=>{
    const root=dialog.current;
    const siblings=Array.from(document.body.children).filter(x=>!x.contains(root)) as HTMLElement[];
    const previous=siblings.map(x=>x.inert);
    siblings.forEach(x=>{x.inert=true});
    const focusable=()=>Array.from(root?.querySelectorAll<HTMLElement>('button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])')??[]);
    (root?.querySelector<HTMLElement>('[data-initial-focus]')??focusable()[0])?.focus();
    const key=(event:KeyboardEvent)=>{
      if(event.key==='Escape'){event.preventDefault();if(!options.current.busy)options.current.onClose();return;}
      if(event.key!=='Tab')return;
      const items=focusable();if(!items.length){event.preventDefault();root?.focus();return;}
      const first=items[0],last=items[items.length-1];
      if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus();}
      else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}
    };
    document.addEventListener('keydown',key);
    const contain=(event:FocusEvent)=>{if(!root?.contains(event.target as Node))(focusable()[0]??root)?.focus()};
    document.addEventListener('focusin',contain);
    return()=>{document.removeEventListener('keydown',key);document.removeEventListener('focusin',contain);siblings.forEach((x,i)=>{x.inert=previous[i]});returnFocusTo?.focus();};
  },[returnFocusTo]);
  return createPortal(<div className="approval-modal" role="presentation"><div ref={dialog} tabIndex={-1} className="approval-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>{children}</div></div>,document.body);
}
