// @vitest-environment jsdom
import { render, fireEvent, cleanup } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AccessibleDialog from './AccessibleDialog';

describe('AccessibleDialog',()=>{
  afterEach(cleanup);
  it('traps focus, closes with Escape, and restores trigger focus',()=>{
    const trigger=document.createElement('button');document.body.append(trigger);trigger.focus();
    const close=vi.fn();
    const {getByRole,unmount}=render(<AccessibleDialog titleId="title" onClose={close} returnFocusTo={trigger}><h2 id="title">Title</h2><button data-initial-focus>First</button><button>Last</button></AccessibleDialog>);
    const buttons=getByRole('dialog').querySelectorAll('button');
    expect(document.activeElement).toBe(buttons[0]);
    buttons[1].focus();fireEvent.keyDown(document,{key:'Tab'});expect(document.activeElement).toBe(buttons[0]);
    fireEvent.keyDown(document,{key:'Tab',shiftKey:true});expect(document.activeElement).toBe(buttons[1]);
    fireEvent.keyDown(document,{key:'Escape'});expect(close).toHaveBeenCalledOnce();
    unmount();expect(document.activeElement).toBe(trigger);trigger.remove();
  });
  it('restores each background inert state on close and direct unmount',()=>{
    const active=document.createElement('button'), alreadyInert=document.createElement('section');
    active.inert=false;alreadyInert.inert=true;document.body.append(active,alreadyInert);
    const view=render(<AccessibleDialog titleId="inert-title" onClose={()=>{}}><h2 id="inert-title">Title</h2><button>Close</button></AccessibleDialog>);
    expect(active.inert).toBe(true);expect(alreadyInert.inert).toBe(true);
    expect((view.getByRole('dialog').closest('.approval-modal') as HTMLElement | null)?.inert).not.toBe(true);
    view.unmount();
    expect(active.inert).toBe(false);expect(alreadyInert.inert).toBe(true);expect(document.querySelector('[role="dialog"]')).toBeNull();
    active.remove();alreadyInert.remove();
  });
  it('cleans listeners, portals, inert state, and focus over three open-close cycles',()=>{
    const trigger=document.createElement('button'),background=document.createElement('main');document.body.append(trigger,background);
    const close=vi.fn();
    for(let cycle=1;cycle<=3;cycle++){
      trigger.focus();
      const view=render(<AccessibleDialog titleId={`cycle-${cycle}`} onClose={close} returnFocusTo={trigger}><h2 id={`cycle-${cycle}`}>Cycle</h2><button data-initial-focus>First</button><button>Last</button></AccessibleDialog>);
      expect(document.activeElement).toBe(view.getByText('First'));expect(background.inert).toBe(true);
      fireEvent.keyDown(document,{key:'Escape'});expect(close).toHaveBeenCalledTimes(cycle);
      view.unmount();expect(document.activeElement).toBe(trigger);expect(background.inert).toBeFalsy();expect(document.querySelector('[role="dialog"]')).toBeNull();
    }
    trigger.remove();background.remove();
  });
  it('restores inert state and listeners when unmounted while busy',()=>{
    const background=document.createElement('main');document.body.append(background);const close=vi.fn();
    const view=render(<AccessibleDialog titleId="busy-title" busy onClose={close}><h2 id="busy-title">Busy</h2><button>Wait</button></AccessibleDialog>);
    fireEvent.keyDown(document,{key:'Escape'});expect(close).not.toHaveBeenCalled();expect(background.inert).toBe(true);
    view.unmount();expect(background.inert).toBeFalsy();fireEvent.keyDown(document,{key:'Escape'});expect(close).not.toHaveBeenCalled();background.remove();
  });
});
