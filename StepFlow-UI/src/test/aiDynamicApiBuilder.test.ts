import { describe, it, expect } from 'vitest';
import { extractOpJson, validateOpDraft, buildUserMessage, toOpDraft, type AiApiShell, type AiOpContext, type AiOpDraft } from '@services/aiDynamicApiBuilder';

describe('extractOpJson', () => {
  const op = { method: 'GET', path: '/{id}', handlerType: 'eav' };

  it('parses a bare JSON object', () => {
    expect(extractOpJson(JSON.stringify(op))).toEqual(op);
  });

  it('extracts from a fenced code block with surrounding prose', () => {
    const raw = `Here is the operation:\n\`\`\`json\n${JSON.stringify(op)}\n\`\`\`\nLet me know if you need changes.`;
    expect(extractOpJson(raw)).toEqual(op);
  });

  it('slices from first { to last } when prose wraps the object', () => {
    const raw = `Sure! ${JSON.stringify(op)} hope that helps`;
    expect(extractOpJson(raw)).toEqual(op);
  });

  it('throws on empty response', () => {
    expect(() => extractOpJson('   ')).toThrow(/empty response/);
  });

  it('throws when no JSON object is present', () => {
    expect(() => extractOpJson('I cannot help with that.')).toThrow(/no JSON object/);
    expect(() => extractOpJson('[1,2,3]')).toThrow(/no JSON object/);
  });

  it('throws on invalid JSON', () => {
    expect(() => extractOpJson('{not json}')).toThrow(/Invalid JSON/);
  });

  it('rejects non-object roots (arrays)', () => {
    expect(() => extractOpJson('```json\n[1,2]\n```')).toThrow(/not a JSON object/);
  });
});

describe('toOpDraft', () => {
  it('fills missing keys with empty defaults and trims strings', () => {
    const draft = toOpDraft({ method: ' GET ', path: '/x', handlerType: 'EAV' });
    expect(draft).toEqual({ method: 'GET', path: '/x', handlerType: 'eav', flowId: null, domainName: null, profileId: null, description: null });
  });

  it('keeps optional identifiers when present', () => {
    const draft = toOpDraft({ method: 'POST', path: '', handlerType: 'flow', flowId: 'f-1' });
    expect(draft.flowId).toBe('f-1');
  });
});

describe('validateOpDraft', () => {
  const shell: AiApiShell = { basePath: '/orders', attributeDomain: 'OrderData', existingOps: [] };
  const validEavGet: AiOpDraft = { method: 'GET', path: '', handlerType: 'eav' };

  it('accepts a minimal eav GET on the api-level domain', () => {
    expect(validateOpDraft(validEavGet, shell)).toEqual([]);
  });

  it('rejects an unknown method', () => {
    expect(validateOpDraft({ ...validEavGet, method: 'HEAD' }, shell)).toContainEqual(expect.stringMatching(/method must be one of/));
  });

  it('accepts a lowercase method (server upper-cases)', () => {
    expect(validateOpDraft({ ...validEavGet, method: 'get' }, shell)).toEqual([]);
  });

  it('rejects a non-empty path that does not start with /', () => {
    const errors = validateOpDraft({ ...validEavGet, path: 'items' }, shell);
    expect(errors).toContainEqual(expect.stringMatching('start with "/"'));
  });

  it('accepts an empty path (the api base path)', () => {
    expect(validateOpDraft({ method: 'GET', path: '', handlerType: 'eav' }, shell)).toEqual([]);
  });

  it('rejects a template parameter that violates ^[A-Za-z_][A-Za-z0-9_]*$', () => {
    const errors = validateOpDraft({ ...validEavGet, path: '/{1abc}' }, shell);
    expect(errors).toContainEqual(expect.stringMatching(/template parameter "\{1abc\}"/));
  });

  it('accepts a valid template parameter', () => {
    expect(validateOpDraft({ ...validEavGet, path: '/{id}' }, shell)).toEqual([]);
  });

  it('rejects an unknown handlerType', () => {
    const errors = validateOpDraft({ method: 'GET', path: '', handlerType: 'soap' as never }, shell);
    expect(errors).toContainEqual(expect.stringMatching(/handlerType must be one of/));
  });

  it('requires flowId for the flow handler', () => {
    const errors = validateOpDraft({ method: 'POST', path: '', handlerType: 'flow' }, shell);
    expect(errors).toContainEqual(expect.stringMatching(/flowId is required/));
  });

  it('accepts a flow op with an existing flowId', () => {
    expect(validateOpDraft({ method: 'POST', path: '/submit', handlerType: 'flow', flowId: 'f-1' }, shell)).toEqual([]);
  });

  it('requires profileId for the dataExchange handler', () => {
    const errors = validateOpDraft({ method: 'POST', path: '', handlerType: 'dataExchange' }, shell);
    expect(errors).toContainEqual(expect.stringMatching(/profileId is required/));
  });

  it('requires a domain for eav and attributeDomain handlers (op-level or api-level)', () => {
    const noApiDomain = { ...shell, attributeDomain: null };
    expect(validateOpDraft({ method: 'GET', path: '', handlerType: 'eav' }, noApiDomain))
      .toContainEqual(expect.stringMatching(/needs a domain/));
    expect(validateOpDraft({ method: 'GET', path: '', handlerType: 'attributeDomain' }, noApiDomain))
      .toContainEqual(expect.stringMatching(/needs a domain/));
  });

  it('lets an op-level domainName override satisfy the domain requirement', () => {
    const noApiDomain = { ...shell, attributeDomain: null };
    expect(validateOpDraft({ method: 'GET', path: '', handlerType: 'eav', domainName: 'Other' }, noApiDomain)).toEqual([]);
  });

  it('rejects an eav GET with two template parameters in the full path', () => {
    const errors = validateOpDraft({ ...validEavGet, path: '/{a}/{b}' }, shell);
    expect(errors).toContainEqual(expect.stringMatching(/at most one \{param\}/));
  });

  it('allows two template parameters for non-GET eav ops (e.g. {rowKeyId} plus a literal)', () => {
    // PUT /{rowKeyId} is the documented EAV update shape; only GET is limited to one param.
    expect(validateOpDraft({ method: 'PUT', path: '/{rowKeyId}', handlerType: 'eav' }, shell)).toEqual([]);
  });

  it('rejects the reserved /apis management prefix (exact and nested)', () => {
    const apisShell = { ...shell, basePath: '/apis' };
    expect(validateOpDraft({ ...validEavGet, path: '' }, apisShell))
      .toContainEqual(expect.stringMatching(/reserved "\/apis"/));
    expect(validateOpDraft({ ...validEavGet, path: '/x' }, apisShell))
      .toContainEqual(expect.stringMatching(/reserved "\/apis"/));
  });

  it('rejects a duplicate route within the payload (same method + normalized full path)', () => {
    const withExisting: AiApiShell = { ...shell, existingOps: [{ method: 'GET', path: '/items', handlerType: 'eav' }] };
    expect(validateOpDraft({ ...validEavGet, path: '/items' }, withExisting))
      .toContainEqual(expect.stringMatching(/route conflict/));
  });

  it('allows the same path under a different method', () => {
    const withExisting: AiApiShell = { ...shell, existingOps: [{ method: 'GET', path: '', handlerType: 'eav' }] };
    expect(validateOpDraft({ method: 'POST', path: '', handlerType: 'eav' }, withExisting)).toEqual([]);
  });

  it('normalizes the full path before comparing (trailing slash, duplicate slashes)', () => {
    const withExisting: AiApiShell = { ...shell, existingOps: [{ method: 'GET', path: '/items/', handlerType: 'eav' }] };
    expect(validateOpDraft({ ...validEavGet, path: '//items' }, withExisting))
      .toContainEqual(expect.stringMatching(/route conflict/));
  });
});

describe('buildUserMessage', () => {
  const ctx: AiOpContext = {
    domains: [{ name: 'OrderData', attributes: [{ name: 'orderNumber', dataType: 0 }, { name: 'total', dataType: 2 }] }],
    sampleRows: { OrderData: [{ rowKeyId: 'r1', entityId: 'A-1', total: 9 }] },
    flows: [{ id: 'f-1', name: 'Order Intake' }],
    profiles: [{ id: 'Import Orders', name: 'Import Orders' }],
  };

  it('contains the method, intent, shell and context block (domains+attributes, sample rows, flows, profiles)', () => {
    const msg = buildUserMessage('GET', 'list recent orders', ctx, { basePath: '/orders', attributeDomain: 'OrderData', existingOps: [] });
    expect(msg).toContain('Create a GET operation');
    expect(msg).toContain('Intent: list recent orders');
    expect(msg).toContain('basePath "/orders"');
    expect(msg).toContain('api-level attributeDomain "OrderData"');
    const contextLine = msg.split('\n').find((l) => l.startsWith('{')) ?? '';
    const parsed = JSON.parse(contextLine);
    expect(parsed.domains[0]).toEqual({ name: 'OrderData', attributes: [{ name: 'orderNumber', dataType: 0 }, { name: 'total', dataType: 2 }] });
    expect(parsed.sampleRows.OrderData).toHaveLength(1);
    expect(parsed.flows).toEqual([{ id: 'f-1', name: 'Order Intake' }]);
    expect(parsed.profiles).toEqual([{ id: 'Import Orders', name: 'Import Orders' }]);
  });

  it('lists existing routes to avoid (normalized full paths)', () => {
    const msg = buildUserMessage(
      'POST',
      'create an order',
      ctx,
      { basePath: '/orders', attributeDomain: null, existingOps: [{ method: 'GET', path: '/{id}', handlerType: 'eav' }] },
    );
    expect(msg).toContain('Existing routes (do not duplicate): GET /orders/{id}');
  });

  it('says there are no existing operations when the list is empty', () => {
    const msg = buildUserMessage('GET', 'x', ctx, { basePath: '/', attributeDomain: null, existingOps: [] });
    expect(msg).toContain('No existing operations yet.');
  });
});
