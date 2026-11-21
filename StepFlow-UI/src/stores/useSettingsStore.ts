import { create } from 'zustand';

const THEME_KEY = 'stepflow-theme'; // read by index.html before first paint to avoid a flash of dark

function initialTheme(): 'dark' | 'light' {
  try { return localStorage.getItem(THEME_KEY) === 'light' ? 'light' : 'dark'; } catch { return 'dark'; }
}

interface SettingsState {
  theme: 'dark' | 'light';
  snapToGrid: boolean;
  snapGridSize: number;
  showHandleLabels: boolean;
  autoSave: boolean;
  autoSaveInterval: number; // seconds

  // Actions
  setTheme: (theme: 'dark' | 'light') => void;
  setSnapToGrid: (enabled: boolean) => void;
  setSnapGridSize: (size: number) => void;
  setShowHandleLabels: (show: boolean) => void;
  setAutoSave: (enabled: boolean) => void;
  setAutoSaveInterval: (seconds: number) => void;
  resetSettings: () => void;
}

const defaultSettings: Omit<SettingsState, 'setTheme' | 'setSnapToGrid' | 'setSnapGridSize' | 'setShowHandleLabels' | 'setAutoSave' | 'setAutoSaveInterval' | 'resetSettings'> = {
  theme: 'dark',
  snapToGrid: true,
  snapGridSize: 16,
  showHandleLabels: true,
  autoSave: true,
  autoSaveInterval: 60,
};

export const useSettingsStore = create<SettingsState>((set) => ({
  ...defaultSettings,
  theme: initialTheme(),

  setTheme: (theme) => {
    try { localStorage.setItem(THEME_KEY, theme); } catch { /* storage unavailable */ }
    set({ theme });
  },
  setSnapToGrid: (snapToGrid) => set({ snapToGrid }),
  setSnapGridSize: (snapGridSize) => set({ snapGridSize }),
  setShowHandleLabels: (showHandleLabels) => set({ showHandleLabels }),
  setAutoSave: (autoSave) => set({ autoSave }),
  setAutoSaveInterval: (autoSaveInterval) => set({ autoSaveInterval }),
  resetSettings: () => set(defaultSettings),
}));
