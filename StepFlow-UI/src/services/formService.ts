// ============================================================================
// Form Capture Service Client
// Talks to /api/forms, /api/attribute-domains and /api/form-captures endpoints.
// Wire format is camelCase (KeyPreservingCamelCaseContractResolver); the UIPage
// document inside a form keeps its PascalCase keys per uidata-schema.json.
// ============================================================================

/** A JSON-configured page definition (UIPage per DataExchange/UIData/uidata-schema.json). */
export interface FormDefinition {
  formId: string;
  /** Version label (auto-incremented integer as a string, e.g. "1", "2"). */
  version: string;
  /** True for the version flow steps resolve when they do not pin one. */
  isCurrentVersion: boolean;
  title: string;
  description?: string | null;
  /** Name of the bound AttributeDomain entry (null = unbound form, values pass through as-is). */
  attributeDomainName?: string | null;
  schemaDefinitionName?: string | null;
  /** The full UIPage JSON. Keys are PascalCase: Id, Title, PageName, PageVersion, RootElements... */
  page: Record<string, unknown>;
}

/** AttributeDataType enum ordinal (backend DataExchange/Enums.cs). */
export const ATTRIBUTE_DATA_TYPES = ['String', 'Boolean', 'Number', 'Date', 'Object', 'Array'] as const;

/** Flat attribute contract row (persistence DTO shape used by the domain registry + API bodies). */
export interface EntityAttributeData {
  attributeName: string;
  dataType: number; // String=0, Boolean=1, Number=2, Date=3, Object=4, Array=5
  description?: string | null;
  displayName: string;
  placeholder: string;
  helpText: string;
  visible: boolean;
  readOnly: boolean;
  primaryKey: boolean;
  /** JSON string with constraints: required, minimum/maximum, minLength/maxLength, pattern. */
  validationSchemaJson?: string | null;
}

export interface SchemaDefinitionRef {
  schemaDefinitionName: string;
  version: string;
  description?: string | null;
}
/** One saved version of a schema definition (MetaData POCO, camelCase wire shape). */
export interface SchemaDefinitionData {
  schemaDefinitionName: string;
  /** Version label (auto-incremented integer as a string, e.g. "1", "2"). */
  version: string;
  description?: string | null;
  /** The raw JSON text body of the definition (free-form JSON authored in Monaco). */
  definition?: string | null;
}

/** One entry of the attribute domain registry (file + API body shape). */
export interface AttributeDomainEntry {
  schemaDefinition?: SchemaDefinitionRef | null;
  attributeDomain: {
    version: string;
    attributeDomainName: string;
    description?: string | null;
    isCurrentVersion: boolean;
    attributes: EntityAttributeData[];
  };
}

/** MetaData POCO as serialized by the form-capture GET endpoint (camelCase). */
export interface EntityAttribute {
  entityAttributeId: number;
  primaryKey: boolean;
  version?: string | null;
  attributeName: string;
  description?: string | null;
  dataType: number;
  validationSchemaJson?: string | null;
  displayName?: string | null;
  placeholder?: string | null;
  helpText?: string | null;
  visible: boolean;
  readOnly: boolean;
  attributeDomainId: number;
}

/** GET /api/form-captures/{taskId} response. */
export interface FormCaptureTaskInfo {
  taskId: string;
  status: string; // "Pending" | "Completed" | ...
  title?: string | null;
  assignee?: string | null;
  executionId?: string | null;
  stateMachineName?: string | null;
  createdAtUtc?: string | null;
  completedAtUtc?: string | null;
  result?: unknown;
  formUrl: string;
  form: FormDefinition;
  attributes: EntityAttribute[];
}

/** Parsed ValidationSchemaJson constraints (subset enforced by the backend). */
export interface AttributeValidation {
  required?: boolean;
  minimum?: number;
  maximum?: number;
  minLength?: number;
  maxLength?: number;
  pattern?: string;
}

/** Parses a ValidationSchemaJson string into its constraint object, or null when absent/invalid. */
export function parseAttributeValidation(json: string | null | undefined): AttributeValidation | null {
  if (!json || !json.trim()) return null;
  try {
    const v = JSON.parse(json);
    return typeof v === 'object' && v !== null ? (v as AttributeValidation) : null;
  } catch {
    return null;
  }
}

/** Human label for a dataType ordinal. */
export function attributeDataTypeLabel(dataType: number): string {
  return ATTRIBUTE_DATA_TYPES[dataType] ?? `Unknown(${dataType})`;
}

// ── Module-level form cache ────────────────────────────────────────────────
// The PropertyPanel renders dropdowns synchronously (same mechanism as the
// targetFlowId localStorage special case). listForms() refreshes this cache,
// so by the time a user configures a FormCapture node it is populated.

let _cachedForms: FormDefinition[] = [];

/** Synchronous snapshot of the last known form definitions (may be stale/empty). */
export function getCachedForms(): FormDefinition[] {
  return _cachedForms;
}

/** Wire shape of GET /api/forms/provider. */
interface ProviderResponse {
  provider?: string;
}
export class FormService {
  /** List all form definitions. Refreshes the module-level cache used by dropdowns. */
  static async listForms(): Promise<FormDefinition[]> {
    const res = await fetch(`/api/forms`);
    if (!res.ok) throw new Error(`Failed to load forms: ${res.status}`);
    _cachedForms = (await res.json()) as FormDefinition[];
    return _cachedForms;
  }

  /** The active storage provider name ("json" | "sqlite"). */
  static async getFormProvider(): Promise<string> {
    const res = await fetch(`/api/forms/provider`);
    if (!res.ok) throw new Error(`Failed to load form provider: ${res.status}`);
    return ((await res.json()) as ProviderResponse).provider ?? 'unknown';
  }

  /** Save (create or replace) a form definition by formId. */
  static async saveForm(def: FormDefinition): Promise<{ status?: string; formId?: string; version?: string }> {
    const res = await fetch(`/api/forms`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(def),
    });
    if (!res.ok) throw new Error(`Failed to save form "${def.formId}": ${res.status}`);
    return (await res.json()) as { status?: string; formId?: string; version?: string };
  }

  /** Delete a form definition by id. */
  static async deleteForm(formId: string): Promise<void> {
    const res = await fetch(`/api/forms/${encodeURIComponent(formId)}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(`Failed to delete form "${formId}": ${res.status}`);
  }

  /** List all attribute domain registry entries. */
  static async listDomains(): Promise<AttributeDomainEntry[]> {
    const res = await fetch(`/api/attribute-domains`);
    if (!res.ok) throw new Error(`Failed to load attribute domains: ${res.status}`);
    return (await res.json()) as AttributeDomainEntry[];
  }

  /** Save (create or replace) an attribute domain by name. */
  static async saveDomain(entry: AttributeDomainEntry): Promise<{ status?: string; domainName?: string }> {
    const res = await fetch(`/api/attribute-domains`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(entry),
    });
    if (!res.ok) throw new Error(`Failed to save domain "${entry.attributeDomain?.attributeDomainName}": ${res.status}`);
    return (await res.json()) as { status?: string; domainName?: string };
  }

  /** Delete an attribute domain by name. */
  static async deleteDomain(name: string): Promise<void> {
    const res = await fetch(`/api/attribute-domains/${encodeURIComponent(name)}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(`Failed to delete domain "${name}": ${res.status}`);
  }
  /** List all saved schema definition versions. */
  static async listSchemas(): Promise<SchemaDefinitionData[]> {
    const res = await fetch(`/api/schema-definitions`);
    if (!res.ok) throw new Error(`Failed to load schema definitions: ${res.status}`);
    return (await res.json()) as SchemaDefinitionData[];
  }

  /** Save (create or replace) a schema definition version by name + version. */
  static async saveSchema(def: SchemaDefinitionData): Promise<{ status?: string; name?: string; version?: string }> {
    const res = await fetch(`/api/schema-definitions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(def),
    });
    if (!res.ok) throw new Error(`Failed to save schema "${def.schemaDefinitionName}": ${res.status}`);
    return (await res.json()) as { status?: string; name?: string; version?: string };
  }

  /** Delete one schema definition version. Throws with the referencing-domain list on 409. */
  static async deleteSchema(name: string, version: string): Promise<void> {
    const res = await fetch(`/api/schema-definitions/${encodeURIComponent(name)}/${encodeURIComponent(version)}`, { method: 'DELETE' });
    if (!res.ok) {
      let detail = `${res.status}`;
      try {
        const body = (await res.json()) as { error?: string; domains?: string[] };
        if (body.error) detail = body.error;
      } catch {
        /* non-JSON error body */
      }
      throw new Error(detail);
    }
  }

  /** Load a suspended form-capture task (form + bound attribute contract). */
  static async getFormCapture(taskId: string): Promise<FormCaptureTaskInfo> {
    const res = await fetch(`/api/form-captures/${encodeURIComponent(taskId)}`);
    if (!res.ok) throw new Error(`Failed to load form capture "${taskId}": ${res.status}`);
    return (await res.json()) as FormCaptureTaskInfo;
  }

  /** Submit captured values for a suspended task. Throws with the backend error map on 400/409. */
  static async submitFormCapture(taskId: string, values: Record<string, unknown>): Promise<{ status?: string }> {
    const res = await fetch(`/api/form-captures/${encodeURIComponent(taskId)}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ values }),
    });
    if (!res.ok) {
      let detail = `${res.status}`;
      try {
        const body = (await res.json()) as { error?: string; errors?: Record<string, string> };
        if (body.error) detail = body.error;
        else if (body.errors && Object.keys(body.errors).length > 0) {
          detail = Object.entries(body.errors)
            .map(([k, v]) => `${k}: ${v}`)
            .join('; ');
        }
      } catch {
        /* non-JSON error body */
      }
      throw new Error(detail);
    }
    return (await res.json()) as { status?: string };
  }
}
