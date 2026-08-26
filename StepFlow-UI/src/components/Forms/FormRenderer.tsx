import { EntityAttributeData, parseAttributeValidation } from '@services/formService';

// ═══════════════════════════════════════════════════════════
// FormRenderer — renders a UIPage document (uidata-schema.json) into live
// form controls. Used by the builder panel's preview pane and mirrored in
// wwwroot/form-capture.html for standalone fill-in.
//
// Value key contract (must match the backend submit endpoint):
//   bound forms  -> keys are AttributeName; an element opts in via its
//                   optional "Attribute" field (PascalCase, UIPage convention)
//                   or lowercase "attribute"; absent -> String(element.Id).
//   unbound forms-> any key passes through as-is.
// ═══════════════════════════════════════════════════════════

/** Loose view over one UIElement node (PascalCase keys per uidata-schema.json). */
export interface UiElement {
  Id?: number | string;
  Type?: string;
  Label?: string;
  Description?: string;
  /** Optional attribute-name binding for the value key. */
  Attribute?: string;
  attribute?: string;
  Children?: UiElement[];
  // TextBox / TextArea
  Placeholder?: string;
  MaxLength?: number;
  Rows?: number;
  Cols?: number;
  // InputGroup
  InputElementType?: 'Text' | 'Email' | 'Number' | 'Url';
  // DropdownOption
  Value?: unknown;
  DisplayText?: string;
  // Checkbox / RadioButton
  Checked?: boolean;
  GroupName?: string;
  // DatePicker
  MinDate?: string;
  MaxDate?: string;
  // Slider
  MinValue?: number;
  MaxValue?: number;
  Step?: number;
  // Section
  Heading?: string;
  [key: string]: unknown;
}

/** Resolves the value key for an element (see header contract). */
export function resolveValueKey(el: UiElement): string {
  if (typeof el.Attribute === 'string' && el.Attribute.length > 0) return el.Attribute;
  if (typeof el.attribute === 'string' && el.attribute.length > 0) return el.attribute;
  return String(el.Id ?? '');
}

export interface FormRendererProps {
  /** The UIPage document (PascalCase keys). */
  page: Record<string, unknown>;
  /** Current values keyed by resolveValueKey. */
  values: Record<string, unknown>;
  /** Called when a control changes. Omit for pure display. */
  onChange?: (key: string, value: unknown) => void;
  /** Bound attribute contract — supplies placeholder/help/required/readOnly per key. */
  attributes?: EntityAttributeData[];
  /** Force every control read-only (preview mode). */
  readOnly?: boolean;
}
export function FormRenderer({ page, values, onChange, attributes = [], readOnly }: FormRendererProps) {
  const rootElements = Array.isArray(page.RootElements) ? (page.RootElements as UiElement[]) : [];
  const attrByKey: Record<string, EntityAttributeData> = {};
  for (const a of attributes) attrByKey[a.attributeName] = a;

  return (
    <div className="space-y-3">
      {rootElements.length === 0 && (
        <p className="text-xs text-gray-500 italic">No root elements in this page.</p>
      )}
      {rootElements.map((el) => (
        <ElementView key={String(el.Id ?? el.Label)} element={el} values={values} onChange={onChange} attrByKey={attrByKey} readOnly={readOnly} />
      ))}
    </div>
  );
}

interface ElementViewProps {
  element: UiElement;
  values: Record<string, unknown>;
  onChange?: (key: string, value: unknown) => void;
  attrByKey: Record<string, EntityAttributeData>;
  readOnly: boolean | undefined;
}

function ElementView({ element, values, onChange, attrByKey, readOnly }: ElementViewProps) {
  const key = resolveValueKey(element);
  const attr = attrByKey[key];
  const isReadOnly = readOnly || (attr?.readOnly ?? false);
  const validation = parseAttributeValidation(attr?.validationSchemaJson);
  const required = validation?.required === true;
  const label = element.Label || attr?.displayName || key;
  const placeholder = element.Placeholder || attr?.placeholder || '';
  const helpText = element.Description || attr?.helpText || '';

  // Containers render their children; inputs render a control.
  switch (element.Type) {
    case 'Section':
      return (
        <fieldset className="border border-gray-700 rounded-md p-3 space-y-3">
          <legend className="px-1 text-xs font-semibold text-gray-300">{element.Heading || label}</legend>
          {(element.Children ?? []).map((child) => (
            <ElementView key={String(child.Id ?? child.Label)} element={child} values={values} onChange={onChange} attrByKey={attrByKey} readOnly={readOnly} />
          ))}
        </fieldset>
      );

    case 'Grid':
    case 'Wizard':
    case 'Step':
      return (
        <div className="space-y-3">
          {element.Label && element.Type !== 'Grid' && (
            <p className="text-xs font-semibold text-gray-400">{label}</p>
          )}
          {(element.Children ?? []).map((child) => (
            <ElementView key={String(child.Id ?? child.Label)} element={child} values={values} onChange={onChange} attrByKey={attrByKey} readOnly={readOnly} />
          ))}
        </div>
      );

    case 'GridItem':
      return (
        <div className="space-y-3">
          {(element.Children ?? []).map((child) => (
            <ElementView key={String(child.Id ?? child.Label)} element={child} values={values} onChange={onChange} attrByKey={attrByKey} readOnly={readOnly} />
          ))}
        </div>
      );

    case 'StaticText':
      return (
        <p className="text-xs text-gray-400">
          {label}
          {helpText && <span className="block text-gray-500">{helpText}</span>}
        </p>
      );

    case 'Button':
      return (
        <button type="button" disabled className="px-3 py-1.5 rounded bg-gray-700 text-xs text-gray-400 cursor-not-allowed">
          {label}
        </button>
      );

    case 'Dropdown': {
      const options = (element.Children ?? []).filter((c) => c.Type === 'DropdownOption');
      return (
        <FieldShell label={label} required={required} helpText={helpText}>
          <select
            className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-200 focus:outline-none focus:border-emerald-500"
            value={String(values[key] ?? '')}
            disabled={isReadOnly || !onChange}
            onChange={(e) => onChange?.(key, e.target.value)}
          >
            <option value="">— select —</option>
            {options.map((opt) => (
              <option key={String(opt.Id ?? opt.Value)} value={String(opt.Value ?? '')}>
                {opt.DisplayText || String(opt.Value ?? '')}
              </option>
            ))}
          </select>
        </FieldShell>
      );
    }

    case 'Checkbox':
      return (
        <label className="flex items-center gap-2 text-sm text-gray-300">
          <input
            type="checkbox"
            className="accent-emerald-500"
            checked={Boolean(values[key])}
            disabled={isReadOnly || !onChange}
            onChange={(e) => onChange?.(key, e.target.checked)}
          />
          {label}
          {required && <span className="text-red-400">*</span>}
        </label>
      );

    case 'RadioButton': {
      // A RadioButton element with children of the same GroupName forms a choice group.
      const siblings = (element.Children ?? []).filter((c) => c.Type === 'RadioButton' && c.GroupName);
      if (siblings.length > 0) {
        const groupKey = element.Attribute || element.attribute || element.GroupName || key;
        return (
          <FieldShell label={label} required={required} helpText={helpText}>
            <div className="space-y-1.5">
              {siblings.map((sib) => (
                <label key={String(sib.Id ?? sib.Label)} className="flex items-center gap-2 text-sm text-gray-300">
                  <input
                    type="radio"
                    name={`formgroup-${groupKey}`}
                    className="accent-emerald-500"
                    checked={values[groupKey] === sib.Label}
                    disabled={isReadOnly || !onChange}
                    onChange={() => onChange?.(groupKey, sib.Label)}
                  />
                  {sib.Label}
                </label>
              ))}
            </div>
          </FieldShell>
        );
      }
      // Standalone radio behaves like a checkbox.
      return (
        <label className="flex items-center gap-2 text-sm text-gray-300">
          <input
            type="radio"
            name={`formgroup-${key}`}
            className="accent-emerald-500"
            checked={Boolean(values[key])}
            disabled={isReadOnly || !onChange}
            onChange={(e) => onChange?.(key, e.target.checked)}
          />
          {label}
        </label>
      );
    }

    case 'DatePicker':
      return (
        <FieldShell label={label} required={required} helpText={helpText}>
          <input
            type="date"
            className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-200 focus:outline-none focus:border-emerald-500"
            value={String(values[key] ?? '')}
            min={element.MinDate?.slice(0, 10)}
            max={element.MaxDate?.slice(0, 10)}
            disabled={isReadOnly || !onChange}
            onChange={(e) => onChange?.(key, e.target.value)}
          />
        </FieldShell>
      );

    case 'Slider': {
      const min = element.MinValue ?? 0;
      const max = element.MaxValue ?? 100;
      return (
        <FieldShell label={label} required={required} helpText={helpText}>
          <div className="flex items-center gap-3">
            <input
              type="range"
              className="flex-1 accent-emerald-500"
              min={min}
              max={max}
              step={element.Step ?? 1}
              value={Number(values[key] ?? element.Value ?? min)}
              disabled={isReadOnly || !onChange}
              onChange={(e) => onChange?.(key, Number(e.target.value))}
            />
            <span className="text-xs font-mono text-gray-400 w-10 text-right">{String(values[key] ?? element.Value ?? min)}</span>
          </div>
        </FieldShell>
      );
    }

    case 'FileUpload':
      return (
        <FieldShell label={label} required={required} helpText={helpText}>
          <input
            type="file"
            className="w-full text-xs text-gray-400 file:mr-2 file:px-2 file:py-1 file:rounded file:border-0 file:bg-gray-700 file:text-gray-300"
            disabled={isReadOnly || !onChange}
            onChange={(e) => onChange?.(key, e.target.files?.[0]?.name ?? '')}
          />
        </FieldShell>
      );

    case 'TextBox':
    case 'InputGroup':
    default: {
      // TextBox and InputGroup both render a single-line input; unknown types fall back to text.
      const inputType = element.Type === 'InputGroup' ? (element.InputElementType?.toLowerCase() ?? 'text') : 'text';
      return (
        <FieldShell label={label} required={required} helpText={helpText}>
          <input
            type={inputType as string}
            className="w-full bg-gray-800 border border-gray-700 rounded px-2 py-1.5 text-sm text-gray-200 focus:outline-none focus:border-emerald-500"
            placeholder={placeholder}
            maxLength={element.MaxLength}
            value={String(values[key] ?? '')}
            disabled={isReadOnly || !onChange}
            onChange={(e) => onChange?.(key, e.target.value)}
          />
        </FieldShell>
      );
    }
  }
}

function FieldShell({ label, required, helpText, children }: { label: string; required?: boolean; helpText?: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <label className="block text-xs font-medium text-gray-400">
        {label}
        {required && <span className="text-red-400 ml-0.5">*</span>}
      </label>
      {children}
      {helpText && <p className="text-[10px] text-gray-500">{helpText}</p>}
    </div>
  );
}
