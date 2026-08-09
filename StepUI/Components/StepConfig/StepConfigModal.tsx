import React, { useState } from "react";
import { StepConfig, StepType, StepConfigFactory, StepValidationResult } from "../../../Types/stepConfig";
import { APIRegistry, APIRegistryEntry } from "../../../Types/apiRegistry";
import { StepConfigForm } from "./StepConfigForm";

export interface StepConfigModalProps {
  stepId?: string;
  stepType: StepType;
  existingStep?: StepConfig;
  onSave: (stepConfig: StepConfig) => Promise<void>;
  onCancel: () => void;
  apiRegistry: APIRegistry;
  availableAPIs: APIRegistryEntry[];
}

export const StepConfigModal: React.FC<StepConfigModalProps> = ({
  stepId,
  stepType,
  existingStep,
  onSave,
  onCancel,
  apiRegistry,
  availableAPIs,
}) => {
  const [stepConfig, setStepConfig] = useState<StepConfig>(() => {
    if (existingStep) {
      return existingStep;
    }

    // Create default step based on type
    switch (stepType) {
      case "AI":
        return StepConfigFactory.createAIStep({
          title: "AI Decision",
          resource: "ai://decision",
          configuration: {
            model: "gpt-4-turbo",
            prompt: "Analyze the input and provide output",
            systemPrompt: "You are an AI assistant",
            temperature: 0.7,
            maxTokens: 200,
            promptVariables: {},
            llmService: "azureOpenAI",
            outputFormat: "json",
          },
        });
      case "RULE":
        return StepConfigFactory.createRuleStep({
          title: "Rule Check",
          resource: "rule://default",
          configuration: {
            engine: "microsoftRulesEngine",
            rules: [],
            defaultOutcome: "pass",
            testMode: false,
          },
        });
      case "SQL":
        return StepConfigFactory.createSQLStep({
          title: "SQL Lookup",
          resource: "duckdb://lookup",
          configuration: {
            database: "default.db",
            query: "SELECT * FROM table",
            parameters: {},
            columnsToReturn: [],
            databaseType: "duckdb",
            useCaching: false,
          },
        });
      case "API":
        return StepConfigFactory.createAPIStep({
          title: "API Call",
          resource: "",
          configuration: {
            method: "POST",
            path: "",
            headers: {},
            parameters: {},
            requestPayload: {},
            responseMapping: {},
            timeout: 30,
            retries: 3,
            errorHandling: "continue",
            authenticated: false,
            useRegisteredApi: false,
          },
        });
      case "PASS":
        return StepConfigFactory.createPassStep({
          title: "Pass Step",
          resource: "pass://default",
          configuration: {},
        });
      case "SCRIPT":
        return StepConfigFactory.createScriptStep({
          title: "Script Execution",
          resource: "script://default",
          configuration: {
            language: "python",
            script: "",
            parameters: {},
            timeout: 30,
          },
        });
      case "HTTP":
        return StepConfigFactory.createAPIStep({
          title: "HTTP Request",
          resource: "",
          configuration: {
            method: "GET",
            path: "",
            headers: {},
            parameters: {},
            timeout: 30,
            retries: 3,
            errorHandling: "continue",
            authenticated: false,
            useRegisteredApi: false,
          },
        });
      case "DUCKDB":
        return StepConfigFactory.createSQLStep({
          title: "DuckDB Query",
          resource: "duckdb://query",
          configuration: {
            database: "default.db",
            query: "SELECT * FROM table",
            parameters: {},
            columnsToReturn: [],
            databaseType: "duckdb",
            useCaching: false,
          },
        });
      case "JSONAT":
        return StepConfigFactory.createJSONATStep({
          title: "JSONAT Processor",
          resource: "jsonat://default",
          configuration: {
            expression: "",
            inputPath: "",
            outputPath: "",
            resultPath: "",
            errorHandling: "continue",
          },
        });
      case "EAV":
        return StepConfigFactory.createEAVStep({
          title: "EAV Step",
          resource: "eav://default",
          configuration: {
            entityType: "",
            attribute: "",
            operation: "read",
            keyValue: "",
            useEavRegistry: false,
          },
        });
      default:
        return {} as StepConfig;
    }
  });

  const [validationResult, setValidationResult] = useState<StepValidationResult | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [activeTab, setActiveTab] = useState<"config" | "preview" | "validation">("config");

  const handleValidation = () => {
    const result: StepValidationResult = {
      isValid: true,
      errors: [],
      warnings: [],
      stepType,
    };

    // Validate step type
    switch (stepType) {
      case "AI":
        const aiConfig = stepConfig.configuration as any;
        if (!aiConfig.model) {
          result.errors.push({
            field: "model",
            message: "LLM model is required",
            severity: "error",
          });
        }
        if (!aiConfig.prompt) {
          result.errors.push({
            field: "prompt",
            message: "AI prompt is required",
            severity: "error",
          });
        }
        break;

      case "RULE":
        const ruleConfig = stepConfig.configuration as any;
        if (!ruleConfig.engine) {
          result.errors.push({
            field: "engine",
            message: "Rule engine is required",
            severity: "error",
          });
        }
        break;

      case "SQL":
        const sqlConfig = stepConfig.configuration as any;
        if (!sqlConfig.query) {
          result.errors.push({
            field: "query",
            message: "SQL query is required",
            severity: "error",
          });
        }
        break;

      case "API":
        const apiConfig = stepConfig.configuration as any;
        if (!apiConfig.useRegisteredApi && !apiConfig.path) {
          result.errors.push({
            field: "path",
            message: "API path or registered API ID is required",
            severity: "error",
          });
        }
        break;

      case "SCRIPT":
        const scriptConfig = stepConfig.configuration as any;
        if (!scriptConfig.script) {
          result.errors.push({
            field: "script",
            message: "Script content is required",
            severity: "error",
          });
        }
        break;

      default:
        break;
    }

    setValidationResult(result);
    setActiveTab("validation");
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      await onSave(stepConfig);
      // Close modal logic here
    } catch (error) {
      console.error("Error saving step:", error);
    } finally {
      setIsSaving(false);
    }
  };

  const handleUseRegisteredAPI = (apiId: string) => {
    const api = availableAPIs.find((a) => a.apiId === apiId);
    if (api) {
      const apiConfig = stepConfig.configuration as any;
      apiConfig.useRegisteredApi = true;
      apiConfig.registeredApiId = apiId;
      apiConfig.method = api.method;
      apiConfig.path = api.path;
      apiConfig.headers = api.headers;
      setStepConfig({ ...stepConfig, configuration: apiConfig });
    }
  };

  const renderStepConfig = () => (
    <StepConfigForm
      stepType={stepType}
      stepConfig={stepConfig}
      stepId={stepId}
      onConfigChange={(config) => setStepConfig({ ...stepConfig, configuration: config })}
      apiRegistry={apiRegistry}
      availableAPIs={availableAPIs}
      onUseRegisteredAPI={handleUseRegisteredAPI}
    />
  );

  const renderPreview = () => (
    <div className="step-preview">
      <h3>Step Preview</h3>
      <div className="preview-content">
        <div className="preview-title">{stepConfig.title}</div>
        <div className="preview-type">{stepConfig.stepType}</div>
        <div className="preview-resource">{stepConfig.resource || "N/A"}</div>
        <div className="preview-description">{stepConfig.description || "No description"}</div>
      </div>
    </div>
  );

  const renderValidation = () => (
    <div className="step-validation">
      {validationResult ? (
        <>
          <h3>Validation Result</h3>
          <div className={`validation-status ${validationResult.isValid ? "valid" : "invalid"}`}>
            {validationResult.isValid ? "✓ Valid" : "✗ Invalid"}
          </div>
          {validationResult.errors.length > 0 && (
            <div className="validation-errors">
              <h4>Errors:</h4>
              {validationResult.errors.map((error, index) => (
                <div key={index} className="error-item">
                  <span className="error-field">{error.field}:</span>
                  <span className="error-message">{error.message}</span>
                </div>
              ))}
            </div>
          )}
          {validationResult.warnings.length > 0 && (
            <div className="validation-warnings">
              <h4>Warnings:</h4>
              {validationResult.warnings.map((warning, index) => (
                <div key={index} className="warning-item">
                  {warning.message}
                </div>
              ))}
            </div>
          )}
        </>
      ) : (
        <div className="no-validation">Click "Validate" to check step configuration</div>
      )}
    </div>
  );

  return (
    <div className="step-config-modal">
      <div className="modal-header">
        <h2>
          Configure Step
          <span className="step-type-badge">{stepType}</span>
        </h2>
        <button className="modal-close-btn" onClick={onCancel}>
          ×
        </button>
      </div>

      <div className="modal-tabs">
        <button
          className={`modal-tab ${activeTab === "config" ? "active" : ""}`}
          onClick={() => setActiveTab("config")}
        >
          Configuration
        </button>
        <button
          className={`modal-tab ${activeTab === "preview" ? "active" : ""}`}
          onClick={() => setActiveTab("preview")}
        >
          Preview
        </button>
        <button
          className={`modal-tab ${activeTab === "validation" ? "active" : ""}`}
          onClick={() => setActiveTab("validation")}
        >
          Validate
        </button>
      </div>

      <div className="modal-content">
        {activeTab === "config" && renderStepConfig()}
        {activeTab === "preview" && renderPreview()}
        {activeTab === "validation" && renderValidation()}
      </div>

      <div className="modal-footer">
        <button className="btn btn-secondary" onClick={onCancel}>
          Cancel
        </button>
        <button className="btn btn-secondary" onClick={handleValidation}>
          Validate
        </button>
        <button
          className="btn btn-primary"
          onClick={handleSave}
          disabled={isSaving}
        >
          {isSaving ? "Saving..." : "Save Step"}
        </button>
      </div>
    </div>
  );
};
