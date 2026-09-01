import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiModelConfigModal } from '@components/AiModelConfigModal';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';

// Minimal fetch response shape — the modal only reads .ok/.status and .json()
const jsonResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body });
const httpError = (status: number, body: unknown) => ({ ok: false, status, json: async () => body });

/** Serves both the backend relay (/api/ai/models) and the direct endpoints. proxyAvailable=false simulates an older backend without the relay. */
function mockOllamaFetch(names: string[], proxyAvailable = true) {
  return vi.fn(async (url: string | URL) => {
    const u = String(url);
    if (u.includes('/api/ai/models')) {
      return proxyAvailable ? jsonResponse({ models: names }) : httpError(404, {});
    }
    if (u.endsWith('/api/tags')) {
      return jsonResponse({ models: names.map((name) => ({ name })) });
    }
    return jsonResponse({});
  });
}

function mockOpenAiCompatibleFetch(ids: string[], proxyAvailable = true) {
  return vi.fn(async (url: string | URL) => {
    const u = String(url);
    if (u.includes('/api/ai/models')) {
      return proxyAvailable ? jsonResponse({ models: ids }) : httpError(404, {});
    }
    if (u.endsWith('/v1/models')) {
      return jsonResponse({ data: ids.map((id) => ({ id })) });
    }
    return jsonResponse({});
  });
}

function modelSelect() {
  // Two comboboxes in the modal: Provider, then Default Model
  const selects = screen.getAllByRole('combobox');
  return selects[selects.length - 1];
}

beforeEach(() => {
  localStorage.clear();
  useAiModelConfigStore.setState({
    provider: 'ollama',
    baseUrl: 'http://localhost:11434',
    apiKey: '',
    defaultModel: 'llama3.3', // preset that does not exist on the server
    temperature: 0.7,
    maxTokens: 1000,
    topP: 1.0,
    isConfigModalOpen: true,
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('AiModelConfigModal', () => {
  it('lists models installed on the Ollama server and auto-selects one when the saved preset does not exist', async () => {
    vi.stubGlobal('fetch', mockOllamaFetch(['qwen2.5-coder:7b', 'llama3.1:8b']));

    render(<AiModelConfigModal />);

    // Installed models appear in the dropdown (after debounce + fetch)
    await waitFor(
      () => {
        expect(screen.getByRole('option', { name: 'qwen2.5-coder:7b' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'llama3.1:8b' })).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    // Saved preset 'llama3.3' is not installed → auto-switch to first installed model
    await waitFor(
      () => expect(useAiModelConfigStore.getState().defaultModel).toBe('qwen2.5-coder:7b'),
      { timeout: 3000 }
    );
  });

  it('allows typing a custom model name when Custom... is selected', async () => {
    vi.stubGlobal('fetch', mockOllamaFetch(['llama3.1:8b']));
    const user = userEvent.setup();

    render(<AiModelConfigModal />);

    // Wait for the auto-switch to settle so it does not race our selection
    await waitFor(
      () => expect(useAiModelConfigStore.getState().defaultModel).toBe('llama3.1:8b'),
      { timeout: 3000 }
    );

    await user.selectOptions(modelSelect(), 'custom');

    const customInput = screen.getByPlaceholderText('e.g. llama3.1:8b or qwen2.5-coder:7b');
    expect(customInput).toBeInTheDocument();

    await user.type(customInput, 'my-model:13b');
    expect(useAiModelConfigStore.getState().defaultModel).toBe('my-model:13b');
  });

  it('lists models from LM Studio via the OpenAI-compatible /v1/models endpoint', async () => {
    useAiModelConfigStore.setState({ provider: 'lmStudio', baseUrl: 'http://localhost:1234', defaultModel: 'llama-3.3-70b' });
    vi.stubGlobal('fetch', mockOpenAiCompatibleFetch(['qwen/qwen3-8b']));

    render(<AiModelConfigModal />);

    await waitFor(
      () => expect(screen.getByRole('option', { name: 'qwen/qwen3-8b' })).toBeInTheDocument(),
      { timeout: 3000 }
    );
    await waitFor(
      () => expect(useAiModelConfigStore.getState().defaultModel).toBe('qwen/qwen3-8b'),
      { timeout: 3000 }
    );
  });

  it('falls back to listing models directly when the backend relay is unavailable', async () => {
    useAiModelConfigStore.setState({ provider: 'lmStudio', baseUrl: 'http://192.168.10.34:1234', defaultModel: '' });
    vi.stubGlobal('fetch', mockOpenAiCompatibleFetch(['qwen/qwen3-8b'], false));

    render(<AiModelConfigModal />);

    await waitFor(
      () => expect(screen.getByRole('option', { name: 'qwen/qwen3-8b' })).toBeInTheDocument(),
      { timeout: 3000 }
    );
  });

  it('shows a hint instead of failing silently when the server cannot be listed', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('fetch failed');
    }));

    render(<AiModelConfigModal />);

    await waitFor(
      () => expect(screen.getByText(/could not list models/i)).toBeInTheDocument(),
      { timeout: 3000 }
    );
    // Custom entry still available as fallback
    const user = userEvent.setup();
    await user.selectOptions(modelSelect(), 'custom');
    expect(screen.getByPlaceholderText('e.g. llama3.1:8b or qwen2.5-coder:7b')).toBeInTheDocument();
  });
});
