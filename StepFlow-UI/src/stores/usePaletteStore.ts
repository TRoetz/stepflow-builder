import { create } from 'zustand';
import { StepCategory } from '@schema-types/schema';

interface PaletteState {
  searchQuery: string;
  collapsedCategories: Set<StepCategory>;
  collapsed: boolean;

  // Actions
  setSearchQuery: (query: string) => void;
  toggleCategory: (category: StepCategory) => void;
  toggleCollapsed: () => void;
  resetCollapsed: () => void;
}

export const usePaletteStore = create<PaletteState>((set) => ({
  searchQuery: '',
  collapsedCategories: new Set<StepCategory>(),
  collapsed: false,

  setSearchQuery: (query: string) => set({ searchQuery: query }),
  toggleCategory: (category: StepCategory) =>
    set((state) => {
      const next = new Set(state.collapsedCategories);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return { collapsedCategories: next };
    }),
  toggleCollapsed: () => set((state) => ({ collapsed: !state.collapsed })),
  resetCollapsed: () => set({ collapsedCategories: new Set() }),
}));
