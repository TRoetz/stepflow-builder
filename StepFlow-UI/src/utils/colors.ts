import { DataType, StepCategory } from '@schema-types/schema';
import { categoryColorById } from '@schemas/categories';

/**
 * Category accent colors.
 */
export const CATEGORY_COLORS: Record<StepCategory, string> = {
  terminal: '#10B981',
  flow: '#8B5CF6',
  ai: '#8B5CF6',
  rule: '#F59E0B',
  data: '#3B82F6',
  api: '#10B981',
  transform: '#EC4899',
  utility: '#6B7280',
  subflow: '#06B6D4',
  human: '#FB923C',
  formcapture: '#22C55E',
  remote: '#F59E0B',
  transfer: '#0EA5E9',
};

/**
 * Data type colors for handle styling.
 */
export const DATA_TYPE_COLORS: Record<DataType, string> = {
  json: '#8b5cf6',
  string: '#60a599',
  number: '#f4d03f',
  boolean: '#e74c3c',
  array: '#3498db',
  image: '#e67e22',
  any: '#95a5a6',
};

/**
 * Get the accent color for a category.
 */
export function getCategoryColor(category: StepCategory): string {
  return categoryColorById.get(category) || CATEGORY_COLORS[category] || '#6366f1';
}

/**
 * Get the color for a data type (used for handles).
 */
export function getDataTypeColor(type: DataType): string {
  return DATA_TYPE_COLORS[type] || '#95a5a6';
}

/**
 * Get a lighter/darker variant of a color.
 */
export function adjustColor(hex: string, amount: number): string {
  const num = parseInt(hex.replace('#', ''), 16);
  const r = Math.min(255, Math.max(0, (num >> 16) + amount));
  const g = Math.min(255, Math.max(0, ((num >> 8) & 0x00ff) + amount));
  const b = Math.min(255, Math.max(0, (num & 0x0000ff) + amount));
  return `#${(r << 16 | g << 8 | b).toString(16).padStart(6, '0')}`;
}

/**
 * Get RGBA color string from hex + alpha.
 */
export function hexToRgba(hex: string, alpha: number): string {
  const num = parseInt(hex.replace('#', ''), 16);
  const r = num >> 16;
  const g = (num >> 8) & 0x00ff;
  const b = num & 0x0000ff;
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
