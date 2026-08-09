/**
 * Step Configuration Types
 * Defines all step types and their configurations
 */

export type StepType = 
  | "AI"
  | "RULE"
  | "SQL"
  | "API"
  | "PASS"
  | "SCRIPT"
  | "HTTP"
  | "DUCKDB"
  | "JSONAT"
  | "EAV";

export interface BaseStep {
  stepId: string;
  stepType: StepType;
  title: string;
  description?: string;
  resource: string;
  next?: string; // Reference to next step
  previous?: string; // Reference to previous step
  configuration: Record<string, unknown>;
  metadata: {
    createdAt: string;
    createdBy: string;
    lastModified: string;
    version: number;
  };
}

/**
 * AI/LLM Step Configuration
 */
export interface AIStepConfig extends BaseStep {
  stepType: "AI";
  configuration: {
    model: string;
    prompt: string;
    systemPrompt?: string;
    temperature?: number;
    maxTokens?: number;
    promptVariables: Record<string, string>;
    llmService: string;
    outputFormat?: "json" | "text" | "markdown";
    outputSchema?: object;
    contextWindow?: number;
    functionCalls?: boolean;
    tools?: string[];
    decisionLogic?: string;
  };
}

/**
 * Rule Engine Step Configuration
 */
export interface RuleStepConfig extends BaseStep {
  stepType: "RULE";
  configuration: {
    engine: "microsoftRulesEngine" | "rulesEngine" | "custom";
    rules: Rule[];
    defaultOutcome: "pass" | "fail" | "exception";
    testMode: boolean;
    timeout?: number;
    priority?: number;
  };
}

/**
 * SQL Lookup Step Configuration
 */
export interface SQLStepConfig extends BaseStep {
  stepType: "SQL" | "DUCKDB";
  configuration: {
    database: string;
    query: string;
    parameters: Record<string, string>;
    columnsToReturn: string[];
    databaseType: "duckdb" | "sqlite" | "postgresql" | "mysql";
    connectionTimeout?: number;
    useCaching: boolean;
    cacheTTL?: number;
  };
}

/**
 * API Call Step Configuration
 */
export interface APIStepConfig extends BaseStep {
  stepType: "API" | "HTTP";
  configuration: {
    method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
    path: string;
    headers: Record<string, string>;
    parameters: Record<string, string>;
    requestPayload?: Record<string, unknown>;
    responseMapping: Record<string, string>;
    timeout?: number;
    retries?: number;
    retryDelay?: number;
    errorHandling: "continue" | "stop" | "log";
    authenticated: boolean;
    useRegisteredApi: boolean;
    registeredApiId?: string;
    apiKey?: string;
    apiSecret?: string;
  };
}

/**
 * Pass Step Configuration
 */
export interface PassStepConfig extends BaseStep {
  stepType: "PASS";
  configuration: {
    passData?: Record<string, unknown>;
    passResult?: boolean;
  };
}

/**
 * Script Execution Step Configuration
 */
export interface ScriptStepConfig extends BaseStep {
  stepType: "SCRIPT";
  configuration: {
    language: "python" | "javascript" | "csharp" | "powershell";
    script: string;
    scriptPath?: string;
    parameters: Record<string, string>;
    timeout?: number;
    workingDirectory?: string;
    environmentVariables?: Record<string, string>;
  };
}

/**
 * JSONata Processor Step Configuration
 */
export interface JSONATStepConfig extends BaseStep {
  stepType: "JSONAT";
  configuration: {
    expression: string;
    inputPath: string;
    outputPath: string;
    resultPath: string;
    errorHandling: "continue" | "stop";
  };
}

/**
 * EAV (Entity-Attribute-Value) Step Configuration
 */
export interface EAVStepConfig extends BaseStep {
  stepType: "EAV";
  configuration: {
    entityType: string;
    attribute: string;
    operation: "create" | "read" | "update" | "delete";
    keyValue: string | number | boolean | string[];
    useEavRegistry: boolean;
    eavRegistryId?: string;
  };
}

/**
 * Rule Definition
 */
export interface Rule {
  name: string;
  description?: string;
  conditions: Condition[];
  outcome: "pass" | "fail" | "exception";
  priority?: number;
  testCases?: TestCase[];
}

export interface Condition {
  field: string;
  operator: "==" | "!=" | ">" | "<" | ">=" | "<=" | "in" | "contains" | "startsWith" | "endsWith" | "exists";
  value: string | number | boolean | string[];
  variable?: string; // JSONPath variable reference
}

export interface TestCase {
  name: string;
  input: Record<string, unknown>;
  expectedOutcome: "pass" | "fail" | "exception";
}

/**
 * API Registry Entry
 */
export interface APIRegistryEntry {
  apiId: string;
  name: string;
  description: string;
  endpoint: string;
  method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  path: string;
  headers: Record<string, string>;
  authentication: {
    type: "none" | "bearer" | "basic" | "oauth2" | "apikey";
    tokenSource?: string;
    tokenPath?: string;
  };
  parameters: Record<string, string>;
  responseSchema?: object;
  timeout: number;
  retries: number;
  tags: string[];
  isActive: boolean;
  version: string;
  category?: string;
  documentationUrl?: string;
}

/**
 * Step Configuration Validation Result
 */
export interface StepValidationResult {
  isValid: boolean;
  errors: ValidationIssue[];
  warnings: ValidationWarning[];
  stepType: StepType;
}

export interface ValidationIssue {
  field: string;
  message: string;
  severity: "error" | "warning";
  suggestions?: string[];
}

export interface ValidationWarning {
  message: string;
  suggestions?: string[];
}

/**
 * Step Execution Result
 */
export interface StepExecutionResult {
  stepId: string;
  stepType: StepType;
  status: "completed" | "failed" | "timeout" | "exception";
  output?: unknown;
  error?: string;
  executionTime?: number;
  metadata: {
    timestamp: string;
    inputSize?: number;
    outputSize?: number;
  };
}

/**
 * Flow Definition
 */
export interface FlowDefinition {
  name: string;
  description: string;
  version: string;
  startAt: string;
  states: Record<string, StepState>;
  metadata: {
    createdAt: string;
    createdBy: string;
    lastModified: string;
  };
}

export interface StepState {
  type: "Task" | "Choice" | "Parallel" | "Map" | "Wait";
  resource: string;
  comment?: string;
  end: boolean;
  next?: string;
  choices?: Choice[];
  default?: string;
  parameters?: Record<string, unknown>;
  resultSelector?: Record<string, string>;
  retry?: RetryPolicy;
  catch?: CatchPolicy[];
}

export interface Choice {
  variable: string;
  numericGreaterThan?: number;
  numericGreaterThanEquals?: number;
  numericLessThan?: number;
  numericLessThanEquals?: number;
  stringEquals?: string;
  booleanEquals?: boolean;
  isPresent?: boolean;
  isNull?: boolean;
  next: string;
}

export interface RetryPolicy {
  retryCount: number;
  retryDelay: number;
  maxBackoff?: number;
}

export interface CatchPolicy {
  error: string;
  next: string;
}

/**
 * Step Configuration Factory
 */
export const StepConfigFactory = {
  createAIStep: (config: Partial<AIStepConfig>): AIStepConfig => ({
    stepId: `ai_${Date.now()}`,
    stepType: "AI",
    title: config.title || "AI Step",
    description: config.description,
    resource: config.resource || "ai://default",
    configuration: {
      model: config.model || "gpt-4-turbo",
      prompt: config.prompt || "Analyze the input and provide output",
      systemPrompt: config.systemPrompt,
      temperature: config.temperature,
      maxTokens: config.maxTokens,
      promptVariables: config.promptVariables || {},
      llmService: config.llmService || "azureOpenAI",
      outputFormat: config.outputFormat || "json",
      outputSchema: config.outputSchema,
      contextWindow: config.contextWindow,
      functionCalls: config.functionCalls,
      tools: config.tools,
      decisionLogic: config.decisionLogic,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createRuleStep: (config: Partial<RuleStepConfig>): RuleStepConfig => ({
    stepId: `rule_${Date.now()}`,
    stepType: "RULE",
    title: config.title || "Rule Step",
    description: config.description,
    resource: config.resource || "rule://default",
    configuration: {
      engine: config.engine || "microsoftRulesEngine",
      rules: config.rules || [],
      defaultOutcome: config.defaultOutcome || "pass",
      testMode: config.testMode || false,
      timeout: config.timeout,
      priority: config.priority,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createSQLStep: (config: Partial<SQLStepConfig>): SQLStepConfig => ({
    stepId: `sql_${Date.now()}`,
    stepType: config.databaseType === "duckdb" ? "DUCKDB" : "SQL",
    title: config.title || "SQL Lookup",
    description: config.description,
    resource: config.resource || "duckdb://default",
    configuration: {
      database: config.database || "default.db",
      query: config.query || "SELECT * FROM table",
      parameters: config.parameters || {},
      columnsToReturn: config.columnsToReturn || [],
      databaseType: config.databaseType || "duckdb",
      connectionTimeout: config.connectionTimeout,
      useCaching: config.useCaching || false,
      cacheTTL: config.cacheTTL,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createAPIStep: (config: Partial<APIStepConfig>): APIStepConfig => ({
    stepId: `api_${Date.now()}`,
    stepType: "API",
    title: config.title || "API Call",
    description: config.description,
    resource: config.resource || "",
    configuration: {
      method: config.method || "POST",
      path: config.path || "",
      headers: config.headers || {},
      parameters: config.parameters || {},
      requestPayload: config.requestPayload,
      responseMapping: config.responseMapping || {},
      timeout: config.timeout,
      retries: config.retries,
      retryDelay: config.retryDelay,
      errorHandling: config.errorHandling || "continue",
      authenticated: config.authenticated || false,
      useRegisteredApi: config.useRegisteredApi || false,
      registeredApiId: config.registeredApiId,
      apiKey: config.apiKey,
      apiSecret: config.apiSecret,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createPassStep: (config: Partial<PassStepConfig>): PassStepConfig => ({
    stepId: `pass_${Date.now()}`,
    stepType: "PASS",
    title: config.title || "Pass Step",
    description: config.description,
    resource: config.resource || "pass://default",
    configuration: {
      passData: config.passData,
      passResult: config.passResult,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createScriptStep: (config: Partial<ScriptStepConfig>): ScriptStepConfig => ({
    stepId: `script_${Date.now()}`,
    stepType: "SCRIPT",
    title: config.title || "Script Step",
    description: config.description,
    resource: config.resource || "script://default",
    configuration: {
      language: config.language || "python",
      script: config.script || "",
      scriptPath: config.scriptPath,
      parameters: config.parameters || {},
      timeout: config.timeout,
      workingDirectory: config.workingDirectory,
      environmentVariables: config.environmentVariables,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createJSONATStep: (config: Partial<JSONATStepConfig>): JSONATStepConfig => ({
    stepId: `jsonat_${Date.now()}`,
    stepType: "JSONAT",
    title: config.title || "JSONAT Processor",
    description: config.description,
    resource: config.resource || "jsonat://default",
    configuration: {
      expression: config.expression || "",
      inputPath: config.inputPath || "",
      outputPath: config.outputPath || "",
      resultPath: config.resultPath || "",
      errorHandling: config.errorHandling || "continue",
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),

  createEAVStep: (config: Partial<EAVStepConfig>): EAVStepConfig => ({
    stepId: `eav_${Date.now()}`,
    stepType: "EAV",
    title: config.title || "EAV Step",
    description: config.description,
    resource: config.resource || "eav://default",
    configuration: {
      entityType: config.entityType || "",
      attribute: config.attribute || "",
      operation: config.operation || "read",
      keyValue: config.keyValue,
      useEavRegistry: config.useEavRegistry || false,
      eavRegistryId: config.eavRegistryId,
    },
    metadata: {
      createdAt: new Date().toISOString(),
      createdBy: "user",
      lastModified: new Date().toISOString(),
      version: 1,
    },
  }),
};

/**
 * Step Configuration Constants
 */
export const StepConfigConstants = {
  DEFAULT_TIMEOUT: 30,
  DEFAULT_RETRIES: 3,
  DEFAULT_RETRY_DELAY: 1000,
  SUPPORTED_LLMS: ["azureOpenAI", "anthropic", "googleVertex", "openAI"],
  SUPPORTED_DATABASES: ["duckdb", "sqlite", "postgresql", "mysql"],
  SUPPORTED_LANGUAGES: ["python", "javascript", "csharp", "powershell"],
};
