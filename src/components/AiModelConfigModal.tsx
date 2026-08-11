import { useCallback, useMemo, useState } from 'react';
import { X, Save, RotateCcw, Globe, Key, Cpu, Thermometer, Hash, SlidersHorizontal, Plug, CheckCircle, AlertCircle } from 'lucide-react';
import {
  useAiModelConfigStore,
  type AiProvider,
} from '@stores/useAiModelConfigStore';
import { testAiConnection } from '@stores/useAiAssistantStore';

const PROVIDER_OPTIONS: { value: AiProvider; label: string; defaultUrl: string }[] = [
  { value: 'openai', label: 'OpenAI', defaultUrl: 'https://api.openai.com/v1' },
  { value: 'azureOpenAI', label: 'Azure OpenAI', defaultUrl: 'https://YOUR_RESOURCE.openai.azure.com' },
  { value: 'anthropic', label: 'Anthropic', defaultUrl: 'https://api.anthropic.com' },
  { value: 'ollama', label: 'Ollama (Local)', defaultUrl: 'http://localhost:11434/v1' },
  { value: 'lmStudio', label: 'LM Studio (Local)', defaultUrl: 'http://localhost:1234/v1' },
  { value: 'llamaCpp', label: 'llama.cpp (Local)', defaultUrl: 'http://localhost:8080/v1' },
  { value: 'openaiCompatible', label: 'OpenAI Compatible', defaultUrl: 'http://localhost:8080/v1' },
];

const MODEL_OPTIONS: Record<AiProvider, { label: string; value: string }[]> = {
  openai: [
    { label: 'GPT-4 Turbo', value: 'gpt-4-turbo' },
    { label: 'GPT-4', value: 'gpt-4' },
    { label: 'GPT-3.5 Turbo', value: 'gpt-3.5-turbo' },
    { label: 'GPT-3.5 Turbo Instruct', value: 'gpt-3.5-turbo-instruct' },
  ],
  azureOpenAI: [
    { label: 'GPT-4 Turbo (Azure)', value: 'gpt-4-turbo' },
    { label: 'GPT-4 (Azure)', value: 'gpt-4' },
    { label: 'GPT-3.5 Turbo (Azure)', value: 'gpt-3.5-turbo' },
  ],
  anthropic: [
    { label: 'Claude 3.5 Sonnet', value: 'claude-3.5-sonnet' },
    { label: 'Claude 3 Opus', value: 'claude-3-opus' },
    { label: 'Claude 3 Sonnet', value: 'claude-3-sonnet' },
    { label: 'Claude 3 Haiku', value: 'claude-3-haiku' },
  ],
  openaiCompatible: [
    { label: 'Default Model', value: 'default' },
    { label: 'GPT-4', value: 'gpt-4' },
    { label: 'GPT-3.5 Turbo', value: 'gpt-3.5-turbo' },
    { label: 'Custom...', value: 'custom' },
  ],
  ollama: [
    { label: 'Llama 3.3 70B', value: 'llama3.3' },
    { label: 'Llama 3.1 8B', value: 'llama3.1' },
    { label: 'Mistral 7B', value: 'mistral' },
    { label: 'Phi 3 Mini', value: 'phi3' },
    { label: 'Gemma 2 9B', value: 'gemma2' },
    { label: 'Custom...', value: 'custom' },
  ],
  lmStudio: [
    { label: 'Llama 3.3 70B', value: 'llama-3.3-70b' },
    { label: 'Llama 3.1 8B', value: 'llama-3.1-8b' },
    { label: 'Mistral 7B', value: 'mistral-7b' },
    { label: 'Phi 3 Mini', value: 'phi-3-mini' },
    { label: 'Custom...', value: 'custom' },
  ],
  llamaCpp: [
    { label: 'Default Model', value: 'default' },
    { label: 'Llama 3.3 70B', value: 'llama-3.3-70b' },
    { label: 'Llama 3.1 8B', value: 'llama-3.1-8b' },
    { label: 'Mistral 7B', value: 'mistral-7b' },
    { label: 'Custom...', value: 'custom' },
  ],
};

export function AiModelConfigModal() {
  const {
    provider,
    baseUrl,
    apiKey,
    defaultModel,
    temperature,
    maxTokens,
    topP,
    isConfigModalOpen,
    setProvider,
    setBaseUrl,
    setApiKey,
    setDefaultModel,
    setTemperature,
    setMaxTokens,
    setTopP,
    closeConfigModal,
  } = useAiModelConfigStore();

  const [testStatus, setTestStatus] = useState<{ loading: boolean; result?: { success: boolean; message: string } }>({ loading: false });

  const handleTestConnection = useCallback(async () => {
    setTestStatus({ loading: true, result: undefined });
    const config = {
      provider,
      baseUrl,
      apiKey,
      defaultModel,
      temperature,
      maxTokens,
      topP,
    };
    const result = await testAiConnection(config);
    setTestStatus({ loading: false, result });
  }, [provider, baseUrl, apiKey, defaultModel, temperature, maxTokens, topP]);

  const availableModels = useMemo(
    () => MODEL_OPTIONS[provider] ?? MODEL_OPTIONS.openai,
    [provider]
  );

  const handleProviderChange = useCallback(
    (newProvider: AiProvider) => {
      const providerInfo = PROVIDER_OPTIONS.find((p) => p.value === newProvider);
      setProvider(newProvider);
      if (providerInfo) {
        setBaseUrl(providerInfo.defaultUrl);
      }
      // Reset model to first available for the new provider
      const models = MODEL_OPTIONS[newProvider] ?? MODEL_OPTIONS.openai;
      if (models.length > 0) {
        setDefaultModel(models[0].value);
      }
    },
    [setProvider, setBaseUrl, setDefaultModel]
  );

  const handleReset = useCallback(() => {
    setProvider('openai');
    setBaseUrl('https://api.openai.com/v1');
    setApiKey('');
    setDefaultModel('gpt-4-turbo');
    setTemperature(0.7);
    setMaxTokens(1000);
    setTopP(1.0);
  }, [setProvider, setBaseUrl, setApiKey, setDefaultModel, setTemperature, setMaxTokens, setTopP]);


  if (!isConfigModalOpen) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-[1000] flex items-center justify-center">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm"
        onClick={closeConfigModal}
      />

      {/* Modal */}
      <div className="relative z-[1001] w-full max-w-lg mx-4 bg-gray-900 border border-gray-700/50 rounded-xl shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-700/50">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-indigo-500/20">
              <Cpu className="w-5 h-5 text-indigo-400" />
            </div>
            <div>
              <h2 className="text-lg font-semibold text-gray-100">AI Model Configuration</h2>
              <p className="text-xs text-gray-400">Configure inference provider and default model settings</p>
            </div>
          </div>
          <button
            className="btn-icon text-2xl"
            title="Close"
            onClick={closeConfigModal}
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="px-6 py-5 space-y-5 max-h-[60vh] overflow-y-auto">
          {/* Provider Selection */}
          <div>
            <label className="text-xs text-gray-400 block mb-1.5">
              <span className="flex items-center gap-1.5">
                <Globe className="w-3.5 h-3.5" />
                Provider
              </span>
            </label>
            <select
              value={provider}
              onChange={(e) => handleProviderChange(e.target.value as AiProvider)}
              className="w-full px-3 py-2 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
            >
              {PROVIDER_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>

          {/* Base URL */}
          <div>
            <label className="text-xs text-gray-400 block mb-1.5">
              <span className="flex items-center gap-1.5">
                <Globe className="w-3.5 h-3.5" />
                API Base URL
              </span>
            </label>
            <input
              type="url"
              value={baseUrl}
              onChange={(e) => setBaseUrl(e.target.value)}
              className="w-full px-3 py-2 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent font-mono"
              placeholder="https://api.openai.com/v1"
            />
          </div>

          {/* API Key */}
          <div>
            <label className="text-xs text-gray-400 block mb-1.5">
              <span className="flex items-center gap-1.5">
                <Key className="w-3.5 h-3.5" />
                API Key
              </span>
            </label>
            <input
              type="password"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              className="w-full px-3 py-2 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent font-mono"
              placeholder="sk-..."
            />
          </div>

          {/* Model Selection */}
          <div>
            <label className="text-xs text-gray-400 block mb-1.5">
              <span className="flex items-center gap-1.5">
                <Cpu className="w-3.5 h-3.5" />
                Default Model
              </span>
            </label>
            <select
              value={defaultModel}
              onChange={(e) => setDefaultModel(e.target.value)}
              className="w-full px-3 py-2 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
            >
              {availableModels.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>

          {/* Divider */}
          <div className="flex items-center gap-3">
            <div className="flex-1 h-px bg-gray-700/50" />
            <span className="text-xs text-gray-500 flex items-center gap-1">
              <SlidersHorizontal className="w-3.5 h-3.5" />
              Generation Parameters
            </span>
            <div className="flex-1 h-px bg-gray-700/50" />
          </div>

          {/* Temperature */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-xs text-gray-400 flex items-center gap-1.5">
                <Thermometer className="w-3.5 h-3.5" />
                Temperature
              </label>
              <span className="text-xs font-mono text-gray-300">{temperature.toFixed(1)}</span>
            </div>
            <input
              type="range"
              min="0"
              max="2"
              step="0.1"
              value={temperature}
              onChange={(e) => setTemperature(parseFloat(e.target.value))}
              className="w-full accent-indigo-500"
            />
            <div className="flex justify-between text-[10px] text-gray-500 mt-0.5">
              <span>Deterministic</span>
              <span>Creative</span>
            </div>
          </div>

          {/* Max Tokens */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-xs text-gray-400 flex items-center gap-1.5">
                <Hash className="w-3.5 h-3.5" />
                Max Tokens
              </label>
              <span className="text-xs font-mono text-gray-300">{maxTokens}</span>
            </div>
            <input
              type="range"
              min="1"
              max="4096"
              step="1"
              value={maxTokens}
              onChange={(e) => setMaxTokens(parseInt(e.target.value))}
              className="w-full accent-indigo-500"
            />
            <div className="flex justify-between text-[10px] text-gray-500 mt-0.5">
              <span>1</span>
              <span>4096</span>
            </div>
          </div>

          {/* Top P */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-xs text-gray-400 flex items-center gap-1.5">
                <SlidersHorizontal className="w-3.5 h-3.5" />
                Top P
              </label>
              <span className="text-xs font-mono text-gray-300">{topP.toFixed(2)}</span>
            </div>
            <input
              type="range"
              min="0"
              max="1"
              step="0.01"
              value={topP}
              onChange={(e) => setTopP(parseFloat(e.target.value))}
              className="w-full accent-indigo-500"
            />
            <div className="flex justify-between text-[10px] text-gray-500 mt-0.5">
              <span>Focused</span>
              <span>Exploratory</span>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="px-6 py-4 border-t border-gray-700/50 space-y-3">
          {/* Test Connection Result */}
          {testStatus.result && (
            <div className={`flex items-center gap-2 text-xs px-3 py-2 rounded-lg ${testStatus.result.success ? 'bg-green-500/10 text-green-400' : 'bg-red-500/10 text-red-400'}`}>
              {testStatus.result.success ? <CheckCircle className="w-4 h-4 shrink-0" /> : <AlertCircle className="w-4 h-4 shrink-0" />}
              <span>{testStatus.result.message}</span>
            </div>
          )}

          <div className="flex items-center justify-between">
            <button
              className="btn btn-ghost text-sm flex items-center gap-1.5"
              onClick={handleReset}
              title="Reset to defaults"
            >
              <RotateCcw className="w-3.5 h-3.5" />
              Reset
            </button>
            <div className="flex items-center gap-2">
              <button
                className="btn btn-ghost text-sm flex items-center gap-1.5"
                onClick={handleTestConnection}
                disabled={testStatus.loading}
                title="Test connection"
              >
                <Plug className="w-3.5 h-3.5" />
                {testStatus.loading ? 'Testing...' : 'Test Connection'}
              </button>
              <button
                className="btn btn-ghost text-sm"
                onClick={closeConfigModal}
              >
                Cancel
              </button>
              <button
                className="btn btn-primary text-sm flex items-center gap-1.5"
                onClick={closeConfigModal}
              >
                <Save className="w-3.5 h-3.5" />
                Save
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
