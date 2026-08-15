import { useState, useMemo, useCallback } from 'react';
import { paletteData, schemaById } from '@schemas/index';
import { flowTemplates } from '@schemas/templates';
import { StepCategory } from '@schema-types/schema';

interface NodePaletteProps {
  onNodeAdd: (schemaId: string) => void;
  onInstantiateTemplate: (templateId: string) => void;
}

export function NodePalette({ onNodeAdd, onInstantiateTemplate }: NodePaletteProps) {
  const [searchQuery, setSearchQuery] = useState('');
  const [templatesCollapsed, setTemplatesCollapsed] = useState(false);
  const [collapsedCategories, setCollapsedCategories] = useState<Set<StepCategory>>(new Set());
  const [favorites, setFavorites] = useState<Set<string>>(() => {
    try {
      const saved = localStorage.getItem('palette-favorites');
      return saved ? new Set(JSON.parse(saved)) : new Set<string>();
    } catch {
      return new Set<string>();
    }
  });

  // Toggle category collapse
  const toggleCategory = useCallback((category: StepCategory) => {
    setCollapsedCategories((prev) => {
      const next = new Set(prev);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return next;
    });
  }, []);

  // Toggle favorite
  const toggleFavorite = useCallback((schemaId: string) => {
    setFavorites((prev) => {
      const next = new Set(prev);
      if (next.has(schemaId)) {
        next.delete(schemaId);
      } else {
        next.add(schemaId);
      }
      // Persist to localStorage
      try {
        localStorage.setItem('palette-favorites', JSON.stringify([...next]));
      } catch {
        // ignore
      }
      return next;
    });
  }, []);

  // Filter categories by search query
  const filteredPalette = useMemo(() => {
    if (!searchQuery.trim()) return paletteData;

    const query = searchQuery.toLowerCase();
    return paletteData
      .map((category) => ({
        ...category,
        items: category.items.filter(
          (item) =>
            item.name.toLowerCase().includes(query) ||
            item.tags.some((tag) => tag.toLowerCase().includes(query)) ||
            item.description?.toLowerCase().includes(query)
        ),
      }))
      .filter((category) => category.items.length > 0);
  }, [searchQuery]);

  // Build favorites list
  const favoriteItems = useMemo(() => {
    return paletteData
      .flatMap((category) => category.items)
      .filter((item) => favorites.has(item.id));
  }, [favorites]);

  // Templates filtered by the current search query
  const visibleTemplates = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return flowTemplates;
    return flowTemplates.filter(
      (t) => t.name.toLowerCase().includes(q) || t.description.toLowerCase().includes(q)
    );
  }, [searchQuery]);

  // Handle node add (drag or double-click)
  const handleNodeAdd = useCallback(
    (schemaId: string) => {
      onNodeAdd(schemaId);
    },
    [onNodeAdd]
  );

  // Handle drag start
  const handleDragStart = useCallback(
    (event: React.DragEvent, schemaId: string) => {
      event.dataTransfer.setData('application/stepflow-schema', schemaId);
      event.dataTransfer.effectAllowed = 'move';
    },
    []
  );

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-200">States Palette</h2>
        <p className="text-xs text-gray-500 mt-0.5">
          Drag states onto the canvas or double-click to add
        </p>
      </div>

      {/* Search */}
      <div className="px-3 py-2 border-b border-gray-800">
        <input
          type="text"
          placeholder="Search steps..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
        />
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto">
        {/* Starter Templates — collapsible like node categories, list scrolls internally */}
        {visibleTemplates.length > 0 && (
          <div className="border-b border-gray-800/50">
            <button
              onClick={() => setTemplatesCollapsed((v) => !v)}
              aria-expanded={!templatesCollapsed}
              title={templatesCollapsed ? 'Show starter templates' : 'Hide starter templates'}
              className="w-full flex items-center justify-between px-4 py-2 text-xs font-semibold uppercase tracking-wider text-gray-400 hover:text-gray-200 transition-colors"
            >
              <span className="flex items-center gap-2">
                <span>📋</span>
                <span>Starter Templates</span>
                <span className="text-xs font-normal text-gray-600">
                  ({visibleTemplates.length})
                </span>
              </span>
              <span className="text-gray-600">{templatesCollapsed ? '▸' : '▾'}</span>
            </button>

            {!templatesCollapsed && (
              <div className="px-2 pb-2">
                <p className="text-[11px] text-gray-500 px-1.5 py-1">
                  Instantiates a pre-built flow (replaces the current canvas)
                </p>
                {/* Bounded, internally scrollable list so many templates don't push nodes out of view */}
                <div className="max-h-[320px] overflow-y-auto flex flex-col gap-1.5 pr-1">
                  {visibleTemplates.map((t) => (
                    <button
                      key={t.id}
                      onClick={() => onInstantiateTemplate(t.id)}
                      title="Click to instantiate this template"
                      className="w-full text-left px-3 py-2.5 rounded-lg border border-gray-700/60 hover:border-indigo-500/60 hover:bg-indigo-500/10 transition-all shrink-0"
                    >
                      <div className="flex items-center gap-2">
                        <span>{t.icon}</span>
                        <span className="text-sm font-medium text-gray-200 flex-1 truncate">{t.name}</span>
                        {t.iteratorBody && (
                          <span className="text-[10px] px-1.5 py-0.5 rounded bg-indigo-500/20 text-indigo-300 shrink-0">
                            + iterator flow
                          </span>
                        )}
                      </div>
                      <p className="text-xs text-gray-500 mt-1 leading-snug">{t.description}</p>
                      <p className="text-[10px] text-gray-600 mt-1">
                        {t.mainFlow.nodes.length} steps · {t.mainFlow.edges.length} connections
                      </p>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {/* Favorites Section */}
        {favoriteItems.length > 0 && !searchQuery && (
          <div className="px-3 py-2">
            <div className="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-2">
              ⭐ Favorites
            </div>
            <div className="grid grid-cols-2 gap-1">
              {favoriteItems.map((item) => (
                <PaletteItem
                  key={item.id}
                  item={item}
                  isFavorite={true}
                  onAdd={() => handleNodeAdd(item.id)}
                  onToggleFavorite={() => toggleFavorite(item.id)}
                  onDragStart={handleDragStart}
                />
              ))}
            </div>
            <div className="border-t border-gray-800 my-3" />
          </div>
        )}

        {/* Categories */}
        {filteredPalette.map((category) => (
          <PaletteCategory
            key={category.id}
            category={category}
            isCollapsed={collapsedCategories.has(category.id)}
            onToggle={() => toggleCategory(category.id)}
            onNodeAdd={handleNodeAdd}
            onToggleFavorite={toggleFavorite}
            onDragStart={handleDragStart}
            favorites={favorites}
          />
        ))}

        {filteredPalette.length === 0 && (
          <div className="px-4 py-8 text-center text-sm text-gray-500">
            No steps match "{searchQuery}"
          </div>
        )}
      </div>
    </div>
  );
}

// ── Sub-components ──

interface PaletteCategoryProps {
  category: { id: StepCategory; name: string; icon: string; color: string; items: PaletteItemData[] };
  isCollapsed: boolean;
  onToggle: () => void;
  onNodeAdd: (schemaId: string) => void;
  onToggleFavorite: (schemaId: string) => void;
  onDragStart: (event: React.DragEvent, schemaId: string) => void;
  favorites: Set<string>;
}

interface PaletteItemData {
  id: string;
  name: string;
  schemaId: string;
  color: string;
  description?: string;
  tags: string[];
}

function PaletteCategory({
  category,
  isCollapsed,
  onToggle,
  onNodeAdd,
  onToggleFavorite,
  onDragStart,
  favorites,
}: PaletteCategoryProps) {
  return (
    <div className="border-b border-gray-800/50">
      <button
        onClick={onToggle}
        className="w-full flex items-center justify-between px-4 py-2 text-xs font-semibold uppercase tracking-wider text-gray-400 hover:text-gray-200 transition-colors"
      >
        <span className="flex items-center gap-2">
          <span>{category.icon}</span>
          <span>{category.name}</span>
          <span className="text-xs font-normal text-gray-600">
            ({category.items.length})
          </span>
        </span>
        <span className="text-gray-600">{isCollapsed ? '▸' : '▾'}</span>
      </button>

      {!isCollapsed && (
        <div className="px-2 pb-2 grid grid-cols-1 gap-1">
          {category.items.map((item) => (
            <PaletteItem
              key={item.id}
              item={item}
              isFavorite={favorites.has(item.id)}
              onAdd={() => onNodeAdd(item.id)}
              onToggleFavorite={() => onToggleFavorite(item.id)}
              onDragStart={onDragStart}
            />
          ))}
        </div>
      )}
    </div>
  );
}

interface PaletteItemProps {
  item: PaletteItemData;
  isFavorite: boolean;
  onAdd: () => void;
  onToggleFavorite: () => void;
  onDragStart: (event: React.DragEvent, schemaId: string) => void;
}

function PaletteItem({ item, isFavorite, onAdd, onToggleFavorite, onDragStart }: PaletteItemProps) {
  const schema = schemaById.get(item.id);

  return (
    <div
      draggable
      onDragStart={(e) => onDragStart(e, item.id)}
      onDoubleClick={onAdd}
      className="flex items-center gap-3 px-3 py-2.5 rounded-lg cursor-grab text-sm transition-all hover:bg-gray-700/60 active:cursor-grabbing active:bg-gray-600/60 group border border-transparent hover:border-gray-600/50"
      title={`${schema?.description || ''}\n\nDouble-click to add`}
    >
      {/* Drag handle indicator */}
      <div className="text-gray-600 opacity-0 group-hover:opacity-100 transition-opacity shrink-0">
        <svg width="10" height="14" viewBox="0 0 10 14" fill="none" xmlns="http://www.w3.org/2000/svg" className="opacity-60">
          <circle cx="3" cy="2" r="1.5" fill="currentColor"/>
          <circle cx="7" cy="2" r="1.5" fill="currentColor"/>
          <circle cx="3" cy="7" r="1.5" fill="currentColor"/>
          <circle cx="7" cy="7" r="1.5" fill="currentColor"/>
          <circle cx="3" cy="12" r="1.5" fill="currentColor"/>
          <circle cx="7" cy="12" r="1.5" fill="currentColor"/>
        </svg>
      </div>

      <div
        className="w-2.5 h-2.5 rounded-full shrink-0"
        style={{ backgroundColor: item.color, boxShadow: `0 0 6px ${item.color}40` }}
      />
      <span className="flex-1 text-gray-300 truncate font-medium">{item.name}</span>
      <button
        onClick={(e) => {
          e.stopPropagation();
          onToggleFavorite();
        }}
        className={`text-xs shrink-0 ${
          isFavorite ? 'text-yellow-400' : 'text-gray-600 opacity-0 group-hover:opacity-100'
        } transition-all`}
        title={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
      >
        {isFavorite ? '★' : '☆'}
      </button>
    </div>
  );
}
