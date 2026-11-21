// ═══════════════════════════════════════════════════════════
// Rules Service — browser client for the named-rule API.
// Named rules are first-class persistent artifacts (choice / jsonata /
// sql / ms-rules / ai-decision) that flows can reference by name.
// ═══════════════════════════════════════════════════════════

export const RULE_KINDS = ['choice', 'jsonata', 'sql', 'ms-rules', 'ai-decision'] as const;
export type RuleKind = (typeof RULE_KINDS)[number];

export interface NamedRule {
  name: string;
  kind: RuleKind | string;
  description?: string | null;
  definition: unknown;
  updatedAtUtc: string;
}

async function fail(res: Response): Promise<never> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string' && body.error.length > 0) message = body.error;
  } catch { /* non-JSON error body - keep the status message */ }
  throw new Error(message);
}

// Rule names may contain '/' (the backend route is {*name}), so encode each segment.
function rulePath(name: string): string {
  return name.split('/').map(encodeURIComponent).join('/');
}

export const RulesService = {
  /** All named rules, newest first per the store's ordering. */
  async list(): Promise<NamedRule[]> {
    const res = await fetch('/api/rules');
    if (!res.ok) return fail(res);
    return (await res.json()) as NamedRule[];
  },

  /** One rule by name. Throws with the backend message on 404. */
  async get(name: string): Promise<NamedRule> {
    const res = await fetch(`/api/rules/${rulePath(name)}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as NamedRule;
  },

  /** Create or update a rule. Returns the saved rule with its updatedAtUtc stamp. */
  async save(rule: { name: string; kind: string; description?: string | null; definition: unknown }): Promise<NamedRule> {
    const res = await fetch('/api/rules', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(rule),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as NamedRule;
  },

  /** Delete a rule by name. Throws with the backend message on 404. */
  async remove(name: string): Promise<{ deleted: string }> {
    const res = await fetch(`/api/rules/${rulePath(name)}`, { method: 'DELETE' });
    if (!res.ok) return fail(res);
    return (await res.json()) as { deleted: string };
  },
};
