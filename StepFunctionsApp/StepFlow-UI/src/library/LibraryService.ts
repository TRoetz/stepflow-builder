import { StepLibraryEntry, StepCategory } from '@schema-types/schema';
import { schemaById } from '@schemas/index';
import { useStepLibraryStore } from './StepLibraryStore';
import { useNodeStore } from '@stores/useNodeStore';
import { XYPosition } from '@xyflow/react';

/**
 * Service for reusable step library operations.
 * Provides CRUD for templates and instantiation.
 */
export const LibraryService = {
  /**
   * List all available templates, optionally filtered.
   */
  async listTemplates(category?: StepCategory, tags?: string[]): Promise<StepLibraryEntry[]> {
    const store = useStepLibraryStore.getState();
    let templates = store.entries;

    if (category) {
      templates = templates.filter((t) => t.category === category);
    }
    if (tags?.length) {
      templates = templates.filter((t) =>
        tags.some((tag) => t.tags.includes(tag))
      );
    }

    return templates;
  },

  /**
   * Get a template by ID.
   */
  async getTemplate(id: string): Promise<StepLibraryEntry | undefined> {
    return useStepLibraryStore.getState().getTemplate(id);
  },

  /**
   * Save current node config as a new template.
   */
  async saveAsTemplate(
    nodeId: string,
    name: string,
    tags: string[]
  ): Promise<StepLibraryEntry | null> {
    const node = useNodeStore.getState().nodes.find((n) => n.id === nodeId);
    if (!node) return null;

    // Copy the schema's input/output port definitions so forked nodes keep their
    // full handle set (a template saved with empty ports would render a single,
    // unidentifiable handle and lose per-port edge anchoring).
    const schemaDef = node.data?.schemaId ? schemaById.get(node.data.schemaId as string) : undefined;
    const template = useStepLibraryStore.getState().saveAsTemplate({
      schemaId: (node.data?.schemaId as string) ?? 'unknown',
      name,
      description: `Template created from node ${name}`,
      category: (node.data?.category as StepCategory | undefined) ?? schemaDef?.category ?? 'utility',
      version: '1.0.0',
      configuration: { ...(node.data?.configuration || {}) },
      inputs: schemaDef?.inputs ?? [],
      outputs: schemaDef?.outputs ?? [],
      tags,
      isPublished: false,
    });

    return template;
  },

  /**
   * Update an existing template (creates new version).
   */
  async updateTemplate(
    id: string,
    updates: Partial<StepLibraryEntry>
  ): Promise<void> {
    useStepLibraryStore.getState().updateTemplate(id, updates);
  },

  /**
   * Publish/unpublish a template.
   */
  async setPublished(id: string, published: boolean): Promise<void> {
    useStepLibraryStore.getState().updateTemplate(id, { isPublished: published });
  },

  /**
   * Create a node from a template (fork — independent instance).
   */
  async instantiateTemplate(
    templateId: string,
    position: XYPosition
  ): Promise<void> {
    const template = await LibraryService.getTemplate(templateId);
    if (!template) return;

    // Increment usage count
    useStepLibraryStore.getState().incrementUsage(templateId);

    // Create node with template config (forked — independent)
    useNodeStore.getState().addNode(template.schemaId, {
      x: position.x,
      y: position.y,
    });

    // Update the newly created node with template config
    const nodes = useNodeStore.getState().nodes;
    const newNode = nodes[nodes.length - 1];
    if (newNode) {
      useNodeStore.getState().updateNodeData(newNode.id, {
        configuration: { ...template.configuration },
        templateId: templateId,
      });
    }
  },

  /**
   * Search templates by query.
   */
  async searchTemplates(query: string): Promise<StepLibraryEntry[]> {
    return useStepLibraryStore.getState().searchTemplates(query);
  },
};
