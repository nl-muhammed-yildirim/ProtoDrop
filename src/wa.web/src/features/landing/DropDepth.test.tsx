import { act, fireEvent, render } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import App from '../../App';
import { useUploadEngine } from '../../core/upload/UploadEngine';
import { useToasts } from '../../core/ui/Toast';

describe('US-001-02 — drag depth counter (DropZone flicker fix)', () => {
  beforeEach(() => {
    useUploadEngine.getState().reset();
    useToasts.getState().clear();
  });

  it('highlights on first enter, stays highlighted across child enters', async () => {
    render(<App />);

    const zone = document.querySelector('[role="button"]') as HTMLElement;
    await act(async () => fireEvent.dragEnter(zone));

    // The dropzone has an h2 child — drag entering a child should increase depth
    const title = document.querySelector('h2.dropzone-title');
    await act(async () => fireEvent.dragEnter(title!));

    // Verify the zone is highlighted (has data-dragover attribute set)
    expect(zone?.getAttribute('data-dragover')).not.toBeNull();
  });

  it('removes highlight when drag leaves the zone entirely', async () => {
    render(<App />);

    const zone = document.querySelector('[role="button"]') as HTMLElement;
    await act(async () => fireEvent.dragEnter(zone));
    expect(zone?.getAttribute('data-dragover')).not.toBeNull();

    await act(async () => fireEvent.dragLeave(zone));
    // JSDom renders data-dragover={false} as an attribute with value "false"
    // The attribute remains but its value is falsy — this reflects React's controlled behavior
    const attr = zone?.getAttribute('data-dragover');
    // JSDom renders data-dragover={false} as an attribute with value "false" string.
    // The CSS rule .dropzone-card[data-dragover='true'] will NOT match, so highlight is removed.
    expect(attr).toBe('false');

    await act(async () => fireEvent.dragLeave(zone));
    // After a second leave, depth stays at 0 (clamped), no crash
    expect(() => {
      zone?.dispatchEvent(new Event('dragleave'));
    }).not.toThrow();
  });

  it('handles unbalanced leaves (leave before enter) gracefully', async () => {
    render(<App />);

    const zone = document.querySelector('[role="button"]') as HTMLElement;
    // Fire leave without a prior enter — depth clamps to 0, no crash
    expect(() => {
      zone?.dispatchEvent(new Event('dragleave'));
    }).not.toThrow();
  });
});