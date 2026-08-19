/**
 * API Registry Service — manages registered API endpoints.
 * In production, this would call the .NET backend API.
 */

export interface ApiRegistration {
  id: string;
  name: string;
  baseUrl: string;
  endpoints: ApiEndpoint[];
  authentication: ApiAuthConfig;
  description?: string;
}

export interface ApiEndpoint {
  path: string;
  method: string;
  description?: string;
}

export interface ApiAuthConfig {
  type: 'none' | 'bearer' | 'basic' | 'apiKey' | 'oauth2';
  token?: string;
  headerName?: string;
}

/**
 * Service for API registry operations.
 */
export const ApiRegistryService = {
  /**
   * List all registered APIs.
   */
  async listApis(): Promise<ApiRegistration[]> {
    try {
      const saved = localStorage.getItem('stepflow-api-registry');
      return saved ? JSON.parse(saved) : [];
    } catch {
      return [];
    }
  },

  /**
   * Get an API by ID.
   */
  async getApi(id: string): Promise<ApiRegistration | undefined> {
    const apis = await ApiRegistryService.listApis();
    return apis.find((api) => api.id === id);
  },

  /**
   * Register a new API.
   */
  async registerApi(api: Omit<ApiRegistration, 'id'>): Promise<ApiRegistration> {
    const apis = await ApiRegistryService.listApis();
    const newApi: ApiRegistration = {
      ...api,
      id: `api-${Date.now()}`,
    };
    apis.push(newApi);
    localStorage.setItem('stepflow-api-registry', JSON.stringify(apis));
    return newApi;
  },

  /**
   * Update an existing API.
   */
  async updateApi(id: string, updates: Partial<ApiRegistration>): Promise<void> {
    const apis = await ApiRegistryService.listApis();
    const index = apis.findIndex((api) => api.id === id);
    if (index >= 0) {
      apis[index] = { ...apis[index], ...updates };
      localStorage.setItem('stepflow-api-registry', JSON.stringify(apis));
    }
  },

  /**
   * Delete an API.
   */
  async deleteApi(id: string): Promise<void> {
    const apis = await ApiRegistryService.listApis();
    const filtered = apis.filter((api) => api.id !== id);
    localStorage.setItem('stepflow-api-registry', JSON.stringify(filtered));
  },

  /**
   * Get dropdown options for API selector.
   */
  async getApiOptions(): Promise<{ label: string; value: string }[]> {
    const apis = await ApiRegistryService.listApis();
    return apis.map((api) => ({
      label: api.name,
      value: api.id,
    }));
  },
};
