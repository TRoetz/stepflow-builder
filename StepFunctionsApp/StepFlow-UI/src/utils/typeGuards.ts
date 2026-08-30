// Canonical runtime type guards for untrusted/persisted JSON boundaries.
// Import from here; never redefine locally at call sites.

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
