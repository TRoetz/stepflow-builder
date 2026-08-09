import { create } from 'zustand';
import { StepLibraryEntry, StepCategory } from '@schema-types/schema';
import { stepLibrary, libraryById } from '@schemas/index';

interface LibraryState {
  entries: StepLibraryEntry[];
  loading: boolean;
  error: string | null;

  // Actions
  loadLibrary: () => void;
  getTemplate: (id: string) => StepLibraryEntry | undefined;
  getTemplatesByCategory: (category: StepCategory) => StepLibraryEntry[];
  getTemplatesByTags: (tags: string[]) => StepLibraryEntry[];
  searchTemplates: (query: string) => StepLibraryEntry[];
  saveAsTemplate: (entry: Omit<StepLibraryEntry, 'id' | 'createdAt' | 'updatedAt' | 'usageCount'>) => StepLibraryEntry;
  updateTemplate: (id: string, updates: Partial<StepLibraryEntry>) => void;
  deleteTemplate: (id: string) => void;
  incrementUsage: (id: string) => void;
}

export const useStepLibraryStore = create<LibraryState>((set, get) => ({
  entries: [],
  loading: false,
  error: null,

  loadLibrary: () => {
    set({ loading: true, error: null });
    try {
      // Load from built-in library + any custom templates from localStorage
      const customTemplates = loadCustomTemplates();
      const allEntries = [...stepLibrary, ...customTemplates];
      set({ entries: allEntries, loading: false });
    } catch (error) {
      set({ error: 'Failed to load library', loading: false });
    }
  },

  getTemplate: (id: string) => {
    return get().entries.find((e) => e.id === id) || libraryById.get(id);
  },

  getTemplatesByCategory: (category: StepCategory) => {
    return get().entries.filter((e) => e.category === category);
  },

  getTemplatesByTags: (tags: string[]) => {
    return get().entries.filter((e) =>
      tags.some((tag) => e.tags.includes(tag))
    );
  },

  searchTemplates: (query: string) => {
    const q = query.toLowerCase();
    return get().entries.filter(
      (e) =>
        e.name.toLowerCase().includes(q) ||
        e.description.toLowerCase().includes(q) ||
        e.tags.some((t) => t.toLowerCase().includes(q))
    );
  },

  saveAsTemplate: (entry: Omit<StepLibraryEntry, 'id' | 'createdAt' | 'updatedAt' | 'usageCount'>) => {
    const now = new Date().toISOString();
    const newTemplate: StepLibraryEntry = {
      ...entry,
      id: `lib:${entry.category}:${entry.name.toLowerCase().replace(/\s+/g, '-')}-${Date.now()}`,
      createdAt: now,
      updatedAt: now,
      usageCount: 0,
    };

    set((state) => ({
      entries: [...state.entries, newTemplate],
    }));

    // Persist custom templates
    saveCustomTemplate(newTemplate);

    return newTemplate;
  },

  updateTemplate: (id: string, updates: Partial<StepLibraryEntry>) => {
    set((state) => ({
      entries: state.entries.map((e) =>
        e.id === id ? { ...e, ...updates, updatedAt: new Date().toISOString() } : e
      ),
    }));
  },

  deleteTemplate: (id: string) => {
    // Only allow deleting custom templates (not built-in)
    set((state) => ({
      entries: state.entries.filter((e) => e.id !== id || !id.startsWith('lib:custom:')),
    }));
  },

  incrementUsage: (id: string) => {
    set((state) => ({
      entries: state.entries.map((e) =>
        e.id === id ? { ...e, usageCount: e.usageCount + 1 } : e
      ),
    }));
  },
}));

// ── Persistence helpers ──
const CUSTOM_TEMPLATES_KEY = 'stepflow-custom-templates';

function loadCustomTemplates(): StepLibraryEntry[] {
  try {
    const saved = localStorage.getItem(CUSTOM_TEMPLATES_KEY);
    return saved ? JSON.parse(saved) : [];
  } catch {
    return [];
  }
}

function saveCustomTemplate(template: StepLibraryEntry) {
  try {
    const templates = loadCustomTemplates();
    templates.push(template);
    localStorage.setItem(CUSTOM_TEMPLATES_KEY, JSON.stringify(templates));
  } catch {
    // ignore storage errors
  }
}
