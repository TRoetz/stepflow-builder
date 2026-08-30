import { useState, useCallback, useEffect } from 'react';
import Editor from '@monaco-editor/react';
import { Plus, Trash2, Save, FileJson, AlertCircle, ArrowLeft, Braces, Rocket, Sparkles, X } from 'lucide-react';
import {
  FormService,
  ATTRIBUTE_DATA_TYPES,
  type FormDefinition,
  type AttributeDomainEntry,
  type EntityAttributeData,
  type SchemaDefinitionData,
} from '@services/formService';
import { FlowService } from '@services/flowService';
import type { AiFormPlan } from '@services/aiFormBuilder';
import { useAutoLayout } from '@hooks/useAutoLayout';
import { AiFormBuildModal } from './AiFormBuildModal';
import { FormRenderer } from './FormRenderer';
import { showToast } from '@stores/useToastStore';

// ═══════════════════════════════════════════════════════════
// FormBuilderWindow — full-screen workspace for versioned UIData forms,
// attribute domains and the schema-definition registry. Replaces the old
// 720px side panel (FormBuilderPanel is retired).
// ═══════════════════════════════════════════════════════════

type Tab = 'forms' | 'domains' | 'schemas';

const NEW_PAGE_TEMPLATE = JSON.stringify(
  {
    Id: 1,
    Title: 'New Form',
    PageName: 1,
    PageVersion: '1.0',
    RootElements: [
      { Id: 10, Type: 'TextBox', Label: 'Name', Attribute: 'Name' },
      { Id: 11, Type: 'Dropdown', Label: 'Status', Attribute: 'Status', Children: [] }
    ]
  },
  null,
  2
);

const NEW_DOMAIN_TEMPLATE: AttributeDomainEntry = {
  attributeDomain: {
    version: '1.0',
    attributeDomainName: '',
    description: '',
    isCurrentVersion: true,
    attributes: []
  }
};

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** "formId::version" — stable selection key across the version list. */
const formKey = (formId: string, version: string) => `${formId}::${version}`;
const schemaKey = (name: string, version: string) => `${name}::${version}`;

/** Largest integer version label in a set (non-numeric labels are ignored). */
function maxVersionInt(versions: string[]): number {
  let max = 0;
  for (const v of versions) {
    const n = parseInt(v, 10);
    if (!Number.isNaN(n) && n > max) max = n;
  }
  return max;
}

interface FormGroup {
  formId: string;
  title: string;
  attributeDomainName?: string | null;
  versions: FormDefinition[]; // sorted by numeric version label
  current?: FormDefinition;
}

function groupForms(forms: FormDefinition[]): FormGroup[] {
  const map = new Map<string, FormDefinition[]>();
  for (const f of forms) {
    const list = map.get(f.formId) ?? [];
    list.push(f);
    map.set(f.formId, list);
  }
  return [...map.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([formId, versions]) => {
      const sorted = [...versions].sort(
        (x, y) => maxVersionInt([x.version]) - maxVersionInt([y.version]) || x.version.localeCompare(y.version)
      );
      const current = sorted.find((f) => f.isCurrentVersion);
      return {
        formId,
        title: (current ?? sorted[0])?.title ?? '',
        attributeDomainName: (current ?? sorted[0])?.attributeDomainName,
        versions: sorted,
        current
      };
    });
}

interface SchemaGroup {
  name: string;
  versions: SchemaDefinitionData[]; // sorted by numeric version label
}

function groupSchemas(schemas: SchemaDefinitionData[]): SchemaGroup[] {
  const map = new Map<string, SchemaDefinitionData[]>();
  for (const s of schemas) {
    const list = map.get(s.schemaDefinitionName) ?? [];
    list.push(s);
    map.set(s.schemaDefinitionName, list);
  }
  return [...map.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([name, versions]) => ({
      name,
      versions: [...versions].sort(
        (x, y) => maxVersionInt([x.version]) - maxVersionInt([y.version]) || x.version.localeCompare(y.version)
      )
    }));
}

export function FormBuilderWindow({ onClose }: { onClose: () => void }) {
  const [tab, setTab] = useState<Tab>('forms');
  const [provider, setProvider] = useState<string | null>(null);

  // ── Forms state (all saved versions) ──────────────────────────────────────
  const [forms, setForms] = useState<FormDefinition[]>([]);
  const [selectedFormKey, setSelectedFormKey] = useState<string | null>(null);
  const [formMeta, setFormMeta] = useState({ formId: '', title: 'New Form', description: '', attributeDomainName: '' });
  const [pageJson, setPageJson] = useState(NEW_PAGE_TEMPLATE);
  const [isDirty, setIsDirty] = useState(false);

  // ── Domains state ─────────────────────────────────────────────────────────
  const [domains, setDomains] = useState<AttributeDomainEntry[]>([]);
  const [selectedDomainName, setSelectedDomainName] = useState<string | null>(null);
  const [domainDraft, setDomainDraft] = useState<AttributeDomainEntry>(() => JSON.parse(JSON.stringify(NEW_DOMAIN_TEMPLATE)) as AttributeDomainEntry);

  // ── Schemas state (all saved versions) ────────────────────────────────────
  const [schemas, setSchemas] = useState<SchemaDefinitionData[]>([]);
  const [selectedSchemaKey, setSelectedSchemaKey] = useState<string | null>(null);
  const [schemaMeta, setSchemaMeta] = useState({ name: '', version: '1', description: '' });
  const [definitionJson, setDefinitionJson] = useState('{}');

  // ── Shared ────────────────────────────────────────────────────────────────
  const [busy, setBusy] = useState(false);

  // ── AI build ──────────────────────────────────────────────────────────────
  const [aiModalOpen, setAiModalOpen] = useState(false);
  const [aiPlan, setAiPlan] = useState<AiFormPlan | null>(null);
  const { autoLayout } = useAutoLayout();

  const loadAll = useCallback(async () => {
    try {
      const [f, d, s] = await Promise.all([FormService.listForms(), FormService.listDomains(), FormService.listSchemas()]);
      setForms(f);
      setDomains(d);
      setSchemas(s);
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    }
  }, []);

  useEffect(() => {
    void loadAll();
    FormService.getFormProvider().then(setProvider).catch(() => setProvider(null));
  }, [loadAll]);

  // ── Derived ───────────────────────────────────────────────────────────────
  const formGroups = groupForms(forms);
  const schemaGroups = groupSchemas(schemas);
  const selectedForm = forms.find((f) => formKey(f.formId, f.version) === selectedFormKey) ?? null;
  const editingExistingForm = selectedForm !== null;
  const isCurrentVersion = !editingExistingForm || (selectedForm?.isCurrentVersion ?? true);
  const showFormEditor = editingExistingForm || formMeta.formId !== '' || isDirty;
  const selectedSchema = schemas.find((s) => schemaKey(s.schemaDefinitionName, s.version) === selectedSchemaKey) ?? null;
  const showSchemaEditor = selectedSchema !== null || schemaMeta.name !== '';

  // ── Form selection / editing ──────────────────────────────────────────────
  const selectForm = useCallback((def: FormDefinition) => {
    setSelectedFormKey(formKey(def.formId, def.version));
    setFormMeta({
      formId: def.formId,
      title: def.title ?? '',
      description: def.description ?? '',
      attributeDomainName: def.attributeDomainName ?? ''
    });
    setPageJson(JSON.stringify(def.page ?? {}, null, 2));
    setIsDirty(false);
  }, []);

  const startNewForm = useCallback(() => {
    setSelectedFormKey(null);
    setFormMeta({ formId: '', title: 'New Form', description: '', attributeDomainName: '' });
    setPageJson(NEW_PAGE_TEMPLATE);
    setIsDirty(true);
  }, []);

  /** Apply an AI-generated plan: form draft into the editor, domain persisted (or queued for review), flow offered on canvas. */
  const handleAiGenerated = useCallback(async (plan: AiFormPlan) => {
    setSelectedFormKey(null);
    setFormMeta({
      formId: plan.form.formId,
      title: plan.form.title,
      description: plan.form.description ?? '',
      attributeDomainName: plan.form.attributeDomainName ?? '',
    });
    setPageJson(JSON.stringify(plan.form.page, null, 2));
    setIsDirty(true);

    const domainName = plan.domain?.attributeDomain?.attributeDomainName;
    if (plan.domain && domainName) {
      try {
        if (!domains.some((d) => d.attributeDomain?.attributeDomainName === domainName)) {
          await FormService.saveDomain(plan.domain);
          showToast({ type: 'success', message: `Attribute domain "${domainName}" saved` });
        } else {
          setTab('domains');
          setSelectedDomainName(domainName);
          setDomainDraft(JSON.parse(JSON.stringify(plan.domain)) as AttributeDomainEntry);
          showToast({ type: 'success', message: `Domain "${domainName}" already exists — AI version loaded in the Domains tab for review; save it there if correct.` });
        }
      } catch (err) {
        showToast({ type: 'error', message: `Form draft loaded, but saving attribute domain failed: ${errorMessage(err)}` });
      }
    }

    setAiPlan(plan.flow ? plan : null);
    await loadAll();
    showToast({ type: 'success', message: `AI form "${plan.form.title}" loaded as a draft — review and save it.` });
  }, [domains, loadAll]);

  /** Import the AI-generated flow onto the canvas (replaces current nodes) and return to the flow view. */
  const handleOpenFlowInCanvas = useCallback(() => {
    if (!aiPlan?.flow) return;
    if (!window.confirm('Importing the generated flow will replace the current canvas. Continue?')) return;
    FlowService.importFlow(aiPlan.flow);
    setTimeout(() => { void autoLayout(); }, 50);
    setAiPlan(null);
    onClose();
  }, [aiPlan, autoLayout, onClose]);

  const parsePage = (): Record<string, unknown> | null => {
    try {
      const parsed = JSON.parse(pageJson) as Record<string, unknown>;
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) return parsed;
      showToast({ type: 'error', message: 'Page must be a JSON object with RootElements' });
    } catch (err) {
      showToast({ type: 'error', message: `Page JSON invalid: ${errorMessage(err)}` });
    }
    return null;
  };

  const handleSaveForm = useCallback(async () => {
    if (!formMeta.formId.trim()) {
      showToast({ type: 'error', message: 'Form ID is required' });
      return;
    }
    const page = parsePage();
    if (!page) return;
    setBusy(true);
    try {
      // Save upserts the version being edited (new drafts start at v1).
      const version = selectedForm?.version ?? '1';
      const res = await FormService.saveForm({
        formId: formMeta.formId.trim(),
        title: formMeta.title,
        description: formMeta.description || null,
        attributeDomainName: formMeta.attributeDomainName || null,
        schemaDefinitionName: selectedForm?.schemaDefinitionName ?? null,
        version,
        isCurrentVersion: true,
        page
      });
      setIsDirty(false);
      showToast({ type: 'success', message: `Form "${formMeta.formId}" v${res.version ?? version} saved` });
      await loadAll();
      setSelectedFormKey(formKey(res.formId ?? formMeta.formId.trim(), res.version ?? version));
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [formMeta, pageJson, selectedForm, loadAll]);

  const handlePublishNewVersion = useCallback(async () => {
    if (!selectedForm || !selectedForm.isCurrentVersion) return;
    const page = parsePage();
    if (!page) return;
    setBusy(true);
    try {
      const siblings = forms.filter((f) => f.formId === selectedForm.formId).map((f) => f.version);
      const next = String(maxVersionInt(siblings) + 1);
      const res = await FormService.saveForm({
        formId: selectedForm.formId,
        title: formMeta.title,
        description: formMeta.description || null,
        attributeDomainName: formMeta.attributeDomainName || null,
        schemaDefinitionName: selectedForm.schemaDefinitionName ?? null,
        version: next,
        isCurrentVersion: true,
        page
      });
      setIsDirty(false);
      showToast({ type: 'success', message: `Published "${selectedForm.formId}" v${res.version ?? next}` });
      await loadAll();
      setSelectedFormKey(formKey(selectedForm.formId, res.version ?? next));
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [selectedForm, formMeta, pageJson, forms, loadAll]);

  const handleSetAsCurrent = useCallback(async () => {
    if (!selectedForm || selectedForm.isCurrentVersion) return;
    setBusy(true);
    try {
      // Re-save the loaded version with isCurrentVersion=true (content unchanged).
      await FormService.saveForm({
        formId: selectedForm.formId,
        title: selectedForm.title,
        description: selectedForm.description ?? null,
        attributeDomainName: selectedForm.attributeDomainName ?? null,
        schemaDefinitionName: selectedForm.schemaDefinitionName ?? null,
        version: selectedForm.version,
        isCurrentVersion: true,
        page: (selectedForm.page ?? {}) as Record<string, unknown>
      });
      showToast({ type: 'success', message: `v${selectedForm.version} of "${selectedForm.formId}" is now current` });
      await loadAll();
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [selectedForm, loadAll]);

  const handleDeleteForm = useCallback(async () => {
    if (!selectedForm) return;
    const count = forms.filter((f) => f.formId === selectedForm.formId).length;
    if (!window.confirm(`Delete form "${selectedForm.formId}" and all ${count} version(s)?`)) return;
    setBusy(true);
    try {
      await FormService.deleteForm(selectedForm.formId);
      setSelectedFormKey(null);
      setFormMeta({ formId: '', title: 'New Form', description: '', attributeDomainName: '' });
      setPageJson(NEW_PAGE_TEMPLATE);
      setIsDirty(false);
      showToast({ type: 'success', message: `Form "${selectedForm.formId}" deleted` });
      await loadAll();
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [selectedForm, forms, loadAll]);

  // ── Domain selection / editing ────────────────────────────────────────────
  const selectDomain = useCallback((entry: AttributeDomainEntry) => {
    setSelectedDomainName(entry.attributeDomain?.attributeDomainName ?? null);
    setDomainDraft(JSON.parse(JSON.stringify(entry)) as AttributeDomainEntry);
  }, []);

  const startNewDomain = useCallback(() => {
    setSelectedDomainName(null);
    setDomainDraft(JSON.parse(JSON.stringify(NEW_DOMAIN_TEMPLATE)) as AttributeDomainEntry);
  }, []);

  const handleSaveDomain = useCallback(async () => {
    const name = domainDraft.attributeDomain?.attributeDomainName ?? '';
    if (!name.trim()) {
      showToast({ type: 'error', message: 'Domain name is required' });
      return;
    }
    setBusy(true);
    try {
      await FormService.saveDomain(domainDraft);
      setSelectedDomainName(name);
      showToast({ type: 'success', message: `Domain "${name}" saved` });
      await loadAll();
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [domainDraft, loadAll]);

  const handleDeleteDomain = useCallback(async () => {
    if (!selectedDomainName) return;
    if (!window.confirm(`Delete domain "${selectedDomainName}"?`)) return;
    setBusy(true);
    try {
      await FormService.deleteDomain(selectedDomainName);
      setSelectedDomainName(null);
      showToast({ type: 'success', message: `Domain "${selectedDomainName}" deleted` });
      await loadAll();
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [selectedDomainName, loadAll]);

  // Schema binding on the domain draft ("name@version", URI-encoded).
  const boundSchema = domainDraft.schemaDefinition ?? null;
  const schemaSelectValue = boundSchema
    ? `${encodeURIComponent(boundSchema.schemaDefinitionName)}@${encodeURIComponent(boundSchema.version)}`
    : '';

  const handleDomainSchemaChange = useCallback((value: string) => {
    setDomainDraft((d) => {
      const attr = d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain;
      if (!value) return { ...d, attributeDomain: attr, schemaDefinition: null };
      const at = value.indexOf('@');
      const name = decodeURIComponent(value.slice(0, at));
      const version = decodeURIComponent(value.slice(at + 1));
      return { ...d, attributeDomain: attr, schemaDefinition: { schemaDefinitionName: name, version } };
    });
  }, []);

  // ── Schema selection / editing ────────────────────────────────────────────
  const selectSchema = useCallback((def: SchemaDefinitionData) => {
    setSelectedSchemaKey(schemaKey(def.schemaDefinitionName, def.version));
    setSchemaMeta({ name: def.schemaDefinitionName, version: def.version, description: def.description ?? '' });
    setDefinitionJson(JSON.stringify(def.definition ?? {}, null, 2));
  }, []);

  const startNewSchema = useCallback(() => {
    setSelectedSchemaKey(null);
    setSchemaMeta({ name: '', version: '1', description: '' });
    setDefinitionJson('{}');
  }, []);

  // Auto-suggest the next integer version when a new draft's name matches an existing group.
  const handleSchemaNameChange = useCallback((value: string) => {
    setSchemaMeta((m) => {
      if (selectedSchemaKey === null && m.version === '1' && value.trim()) {
        const existing = schemas.filter((s) => s.schemaDefinitionName === value.trim()).map((s) => s.version);
        if (existing.length > 0) return { ...m, name: value, version: String(maxVersionInt(existing) + 1) };
      }
      return { ...m, name: value };
    });
  }, [selectedSchemaKey, schemas]);

  const handleSaveSchema = useCallback(async () => {
    if (!schemaMeta.name.trim()) {
      showToast({ type: 'error', message: 'Schema name is required' });
      return;
    }
    let definition: unknown;
    try {
      definition = JSON.parse(definitionJson);
    } catch (err) {
      showToast({ type: 'error', message: `Definition JSON invalid: ${errorMessage(err)}` });
      return;
    }
    setBusy(true);
    try {
      const version = schemaMeta.version.trim() || '1';
      const res = await FormService.saveSchema({
        schemaDefinitionName: schemaMeta.name.trim(),
        version,
        description: schemaMeta.description.trim() || null,
        definition: JSON.stringify(definition, null, 2)
      });
      showToast({ type: 'success', message: `Schema "${schemaMeta.name}" v${res.version ?? version} saved` });
      await loadAll();
      setSelectedSchemaKey(schemaKey(res.name ?? schemaMeta.name.trim(), res.version ?? version));
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [schemaMeta, definitionJson, loadAll]);

  const handleDeleteSchema = useCallback(async () => {
    if (!selectedSchema) return;
    if (!window.confirm(`Delete schema "${selectedSchema.schemaDefinitionName}" v${selectedSchema.version}?`)) return;
    setBusy(true);
    try {
      await FormService.deleteSchema(selectedSchema.schemaDefinitionName, selectedSchema.version);
      setSelectedSchemaKey(null);
      setSchemaMeta({ name: '', version: '1', description: '' });
      setDefinitionJson('{}');
      showToast({ type: 'success', message: `Schema "${selectedSchema.schemaDefinitionName}" v${selectedSchema.version} deleted` });
      await loadAll();
    } catch (err) {
      // 409 conflicts surface their reason here (e.g. referenced by a domain).
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  }, [selectedSchema, loadAll]);

  // ── Preview derivation (forms tab) ────────────────────────────────────────
  let previewPage: Record<string, unknown> | null = null;
  let previewError: string | null = null;
  try {
    const parsed = JSON.parse(pageJson) as Record<string, unknown>;
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) previewPage = parsed;
    else previewError = 'Page must be a JSON object';
  } catch (err) {
    previewError = errorMessage(err);
  }

  const selectedDomainEntry = domains.find((d) => d.attributeDomain?.attributeDomainName === formMeta.attributeDomainName);
  const previewAttributes: EntityAttributeData[] = selectedDomainEntry?.attributeDomain?.attributes ?? [];

  return (
    <div className="absolute inset-0 z-40 flex flex-col bg-gray-950">
      {/* Sub-header */}
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-800 bg-gray-900 shrink-0">
        <div className="flex items-center gap-2 min-w-0">
          <FileJson className="w-4 h-4 text-emerald-400 shrink-0" />
          <h2 className="text-sm font-semibold truncate">Form Builder</h2>
          {provider && (
            <span className="text-[10px] px-1.5 py-0.5 rounded bg-gray-800 text-gray-400 border border-gray-700 shrink-0" title="Active storage provider">
              {provider}
            </span>
          )}
        </div>
        <div className="flex items-center gap-1 shrink-0">
          {(['forms', 'domains', 'schemas'] as Tab[]).map((t) => (
            <button
              key={t}
              onClick={() => setTab(t)}
              className={`px-3 py-1.5 rounded-md text-xs font-medium capitalize transition-colors ${tab === t ? 'bg-emerald-600/20 text-emerald-300 border border-emerald-700/50' : 'text-gray-400 hover:text-gray-200 border border-transparent'}`}
            >
              {t}
            </button>
          ))}
          <div className="h-5 w-px bg-gray-700 mx-1" />
          <button
            onClick={onClose}
            title="Back to Flow"
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium text-gray-300 hover:text-white bg-gray-800/60 hover:bg-gray-700 border border-gray-700 transition-colors"
          >
            <ArrowLeft className="w-3.5 h-3.5" /> Back to Flow
          </button>
        </div>
      </div>

      {tab === 'forms' && (
        /* ── Forms tab: versioned form list + editor ─────────────────────── */
        <div className="flex flex-1 overflow-hidden">
          {/* Form groups */}
          <div className="w-72 shrink-0 border-r border-gray-800 bg-gray-900/40 flex flex-col">
            <div className="p-2 border-b border-gray-800 space-y-1.5">
              <button onClick={startNewForm} className="flex items-center gap-1.5 w-full px-2 py-1.5 rounded bg-emerald-600/20 text-emerald-300 hover:bg-emerald-600/30 text-xs transition-colors">
                <Plus className="w-3 h-3" /> New Form
              </button>
              <button onClick={() => setAiModalOpen(true)} title="Describe what to capture — AI generates the form, domain contract and save flow" className="flex items-center gap-1.5 w-full px-2 py-1.5 rounded bg-indigo-600/20 text-indigo-300 hover:bg-indigo-600/30 text-xs transition-colors">
                <Sparkles className="w-3 h-3" /> AI Build
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-2 space-y-1.5">
              {formGroups.length === 0 && <p className="text-xs text-gray-500 italic px-1 py-2">No forms yet.</p>}
              {formGroups.map((g) => (
                <div key={g.formId} className={`rounded-lg border p-2 ${selectedFormKey === formKey(g.formId, g.current?.version ?? '') ? 'border-emerald-700/60 bg-emerald-950/20' : 'border-gray-800 hover:border-gray-700'}`}>
                  <div className="flex items-center justify-between gap-1">
                    <button onClick={() => g.current && selectForm(g.current)} title={g.formId} className="text-xs font-medium text-gray-200 truncate flex-1 text-left hover:text-white transition-colors">
                      {g.title || g.formId}
                    </button>
                    {g.versions.length > 1 && (
                      <span className="text-[9px] px-1 py-0.5 rounded bg-gray-800 text-gray-400 shrink-0">{g.versions.length} versions</span>
                    )}
                  </div>
                  {g.attributeDomainName && (
                    <div className="text-[10px] text-gray-500 truncate mt-0.5">→ {g.attributeDomainName}</div>
                  )}
                  <div className="flex items-center gap-1 mt-1.5 flex-wrap">
                    {g.versions.map((v) => (
                      <button
                        key={v.version}
                        onClick={() => selectForm(v)}
                        title={`Open v${v.version}${v.isCurrentVersion ? ' (current)' : ''}`}
                        className={`text-[9px] px-1.5 py-0.5 rounded border transition-colors ${selectedFormKey === formKey(g.formId, v.version) ? 'bg-emerald-600/30 text-emerald-200 border-emerald-700' : v.isCurrentVersion ? 'bg-gray-800 text-emerald-400/90 border-gray-700 hover:border-emerald-800' : 'bg-gray-900 text-gray-500 border-gray-800 hover:text-gray-300'}`}
                      >
                        v{v.version}{v.isCurrentVersion ? ' •' : ''}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Editor + preview */}
          <div className="flex-1 flex flex-col overflow-hidden">
            {!showFormEditor ? (
              <div className="flex-1 flex items-center justify-center text-xs text-gray-500">Select a form or create a new one.</div>
            ) : (
              <>
                {/* Meta fields */}
                {aiPlan?.flow && (
                  <div className="mx-3 mt-2 rounded-lg border border-indigo-500/40 bg-indigo-500/10 px-3 py-2 flex items-center gap-2">
                    <Rocket className="w-4 h-4 text-indigo-300 shrink-0" />
                    <div className="text-[11px] text-indigo-200 flex-1 min-w-0 truncate">
                      AI generated a flow: {Object.keys(aiPlan.flow.states).join(' → ')} — open it on the canvas to review.
                    </div>
                    <button onClick={handleOpenFlowInCanvas} className="shrink-0 px-2 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white text-[11px] font-medium transition-colors">
                      Open Flow in Canvas
                    </button>
                    <button onClick={() => setAiPlan(null)} title="Dismiss" className="shrink-0 p-1 rounded text-indigo-300/70 hover:text-white transition-colors">
                      <X className="w-3.5 h-3.5" />
                    </button>
                  </div>
                )}
                <div className="grid grid-cols-2 gap-2 p-3 border-b border-gray-800">
                  <input
                    className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs font-mono text-gray-200 focus:outline-none focus:border-emerald-500 disabled:opacity-50"
                    placeholder="Form ID (e.g. order-approval)"
                    value={formMeta.formId}
                    onChange={(e) => { setFormMeta((m) => ({ ...m, formId: e.target.value })); setIsDirty(true); }}
                    disabled={editingExistingForm}
                  />
                  <input
                    className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                    placeholder="Title"
                    value={formMeta.title}
                    onChange={(e) => { setFormMeta((m) => ({ ...m, title: e.target.value })); setIsDirty(true); }}
                  />
                  <input
                    className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                    placeholder="Description (optional)"
                    value={formMeta.description}
                    onChange={(e) => { setFormMeta((m) => ({ ...m, description: e.target.value })); setIsDirty(true); }}
                  />
                  <div className="flex items-center gap-2">
                    <select
                      className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                      value={formMeta.attributeDomainName}
                      onChange={(e) => { setFormMeta((m) => ({ ...m, attributeDomainName: e.target.value })); setIsDirty(true); }}
                    >
                      <option value="">— no domain binding —</option>
                      {domains.map((d) => (
                        <option key={d.attributeDomain?.attributeDomainName} value={d.attributeDomain?.attributeDomainName ?? ''}>
                          {d.attributeDomain?.attributeDomainName}
                        </option>
                      ))}
                    </select>
                    {editingExistingForm ? (
                      <span className={`text-[10px] px-2 py-1 rounded border whitespace-nowrap ${isCurrentVersion ? 'bg-emerald-950/40 text-emerald-300 border-emerald-800' : 'bg-gray-900 text-gray-400 border-gray-700'}`}>
                        v{selectedForm!.version} {isCurrentVersion ? '· current' : '· read-only history'}
                      </span>
                    ) : (
                      <span className="text-[10px] px-2 py-1 rounded border bg-gray-900 text-gray-400 border-gray-700 whitespace-nowrap">new form → v1</span>
                    )}
                  </div>
                </div>

                {/* Page JSON editor + live preview */}
                <div className="flex-1 min-h-0 flex">
                  <div className="w-1/2 border-r border-gray-800 min-h-0">
                    {isCurrentVersion ? (
                      <Editor
                        height="100%"
                        defaultLanguage="json"
                        theme="vs-dark"
                        value={pageJson}
                        onChange={(v) => { setPageJson(v ?? ''); setIsDirty(true); }}
                        options={{ minimap: { enabled: false }, fontSize: 12, lineNumbers: 'off', scrollBeyondLastLine: false }}
                      />
                    ) : (
                      <div className="h-full overflow-auto p-3">
                        <pre className="text-xs font-mono text-gray-400 whitespace-pre-wrap">{pageJson}</pre>
                      </div>
                    )}
                  </div>
                  {/* Live preview */}
                  <div className="w-1/2 overflow-y-auto p-3">
                    <p className="text-[10px] font-semibold text-gray-500 uppercase tracking-wide mb-2">Live Preview</p>
                    {previewError ? (
                      <div className="flex items-start gap-2 text-xs text-red-400 bg-red-950/30 border border-red-900 rounded p-2">
                        <AlertCircle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
                        <span>{previewError}</span>
                      </div>
                    ) : previewPage ? (
                      <FormRenderer page={previewPage} values={{}} attributes={previewAttributes} readOnly />
                    ) : null}
                  </div>
                </div>

                {/* Actions */}
                <div className="flex items-center gap-2 px-3 py-2 border-t border-gray-800">
                  {isCurrentVersion ? (
                    <button
                      onClick={() => void handleSaveForm()}
                      disabled={busy}
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-medium transition-colors disabled:opacity-50"
                    >
                      <Save className="w-3 h-3" /> {busy ? 'Saving…' : isDirty ? `Save v${selectedForm?.version ?? '1'}` : 'Saved'}
                    </button>
                  ) : (
                    <button
                      onClick={() => void handleSetAsCurrent()}
                      disabled={busy}
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-emerald-600/20 hover:bg-emerald-600/30 text-emerald-300 border border-emerald-700/60 text-xs font-medium transition-colors disabled:opacity-50"
                    >
                      <Rocket className="w-3 h-3" /> Set as Current
                    </button>
                  )}
                  {editingExistingForm && isCurrentVersion && (
                    <button
                      onClick={() => void handlePublishNewVersion()}
                      disabled={busy}
                      title="Snapshot the editor content as v(max+1) and make it current"
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-gray-800 hover:bg-gray-700 text-gray-200 border border-gray-700 text-xs font-medium transition-colors disabled:opacity-50"
                    >
                      <Rocket className="w-3 h-3" /> Publish New Version
                    </button>
                  )}
                  {selectedForm && (
                    <button
                      onClick={() => void handleDeleteForm()}
                      disabled={busy}
                      title="Delete the form and all its versions"
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-red-900/40 hover:bg-red-900/60 text-red-300 text-xs transition-colors disabled:opacity-50"
                    >
                      <Trash2 className="w-3 h-3" /> Delete Form
                    </button>
                  )}
                  {isDirty && isCurrentVersion && <span className="text-[10px] text-amber-400">unsaved changes</span>}
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {tab === 'domains' && (
        /* ── Domains tab: registry + editor with schema binding ─────────── */
        <div className="flex flex-1 overflow-hidden">
          {/* Domain list */}
          <div className="w-72 shrink-0 border-r border-gray-800 bg-gray-900/40 flex flex-col">
            <div className="p-2 border-b border-gray-800">
              <button onClick={startNewDomain} className="flex items-center gap-1.5 w-full px-2 py-1.5 rounded bg-emerald-600/20 text-emerald-300 hover:bg-emerald-600/30 text-xs transition-colors">
                <Plus className="w-3 h-3" /> New Domain
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-2 space-y-1">
              {domains.length === 0 && <p className="text-xs text-gray-500 italic px-1 py-2">No domains yet.</p>}
              {domains.map((d) => (
                <button
                  key={d.attributeDomain?.attributeDomainName}
                  onClick={() => selectDomain(d)}
                  className={`w-full text-left px-2 py-1.5 rounded text-xs transition-colors ${selectedDomainName === d.attributeDomain?.attributeDomainName ? 'bg-emerald-600/20 text-emerald-300' : 'text-gray-400 hover:bg-gray-800 hover:text-gray-200'}`}
                >
                  <div className="font-medium truncate">{d.attributeDomain?.attributeDomainName || '(unnamed)'}</div>
                  <div className="truncate opacity-60">
                    v{d.attributeDomain?.version ?? '?'} · {d.attributeDomain?.attributes?.length ?? 0} attrs
                    {d.schemaDefinition ? ` · schema ${d.schemaDefinition.schemaDefinitionName}@${d.schemaDefinition.version}` : ''}
                  </div>
                </button>
              ))}
            </div>
          </div>

          {/* Domain editor */}
          <div className="flex-1 flex flex-col overflow-hidden">
            <div className="p-3 border-b border-gray-800 space-y-2">
              <div className="grid grid-cols-3 gap-2">
                <input
                  className="col-span-2 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                  placeholder="Domain name (e.g. OrderApproval)"
                  value={domainDraft.attributeDomain?.attributeDomainName ?? ''}
                  onChange={(e) => setDomainDraft((d) => ({ ...d, attributeDomain: { ...(d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain), attributeDomainName: e.target.value } }))}
                />
                <input
                  className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                  placeholder="Version (e.g. 1.0)"
                  value={domainDraft.attributeDomain?.version ?? ''}
                  onChange={(e) => setDomainDraft((d) => ({ ...d, attributeDomain: { ...(d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain), version: e.target.value } }))}
                />
              </div>
              <input
                className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                placeholder="Description (optional)"
                value={domainDraft.attributeDomain?.description ?? ''}
                onChange={(e) => setDomainDraft((d) => ({ ...d, attributeDomain: { ...(d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain), description: e.target.value } }))}
              />
              {/* Schema binding */}
              <div className="flex items-center gap-2">
                <label className="text-[10px] text-gray-500 uppercase tracking-wide shrink-0 flex items-center gap-1">
                  <Braces className="w-3 h-3" /> Schema
                </label>
                <select
                  className="flex-1 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                  value={schemaSelectValue}
                  onChange={(e) => handleDomainSchemaChange(e.target.value)}
                >
                  <option value="">— no schema —</option>
                  {schemas.map((s) => (
                    <option key={schemaKey(s.schemaDefinitionName, s.version)} value={`${encodeURIComponent(s.schemaDefinitionName)}@${encodeURIComponent(s.version)}`}>
                      {s.schemaDefinitionName} @ v{s.version}{s.description ? ` — ${s.description}` : ''}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {/* Attribute table */}
            <div className="flex-1 overflow-y-auto p-3 space-y-2">
              {(domainDraft.attributeDomain?.attributes ?? []).map((attr, idx) => (
                <AttributeRow key={idx} attr={attr} onChange={(next) => {
                  const attrs = [...(domainDraft.attributeDomain?.attributes ?? [])];
                  attrs[idx] = next;
                  setDomainDraft((d) => ({ ...d, attributeDomain: { ...(d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain), attributes: attrs } }));
                }} onRemove={() => {
                  const attrs = (domainDraft.attributeDomain?.attributes ?? []).filter((_, i) => i !== idx);
                  setDomainDraft((d) => ({ ...d, attributeDomain: { ...(d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain), attributes: attrs } }));
                }} />
              ))}
              <button
                onClick={() => setDomainDraft((d) => ({
                  ...d,
                  attributeDomain: {
                    ...(d.attributeDomain ?? NEW_DOMAIN_TEMPLATE.attributeDomain),
                    attributes: [...(d.attributeDomain?.attributes ?? []), { attributeName: '', dataType: 0, displayName: '', placeholder: '', helpText: '', visible: true, readOnly: false, primaryKey: false }]
                  }
                }))}
                className="flex items-center gap-1.5 px-2 py-1.5 rounded border border-dashed border-gray-700 text-gray-400 hover:text-emerald-300 hover:border-emerald-600/50 text-xs transition-colors w-full"
              >
                <Plus className="w-3 h-3" /> Add Attribute
              </button>
            </div>

            {/* Actions */}
            <div className="flex items-center gap-2 px-3 py-2 border-t border-gray-800">
              <button
                onClick={() => void handleSaveDomain()}
                disabled={busy}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-medium transition-colors disabled:opacity-50"
              >
                <Save className="w-3 h-3" /> {busy ? 'Saving…' : 'Save Domain'}
              </button>
              {selectedDomainName && (
                <button
                  onClick={() => void handleDeleteDomain()}
                  disabled={busy}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-red-900/40 hover:bg-red-900/60 text-red-300 text-xs transition-colors disabled:opacity-50"
                >
                  <Trash2 className="w-3 h-3" /> Delete
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {tab === 'schemas' && (
        /* ── Schemas tab: versioned schema-definition registry ──────────── */
        <div className="flex flex-1 overflow-hidden">
          {/* Schema groups */}
          <div className="w-72 shrink-0 border-r border-gray-800 bg-gray-900/40 flex flex-col">
            <div className="p-2 border-b border-gray-800">
              <button onClick={startNewSchema} className="flex items-center gap-1.5 w-full px-2 py-1.5 rounded bg-emerald-600/20 text-emerald-300 hover:bg-emerald-600/30 text-xs transition-colors">
                <Plus className="w-3 h-3" /> New Schema
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-2 space-y-1.5">
              {schemaGroups.length === 0 && <p className="text-xs text-gray-500 italic px-1 py-2">No schemas yet.</p>}
              {schemaGroups.map((g) => (
                <div key={g.name} className={`rounded-lg border p-2 ${selectedSchemaKey === schemaKey(g.name, g.versions[0]?.version ?? '') ? 'border-emerald-700/60 bg-emerald-950/20' : 'border-gray-800 hover:border-gray-700'}`}>
                  <div className="text-xs font-medium text-gray-200 truncate" title={g.name}>{g.name}</div>
                  {g.versions[0]?.description && (
                    <div className="text-[10px] text-gray-500 truncate mt-0.5">{g.versions[0].description}</div>
                  )}
                  <div className="flex items-center gap-1 mt-1.5 flex-wrap">
                    {g.versions.map((v) => (
                      <button
                        key={v.version}
                        onClick={() => selectSchema(v)}
                        title={`Open v${v.version}`}
                        className={`text-[9px] px-1.5 py-0.5 rounded border transition-colors ${selectedSchemaKey === schemaKey(g.name, v.version) ? 'bg-emerald-600/30 text-emerald-200 border-emerald-700' : 'bg-gray-900 text-gray-400 border-gray-800 hover:text-gray-200'}`}
                      >
                        v{v.version}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Schema editor */}
          <div className="flex-1 flex flex-col overflow-hidden">
            {!showSchemaEditor ? (
              <div className="flex-1 flex items-center justify-center text-xs text-gray-500">Select a schema or create a new one.</div>
            ) : (
              <>
                {/* Meta fields */}
                <div className="grid grid-cols-[2fr_100px_3fr] gap-2 p-3 border-b border-gray-800">
                  <input
                    className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs font-mono text-gray-200 focus:outline-none focus:border-emerald-500"
                    placeholder="Schema name (e.g. OrderApproval)"
                    value={schemaMeta.name}
                    onChange={(e) => handleSchemaNameChange(e.target.value)}
                  />
                  <input
                    className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs font-mono text-gray-200 focus:outline-none focus:border-emerald-500"
                    placeholder="Version"
                    value={schemaMeta.version}
                    onChange={(e) => setSchemaMeta((m) => ({ ...m, version: e.target.value }))}
                  />
                  <input
                    className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
                    placeholder="Description (optional)"
                    value={schemaMeta.description}
                    onChange={(e) => setSchemaMeta((m) => ({ ...m, description: e.target.value }))}
                  />
                </div>

                {/* Definition JSON editor */}
                <div className="flex-1 min-h-0">
                  <Editor
                    height="100%"
                    defaultLanguage="json"
                    theme="vs-dark"
                    value={definitionJson}
                    onChange={(v) => setDefinitionJson(v ?? '')}
                    options={{ minimap: { enabled: false }, fontSize: 12, lineNumbers: 'off', scrollBeyondLastLine: false }}
                  />
                </div>

                {/* Parse-error display */}
                {(() => {
                  try {
                    JSON.parse(definitionJson);
                    return null;
                  } catch (err) {
                    return (
                      <div className="flex items-start gap-2 text-xs text-red-400 bg-red-950/30 border-t border-red-900 px-3 py-1.5 shrink-0">
                        <AlertCircle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
                        <span>{errorMessage(err)}</span>
                      </div>
                    );
                  }
                })()}

                {/* Actions */}
                <div className="flex items-center gap-2 px-3 py-2 border-t border-gray-800">
                  <button
                    onClick={() => void handleSaveSchema()}
                    disabled={busy}
                    className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-medium transition-colors disabled:opacity-50"
                  >
                    <Save className="w-3 h-3" /> {busy ? 'Saving…' : `Save v${schemaMeta.version || '1'}`}
                  </button>
                  {selectedSchema && (
                    <button
                      onClick={() => void handleDeleteSchema()}
                      disabled={busy}
                      title="Delete this schema version"
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded bg-red-900/40 hover:bg-red-900/60 text-red-300 text-xs transition-colors disabled:opacity-50"
                    >
                      <Trash2 className="w-3 h-3" /> Delete Version
                    </button>
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      )}
      <AiFormBuildModal open={aiModalOpen} onClose={() => setAiModalOpen(false)} onGenerated={(plan) => void handleAiGenerated(plan)} />
    </div>
  );
}

// ── Attribute editor row (ported from the retired FormBuilderPanel) ─────────

function AttributeRow({ attr, onChange, onRemove }: { attr: EntityAttributeData; onChange: (next: EntityAttributeData) => void; onRemove: () => void }) {
  const set = <K extends keyof EntityAttributeData>(key: K, value: EntityAttributeData[K]) => onChange({ ...attr, [key]: value });

  return (
    <div className="border border-gray-800 rounded-md p-2 space-y-1.5 bg-gray-950/40">
      <div className="grid grid-cols-[1fr_130px_auto] gap-2 items-center">
        <input
          className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
          placeholder="AttributeName (e.g. OrderTotal)"
          value={attr.attributeName}
          onChange={(e) => set('attributeName', e.target.value)}
        />
        <select
          className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
          value={attr.dataType}
          onChange={(e) => set('dataType', Number(e.target.value))}
        >
          {ATTRIBUTE_DATA_TYPES.map((label, i) => (
            <option key={label} value={i}>{label}</option>
          ))}
        </select>
        <button onClick={onRemove} className="text-gray-500 hover:text-red-400 transition-colors" title="Remove attribute">
          <Trash2 className="w-3.5 h-3.5" />
        </button>
      </div>
      <div className="grid grid-cols-2 gap-2">
        <input
          className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
          placeholder={`DisplayName (default: ${attr.attributeName || '—'})`}
          value={attr.displayName}
          onChange={(e) => set('displayName', e.target.value)}
        />
        <input
          className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
          placeholder="Placeholder (optional)"
          value={attr.placeholder}
          onChange={(e) => set('placeholder', e.target.value)}
        />
      </div>
      <input
        className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-emerald-500"
        placeholder="HelpText (optional)"
        value={attr.helpText}
        onChange={(e) => set('helpText', e.target.value)}
      />
      <div className="flex items-center gap-4">
        {(['visible', 'readOnly', 'primaryKey'] as const).map((flag) => (
          <label key={flag} className="flex items-center gap-1.5 text-[11px] text-gray-400">
            <input type="checkbox" className="accent-emerald-500" checked={Boolean(attr[flag])} onChange={(e) => set(flag, e.target.checked)} />
            {flag === 'visible' ? 'Visible' : flag === 'readOnly' ? 'Read-only' : 'Primary key'}
          </label>
        ))}
      </div>
      <input
        className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-[11px] font-mono text-gray-300 focus:outline-none focus:border-emerald-500"
        placeholder='ValidationSchemaJson (optional, e.g. {"required":true,"minimum":0})'
        value={attr.validationSchemaJson ?? ''}
        onChange={(e) => set('validationSchemaJson', e.target.value || null)}
      />
    </div>
  );
}
