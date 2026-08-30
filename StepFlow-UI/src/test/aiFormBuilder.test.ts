import { describe, it, expect } from 'vitest';
import { extractFormPlanJson, validateFormPlan } from '@services/aiFormBuilder';

const validPlan = {
  form: {
    formId: 'order-intake',
    title: 'Order Intake',
    description: 'Captures customer orders.',
    attributeDomainName: 'OrderIntake',
    page: {
      Id: 1,
      Title: 'Order Intake',
      PageName: 0,
      PageVersion: '1.0',
      RootElements: [{ Id: 10, Type: 'TextBox', Attribute: 'orderNumber', Label: 'Order Number' }],
    },
  },
  domain: {
    schemaDefinition: null,
    attributeDomain: {
      version: '1',
      attributeDomainName: 'OrderIntake',
      description: '',
      isCurrentVersion: true,
      attributes: [
        { attributeName: 'orderNumber', dataType: 0, displayName: 'Order Number', placeholder: '', helpText: '', visible: true, readOnly: false, primaryKey: true },
      ],
    },
  },
  flow: {
    startAt: 'CaptureOrder',
    states: {
      CaptureOrder: { type: 'FormCapture', task: { formId: 'order-intake', title: 'Order Intake' }, next: 'SaveOrder' },
      SaveOrder: { type: 'Task', resource: 'eav://OrderIntake', parameters: { operation: 'write', entityType: 'OrderIntake' }, next: 'Done' },
      Done: { type: 'Succeed' },
    },
  },
};

describe('extractFormPlanJson', () => {
  it('parses a bare JSON object', () => {
    expect(extractFormPlanJson(JSON.stringify(validPlan))).toEqual(validPlan);
  });

  it('extracts from a fenced code block with surrounding prose', () => {
    const raw = `Here is the plan:\n\`\`\`json\n${JSON.stringify(validPlan)}\n\`\`\`\nLet me know if you need changes.`;
    expect(extractFormPlanJson(raw)).toEqual(validPlan);
  });

  it('slices from first { to last } when prose wraps the object', () => {
    const raw = `Sure! ${JSON.stringify(validPlan)} hope that helps`;
    expect(extractFormPlanJson(raw)).toEqual(validPlan);
  });

  it('throws on empty response', () => {
    expect(() => extractFormPlanJson('   ')).toThrow(/empty response/);
  });

  it('throws when no JSON object is present', () => {
    expect(() => extractFormPlanJson('I cannot help with that.')).toThrow(/no JSON object/);
    expect(() => extractFormPlanJson('[1,2,3]')).toThrow(/no JSON object/);
  });

  it('throws on invalid JSON', () => {
    expect(() => extractFormPlanJson('{not json}')).toThrow(/Invalid JSON/);
  });

  it('rejects non-object roots (arrays)', () => {
    expect(() => extractFormPlanJson('```json\n[1,2]\n```')).toThrow(/not a JSON object/);
  });
});

describe('validateFormPlan', () => {
  it('accepts a valid form + domain + flow plan and normalizes the draft', () => {
    const plan = validateFormPlan(validPlan);
    expect(plan.form.formId).toBe('order-intake');
    expect(plan.form.title).toBe('Order Intake');
    expect(plan.form.attributeDomainName).toBe('OrderIntake');
    expect(plan.domain?.attributeDomain?.attributeDomainName).toBe('OrderIntake');
    expect(plan.flow?.startAt).toBe('CaptureOrder');
  });

  it('accepts a form-only plan (domain and flow null)', () => {
    const plan = validateFormPlan({ form: validPlan.form, domain: null, flow: null });
    expect(plan.domain).toBeNull();
    expect(plan.flow).toBeNull();
    expect(plan.form.attributeDomainName).toBe('OrderIntake'); // falls back to the form's own binding
  });

  it('throws when the form section is missing', () => {
    expect(() => validateFormPlan({ domain: null, flow: null })).toThrow(/missing the "form" section/);
  });

  it('throws on empty formId or title', () => {
    const noId = JSON.parse(JSON.stringify(validPlan));
    noId.form.formId = '   ';
    expect(() => validateFormPlan(noId)).toThrow(/non-empty formId/);

    const noTitle = JSON.parse(JSON.stringify(validPlan));
    noTitle.form.title = '';
    expect(() => validateFormPlan(noTitle)).toThrow(/missing a title/);
  });

  it('throws when the page has no RootElements', () => {
    const bad = JSON.parse(JSON.stringify(validPlan));
    delete bad.form.page.RootElements;
    expect(() => validateFormPlan(bad)).toThrow(/no RootElements/);
  });

  it('throws when form binding and domain name disagree', () => {
    const mismatched = JSON.parse(JSON.stringify(validPlan));
    mismatched.domain.attributeDomain.attributeDomainName = 'SomethingElse';
    expect(() => validateFormPlan(mismatched)).toThrow(/binds domain/);
  });

  it('throws when a domain attribute lacks its dataType ordinal', () => {
    const bad = JSON.parse(JSON.stringify(validPlan));
    delete bad.domain.attributeDomain.attributes[0].dataType;
    expect(() => validateFormPlan(bad)).toThrow(/missing its dataType ordinal/);
  });

  it('throws when the flow start state is not defined in states', () => {
    const bad = JSON.parse(JSON.stringify(validPlan));
    bad.flow.startAt = 'Missing';
    expect(() => validateFormPlan(bad)).toThrow(/not defined in states/);
  });

  it('throws when the first state is not a FormCapture', () => {
    const bad = JSON.parse(JSON.stringify(validPlan));
    bad.flow.states.CaptureOrder.type = 'Task';
    expect(() => validateFormPlan(bad)).toThrow(/must start with a FormCapture/);
  });

  it('throws when the capture step references a different form id', () => {
    const bad = JSON.parse(JSON.stringify(validPlan));
    bad.flow.states.CaptureOrder.task.formId = 'other-form';
    expect(() => validateFormPlan(bad)).toThrow(/must reference the generated form id/);
  });
});
