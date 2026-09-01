import { afterEach, describe, expect, it, vi } from 'vitest';
import { callAiApi, listLocalModels, testAiConnection } from '@stores/useAiAssistantStore';
import type { AiModelConfig } from '@stores/useAiModelConfigStore';

// Minimal fetch response shape — the store only reads .ok/.status and .json()
const jsonResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body });
const httpError = (status: number, body: unknown) => ({ ok: false, status, json: async () => body });

/** Records every call and delegates to a URL router. */
function recordingFetch(handler: (url: string, init?: RequestInit) => { ok: boolean; status: number; json: () => Promise<unknown> }) {
  return vi.fn(async (input: string | URL | Request, init?: RequestInit) => handler(String(input), init));
}

const localConfig: AiModelConfig = {
  provider: 'lmStudio',
  baseUrl: 'http://192.168.10.34:1234',
  apiKey: '',
  defaultModel: 'qwen/qwen3-8b',
  temperature: 0.7,
  maxTokens: 1000,
  topP: 1.0,
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('local-provider proxy routing (remote LAN endpoints without CORS)', () => {
  it('routes chat calls through the backend relay and returns upstream content', async () => {
    const fetchMock = recordingFetch((url, init) => {
      if (url === '/api/ai/chat') {
        expect(JSON.parse(String(init?.body))).toMatchObject({
          provider: 'lmStudio',
          baseUrl: 'http://192.168.10.34:1234',
          model: 'qwen/qwen3-8b',
        });
        return jsonResponse({ choices: [{ message: { content: '{"ok":true}' }, finish_reason: 'stop' }] });
      }
      throw new Error(`unexpected direct call to ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const result = await callAiApi(localConfig, 'system prompt', 'hello');

    expect(result).toBe('{"ok":true}');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('falls back to a direct browser call when the relay is unavailable (404)', async () => {
    const fetchMock = recordingFetch((url) => {
      if (url === '/api/ai/chat') return httpError(404, {});
      expect(url).toBe('http://192.168.10.34:1234/v1/chat/completions');
      return jsonResponse({ choices: [{ message: { content: 'direct' }, finish_reason: 'stop' }] });
    });
    vi.stubGlobal('fetch', fetchMock);

    const result = await callAiApi(localConfig, 'system prompt', 'hello');

    expect(result).toBe('direct');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('surfaces upstream errors from the relay without falling back to a direct call', async () => {
    const fetchMock = recordingFetch((url) => {
      if (url === '/api/ai/chat') return httpError(502, { error: { message: 'Upstream AI server returned HTTP 404. model not found' } });
      throw new Error(`unexpected direct call to ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    await expect(callAiApi(localConfig, 'system prompt', 'hello')).rejects.toThrow(/model not found/);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('tests local connections through the relay with a minimal prompt', async () => {
    const fetchMock = recordingFetch((url, init) => {
      if (url === '/api/ai/chat') {
        expect(JSON.parse(String(init?.body))).toMatchObject({ maxTokens: 1 });
        return jsonResponse({ choices: [{ message: { content: 'Hi' } }] });
      }
      throw new Error(`unexpected direct call to ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const result = await testAiConnection(localConfig);

    expect(result).toEqual({ success: true, message: 'Connection successful!' });
  });

  it('lists local models through the relay', async () => {
    const fetchMock = recordingFetch((url) => {
      if (String(url).startsWith('/api/ai/models?')) {
        expect(String(url)).toContain(`provider=lmStudio`);
        expect(String(url)).toContain(encodeURIComponent('http://192.168.10.34:1234'));
        return jsonResponse({ models: ['qwen/qwen3-8b', 'gemma-4-e2b-it'] });
      }
      throw new Error(`unexpected direct call to ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const names = await listLocalModels('lmStudio', 'http://192.168.10.34:1234');

    expect(names).toEqual(['qwen/qwen3-8b', 'gemma-4-e2b-it']);
  });

  it('falls back to the direct /v1/models endpoint when the relay is unavailable', async () => {
    const fetchMock = recordingFetch((url) => {
      if (String(url).startsWith('/api/ai/models?')) return httpError(404, {});
      expect(String(url)).toBe('http://192.168.10.34:1234/v1/models');
      return jsonResponse({ data: [{ id: 'qwen/qwen3-8b' }] });
    });
    vi.stubGlobal('fetch', fetchMock);

    const names = await listLocalModels('lmStudio', 'http://192.168.10.34:1234');

    expect(names).toEqual(['qwen/qwen3-8b']);
  });
});
