import { useEffect } from 'react';
import { create } from 'zustand';

// UI-Reference §4.3 — toasts: top-center, auto-dismiss 5 s, role status/alert,
// left border in kind color (styles in styles/landing.css).

export type ToastKind = 'info' | 'warning' | 'error';

interface ToastItem {
  id: number;
  kind: ToastKind;
  message: string;
}

const AUTO_DISMISS_MS = 5000; // UI-Reference §4.3

let nextId = 1;

interface ToastApi {
  toasts: ToastItem[];
  push(kind: ToastKind, message: string): void;
  dismiss(id: number): void;
  clear(): void; // test isolation (UploadEngine.reset() equivalent)
}

// eslint-disable-next-line react-refresh/only-export-components -- store + Toaster ship together (UI-Reference 4.3)
export const useToasts = create<ToastApi>()((set) => ({
  toasts: [],
  push(kind, message) {
    set((state) => ({
      toasts: [...state.toasts, { id: nextId++, kind, message }],
    }));
  },
  dismiss(id) {
    set((state) => ({ toasts: state.toasts.filter((toast) => toast.id !== id) }));
  },
  clear() {
    set({ toasts: [] });
  },
}));

/** Module-level push for non-React call sites (e.g. DropZone handlers). */
// eslint-disable-next-line react-refresh/only-export-components -- non-React call sites need a module-level push
export const showToast = (kind: ToastKind, message: string): void => {
  useToasts.getState().push(kind, message);
};

function ToastCard({ id, kind, message }: ToastItem) {
  const dismiss = useToasts((state) => state.dismiss);

  useEffect(() => {
    const handle = window.setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
    return () => window.clearTimeout(handle);
  }, [id, dismiss]);

  return (
    <div className={`toast toast-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      {message}
    </div>
  );
}

export function Toaster() {
  const toasts = useToasts((state) => state.toasts);
  if (toasts.length === 0) return null;
  return (
    <div className="toast-container">
      {toasts.map((toast) => (
        <ToastCard key={toast.id} {...toast} />
      ))}
    </div>
  );
}
