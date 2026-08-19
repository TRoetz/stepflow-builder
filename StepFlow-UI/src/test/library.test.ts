import { describe, it, expect, beforeEach } from 'vitest';
import { useStepLibraryStore } from '@library/StepLibraryStore';
import { LibraryService } from '@library/LibraryService';

describe('Step Library Store', () => {
  beforeEach(() => {
    useStepLibraryStore.setState({
      entries: [],
      loading: false,
      error: null,
    });
  });

  it('should load library', () => {
    useStepLibraryStore.getState().loadLibrary();
    const { entries } = useStepLibraryStore.getState();
    expect(entries.length).toBeGreaterThan(0);
  });

  it('should get template by ID', () => {
    useStepLibraryStore.getState().loadLibrary();
    const first = useStepLibraryStore.getState().entries[0];
    const found = useStepLibraryStore.getState().getTemplate(first.id);
    expect(found).toBeDefined();
    expect(found?.id).toBe(first.id);
  });

  it('should filter by category', () => {
    useStepLibraryStore.getState().loadLibrary();
    const aiTemplates = useStepLibraryStore.getState().getTemplatesByCategory('ai');
    expect(aiTemplates.length).toBeGreaterThan(0);
    expect(aiTemplates.every((t) => t.category === 'ai')).toBe(true);
  });

  it('should save as template', () => {
    const template = useStepLibraryStore.getState().saveAsTemplate({
      schemaId: 'stepflow:utility:pass',
      name: 'My Custom Pass',
      description: 'A custom pass template',
      category: 'utility',
      version: '1.0.0',
      configuration: {},
      inputs: [],
      outputs: [],
      tags: ['custom'],
      isPublished: false,
    });
    expect(template.id).toBeTruthy();
    expect(template.name).toBe('My Custom Pass');
  });

  it('should increment usage count', () => {
    useStepLibraryStore.getState().loadLibrary();
    const first = useStepLibraryStore.getState().entries[0];
    const before = first.usageCount;
    useStepLibraryStore.getState().incrementUsage(first.id);
    const after = useStepLibraryStore.getState().getTemplate(first.id);
    expect(after?.usageCount).toBe(before + 1);
  });

  it('should update template', () => {
    useStepLibraryStore.getState().loadLibrary();
    const first = useStepLibraryStore.getState().entries[0];
    useStepLibraryStore.getState().updateTemplate(first.id, { name: 'Updated Name' });
    const updated = useStepLibraryStore.getState().getTemplate(first.id);
    expect(updated?.name).toBe('Updated Name');
  });
});

describe('LibraryService', () => {
  beforeEach(() => {
    useStepLibraryStore.setState({
      entries: [],
      loading: false,
      error: null,
    });
  });

  it('should list templates', async () => {
    useStepLibraryStore.getState().loadLibrary();
    const templates = await LibraryService.listTemplates();
    expect(templates.length).toBeGreaterThan(0);
  });

  it('should list templates by category', async () => {
    useStepLibraryStore.getState().loadLibrary();
    const aiTemplates = await LibraryService.listTemplates('ai');
    expect(aiTemplates.every((t) => t.category === 'ai')).toBe(true);
  });

  it('should get template by ID', async () => {
    useStepLibraryStore.getState().loadLibrary();
    const first = useStepLibraryStore.getState().entries[0];
    const found = await LibraryService.getTemplate(first.id);
    expect(found).toBeDefined();
    expect(found?.id).toBe(first.id);
  });
});
