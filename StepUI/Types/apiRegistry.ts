/**
 * API Registry Types
 * Defines API management structures
 */

import { APIRegistryEntry } from "./stepConfig";

export interface APIRegistry {
  apiId: string;
  name: string;
  description: string;
  endpoint: string;
  method: HTTPMethod;
  path: string;
  headers: Record<string, string>;
  authentication: APIAuthentication;
  parameters: Record<string, string>;
  responseSchema?: object;
  timeout: number;
  retries: number;
  tags: string[];
  isActive: boolean;
  version: string;
  category?: string;
  documentationUrl?: string;
  lastUsed?: string;
  usageCount?: number;
}

export type HTTPMethod = "GET" | "POST" | "PUT" | "DELETE" | "PATCH";

export type APIAuthentication = {
  type: "none" | "bearer" | "basic" | "oauth2" | "apikey" | "certificate";
  tokenSource?: string;
  tokenPath?: string;
  apiKey?: string;
  apiSecret?: string;
  certificatePath?: string;
  certificatePassword?: string;
  scopes?: string[];
  audience?: string;
  issuer?: string;
};

export interface APIRegistryService {
  /**
   * Register a new API
   */
  registerAPI(api: APIRegistryEntry): Promise<APIRegistryEntry>;

  /**
   * Get API by ID
   */
  getAPI(apiId: string): Promise<APIRegistryEntry | null>;

  /**
   * Search APIs
   */
  searchAPIs(query: string): Promise<APIRegistryEntry[]>;

  /**
   * Get APIs by category
   */
  getAPIsByCategory(category: string): Promise<APIRegistryEntry[]>;

  /**
   * Get active APIs only
   */
  getActiveAPIs(): Promise<APIRegistryEntry[]>;

  /**
   * Update API
   */
  updateAPI(apiId: string, updates: Partial<APIRegistryEntry>): Promise<APIRegistryEntry>;

  /**
   * Delete API
   */
  deleteAPI(apiId: string): Promise<void>;

  /**
   * Enable/Disable API
   */
  toggleAPI(apiId: string, isActive: boolean): Promise<APIRegistryEntry>;

  /**
   * Get API usage statistics
   */
  getUsageStats(apiId: string): Promise<UsageStats | null>;

  /**
   * Test API connection
   */
  testConnection(apiId: string): Promise<ConnectionTestResult>;

  /**
   * Import APIs from JSON
   */
  importAPIs(json: string): Promise<void>;

  /**
   * Export APIs to JSON
   */
  exportAPIs(): Promise<string>;

  /**
   * Bulk import APIs
   */
  bulkImportAPIs(apiEntries: APIRegistryEntry[]): Promise<void>;

  /**
   * Bulk enable/disable APIs
   */
  bulkToggleAPIs(apiIds: string[], isActive: boolean): Promise<void>;
}

export interface UsageStats {
  totalCalls: number;
  successRate: number;
  averageLatency: number;
  lastCall: string;
  errorCount: number;
  timeouts: number;
}

export interface ConnectionTestResult {
  success: boolean;
  message: string;
  latency: number;
  status: string;
  headers: Record<string, string>;
}

export interface APIRegistryConstants {
  DEFAULT_TIMEOUT: number;
  DEFAULT_RETRIES: number;
  DEFAULT_RETRY_DELAY: number;
  MAX_API_NAME_LENGTH: number;
  MAX_API_DESCRIPTION_LENGTH: number;
  REQUIRED_API_FIELDS: string[];
  CATEGORIZATION: string[];
}

export const APIRegistryConstants: APIRegistryConstants = {
  DEFAULT_TIMEOUT: 30,
  DEFAULT_RETRIES: 3,
  DEFAULT_RETRY_DELAY: 1000,
  MAX_API_NAME_LENGTH: 100,
  MAX_API_DESCRIPTION_LENGTH: 500,
  REQUIRED_API_FIELDS: ["name", "endpoint", "method", "path"],
  CATEGORIZATION: [
    "payment",
    "authentication",
    "email",
    "analytics",
    "integration",
    "financial",
    "shipping",
    "inventory",
    "customer",
    "order",
    "reporting",
    "notification",
    "webhook",
    "file",
    "media"
  ]
};

/**
 * API Registry Entry Factory
 */
export const APIRegistryFactory = {
  createAPI: (config: Partial<APIRegistryEntry>): APIRegistryEntry => ({
    apiId: config.apiId || `api_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
    name: config.name || "Untitled API",
    description: config.description || "",
    endpoint: config.endpoint || "",
    method: config.method || "POST",
    path: config.path || "",
    headers: config.headers || { "Content-Type": "application/json" },
    authentication: config.authentication || {
      type: "none"
    },
    parameters: config.parameters || {},
    responseSchema: config.responseSchema,
    timeout: config.timeout || APIRegistryConstants.DEFAULT_TIMEOUT,
    retries: config.retries || APIRegistryConstants.DEFAULT_RETRIES,
    tags: config.tags || [],
    isActive: config.isActive ?? true,
    version: config.version || "1.0",
    category: config.category,
    documentationUrl: config.documentationUrl,
    lastUsed: undefined,
    usageCount: 0,
  }),

  createAuthentication: (config: Partial<APIAuthentication>): APIAuthentication => ({
    type: config.type || "none",
    tokenSource: config.tokenSource,
    tokenPath: config.tokenPath,
    apiKey: config.apiKey,
    apiSecret: config.apiSecret,
    certificatePath: config.certificatePath,
    certificatePassword: config.certificatePassword,
    scopes: config.scopes,
    audience: config.audience,
    issuer: config.issuer,
  }),
};

/**
 * API Registry Validation
 */
export interface APIValidationResult {
  isValid: boolean;
  errors: ValidationError[];
  warnings: ValidationWarning[];
}

export interface ValidationError {
  field: string;
  message: string;
  suggestions?: string[];
}

export interface ValidationWarning {
  message: string;
  suggestions?: string[];
}

export const APIValidation = {
  validate: (api: APIRegistryEntry): APIValidationResult => {
    const errors: ValidationError[] = [];
    const warnings: ValidationWarning[] = [];

    // Check required fields
    const missingRequiredFields = APIRegistryConstants.REQUIRED_API_FIELDS.filter(
      field => !api[field as keyof APIRegistryEntry]
    );

    if (missingRequiredFields.length > 0) {
      errors.push({
        field: "missingFields",
        message: `Missing required fields: ${missingRequiredFields.join(", ")}`,
      });
    }

    // Validate endpoint
    if (api.endpoint && !api.endpoint.startsWith("http")) {
      errors.push({
        field: "endpoint",
        message: "Endpoint must start with http:// or https://",
      });
    }

    // Validate authentication configuration
    const auth = api.authentication || {};
    if (auth.type === "bearer" && !auth.tokenSource) {
      warnings.push({
        message: "Bearer token source not specified",
        suggestions: [
          "Set tokenSource to 'azureKeyVault', 'environmentVariable', or 'header'",
          "Or set tokenPath to a custom token endpoint",
        ],
      });
    }

    if (auth.type === "apikey" && !auth.apiKey) {
      warnings.push({
        message: "API key not specified",
        suggestions: ["Set apiKey in authentication configuration"],
      });
    }

    // Validate name length
    if (api.name && api.name.length > APIRegistryConstants.MAX_API_NAME_LENGTH) {
      errors.push({
        field: "name",
        message: `API name exceeds ${APIRegistryConstants.MAX_API_NAME_LENGTH} characters`,
      });
    }

    // Validate description length
    if (api.description && api.description.length > APIRegistryConstants.MAX_API_DESCRIPTION_LENGTH) {
      warnings.push({
        message: `API description exceeds ${APIRegistryConstants.MAX_API_DESCRIPTION_LENGTH} characters`,
      });
    }

    // Check for overlapping tags
    const duplicateTags = [...new Set(api.tags)].filter(
      (tag, index, array) => array.indexOf(tag) !== index
    );
    if (duplicateTags.length > 0) {
      warnings.push({
        message: `Duplicate tags found: ${duplicateTags.join(", ")}`,
      });
    }

    return {
      isValid: errors.length === 0,
      errors,
      warnings,
    };
  },
};
