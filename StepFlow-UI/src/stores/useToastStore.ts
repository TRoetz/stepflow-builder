import { create } from 'zustand';

export interface ToastItem {
  id: number;
  type: 'success' | 'error';
  message: string;
}

interface ToastState {
  toasts: ToastItem[];
  push: (toast: Omit<ToastItem, 'id'>) => void;
  dismiss: (id: number) => void;
}

let nextId = 1;

/**
 * Global toast queue. Replaces the old App-local `window.__setToast` hack so any
 * component or service can surface feedback without prop drilling. Pushed toasts
 * auto-dismiss after a short delay (longer for errors).
 */
export const useToastStore = create<ToastState>((set, get) => ({
  toasts: [],
  push: (toast) => {
    const id = nextId++;
    // Keep the queue short — drop oldest if more than 3 are pending.
    set((s) => ({ toasts: [...s.toasts.slice(-2), { ...toast, id }] }));
    window.setTimeout(() => get().dismiss(id), toast.type === 'error' ? 5000 : 3000);
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));

/** Imperative helper for services and one-shot handlers. */
export function showToast(toast: Omit<ToastItem, 'id'>): void {
  useToastStore.getState().push(toast);
}
