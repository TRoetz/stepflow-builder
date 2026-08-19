import { create } from 'zustand';
import { persist } from 'zustand/middleware';

// ── AI Provider Types ──

export type AiProvider = 'openai' | 'azureOpenAI' | 'anthropic' | 'openaiCompatible' | 'ollama' | 'lmStudio' | 'llamaCpp';

export interface AiProviderConfig {
  provider: AiProvider;
  baseUrl: string;
  apiKey: string;
  defaultModel: string;
  temperature: number;
  maxTokens: number;
}

export type AiModelConfig = Pick<
  AiModelConfigState,
  'provider' | 'baseUrl' | 'apiKey' | 'defaultModel' | 'temperature' | 'maxTokens' | 'topP'
>;

export interface AiModelConfigState {
  // Provider configuration
  provider: AiProvider;
  baseUrl: string;
  apiKey: string;
  defaultModel: string;
  temperature: number;
  maxTokens: number;
  topP: number;

  // Modal state
  isConfigModalOpen: boolean;

  // Actions
  setProvider: (provider: AiProvider) => void;
  setBaseUrl: (url: string) => void;
  setApiKey: (key: string) => void;
  setDefaultModel: (model: string) => void;
  setTemperature: (temp: number) => void;
  setMaxTokens: (tokens: number) => void;
  setTopP: (top: number) => void;
  setConfig: (config: Partial<AiProviderConfig>) => void;
  toggleConfigModal: () => void;
  openConfigModal: () => void;
  closeConfigModal: () => void;
  resetConfig: () => void;
}

const defaultConfig: Omit<
  AiModelConfigState,
  | 'setProvider'
  | 'setBaseUrl'
  | 'setApiKey'
  | 'setDefaultModel'
  | 'setTemperature'
  | 'setMaxTokens'
  | 'setTopP'
  | 'setConfig'
  | 'toggleConfigModal'
  | 'openConfigModal'
  | 'closeConfigModal'
  | 'resetConfig'
> = {
  provider: 'openai',
  baseUrl: 'https://api.openai.com/v1',
  apiKey: '',
  defaultModel: 'gpt-4-turbo',
  temperature: 0.7,
  maxTokens: 4096,
  topP: 1.0,
  isConfigModalOpen: false,
};
export const useAiModelConfigStore = create<AiModelConfigState>()(
  persist(
    (set) => ({
      ...defaultConfig,

      setProvider: (provider) => set({ provider }),
      setBaseUrl: (baseUrl) => set({ baseUrl }),
      setApiKey: (apiKey) => set({ apiKey }),
      setDefaultModel: (defaultModel) => set({ defaultModel }),
      setTemperature: (temperature) => set({ temperature }),
      setMaxTokens: (maxTokens) => set({ maxTokens }),
      setTopP: (topP) => set({ topP }),
      setConfig: (partial) => set(() => partial as Partial<AiModelConfigState>),

      toggleConfigModal: () => set((s) => ({ isConfigModalOpen: !s.isConfigModalOpen })),
      openConfigModal: () => set({ isConfigModalOpen: true }),
      closeConfigModal: () => set({ isConfigModalOpen: false }),
      resetConfig: () => set(defaultConfig),
    }),
    {
      name: 'stepflow-ai-config',
      partialize: (state) => ({
        provider: state.provider,
        baseUrl: state.baseUrl,
        apiKey: state.apiKey,
        defaultModel: state.defaultModel,
        temperature: state.temperature,
        maxTokens: state.maxTokens,
        topP: state.topP,
      }),
    }
  )
);
