import React, { useState } from "react";
import { StepConfig, StepType } from "../../../Types/stepConfig";
import { APIRegistry } from "../../../Types/apiRegistry";

export interface StepConfigFormProps {
  stepType: StepType;
  stepConfig: StepConfig;
  stepId: string;
  onConfigChange: (config: unknown) => void;
  apiRegistry: APIRegistry;
  availableAPIs: unknown;
  onUseRegisteredAPI: (apiId: string) => void;
}

export const StepConfigForm: React.FC<StepConfigFormProps> = ({
  stepType,
  stepConfig,
  stepId,
  onConfigChange,
  apiRegistry,
  availableAPIs,
  onUseRegisteredAPI,
}) => {
  const [activeTab, setActiveTab] = useState<"basic" | "advanced" | "rules">("basic");

  const handleBasicChange = (field: string, value: string) => {
    const config = { ...stepConfig.configuration };
    (config as any)[field] = value;
    onConfigChange(config);
  };

  const handleBasicNumberChange = (field: string, value: number) => {
    const config = { ...stepConfig.configuration };
    (config as any)[field] = value;
    onConfigChange(config);
  };

  const handleBooleanChange = (field: string, value: boolean) => {
    const config = { ...stepConfig.configuration };
    (config as any)[field] = value;
    onConfigChange(config);
  };

  const renderBasicTab = () => {
    const config = stepConfig.configuration as any;

    switch (stepType) {
      case "AI":
        return (
          <div className="config-section">
            <h4>Basic Configuration</h4>
            <div className="form-group">
              <label>Model:</label>
              <input
                type="text"
                value={config.model || "gpt-4-turbo"}
                onChange={(e) => handleBasicChange("model", e.target.value)}
                placeholder="gpt-4-turbo"
              />
            </div>
            <div className="form-group">
              <label>Prompt:</label>
              <textarea
                value={config.prompt || ""}
                onChange={(e) => handleBasicChange("prompt", e.target.value)}
                placeholder="Enter AI prompt"
                rows={4}
              />
            </div>
            <div className="form-group">
              <label>Temperature:</label>
              <input
                type="number"
                min="0"
                max="2"
                step="0.1"
                value={config.temperature || 0.7}
                onChange={(e) => handleBasicNumberChange("temperature", parseFloat(e.target.value))}
              />
            </div>
            <div className="form-group">
              <label>Max Tokens:</label>
              <input
                type="number"
                value={config.maxTokens || 200}
                onChange={(e) => handleBasicNumberChange("maxTokens", parseInt(e.target.value))}
              />
            </div>
            <div className="form-group">
              <label>Output Format:</label>
              <select
                value={config.outputFormat || "json"}
                onChange={(e) => handleBasicChange("outputFormat", e.target.value)}
              >
                <option value="json">JSON</option>
                <option value="text">Text</option>
                <option value="markdown">Markdown</option>
              </select>
            </div>
          </div>
        );

      case "RULE":
        return (
          <div className="config-section">
            <h4>Basic Configuration</h4>
            <div className="form-group">
              <label>Engine:</label>
              <select
                value={config.engine || "microsoftRulesEngine"}
                onChange={(e) => handleBasicChange("engine", e.target.value)}
              >
                <option value="microsoftRulesEngine">Microsoft Rules Engine</option>
                <option value="rulesEngine">Rules Engine</option>
                <option value="custom">Custom</option>
              </select>
            </div>
            <div className="form-group">
              <label>Default Outcome:</label>
              <select
                value={config.defaultOutcome || "pass"}
                onChange={(e) => handleBasicChange("defaultOutcome", e.target.value)}
              >
                <option value="pass">Pass</option>
                <option value="fail">Fail</option>
                <option value="exception">Exception</option>
              </select>
            </div>
            <div className="form-group">
              <label>Test Mode:</label>
              <input
                type="checkbox"
                checked={config.testMode || false}
                onChange={(e) => handleBooleanChange("testMode", e.target.checked)}
              />
            </div>
          </div>
        );

      case "SQL":
        return (
          <div className="config-section">
            <h4>Basic Configuration</h4>
            <div className="form-group">
              <label>Database:</label>
              <input
                type="text"
                value={config.database || "default.db"}
                onChange={(e) => handleBasicChange("database", e.target.value)}
                placeholder="default.db"
              />
            </div>
            <div className="form-group">
              <label>Database Type:</label>
              <select
                value={config.databaseType || "duckdb"}
                onChange={(e) => handleBasicChange("databaseType", e.target.value)}
              >
                <option value="duckdb">DuckDB</option>
                <option value="sqlite">SQLite</option>
                <option value="postgresql">PostgreSQL</option>
                <option value="mysql">MySQL</option>
              </select>
            </div>
          </div>
        );

      case "API":
        return (
          <div className="config-section">
            <h4>Basic Configuration</h4>
            <div className="form-group">
              <label>Use Registered API:</label>
              <input
                type="checkbox"
                checked={config.useRegisteredApi || false}
                onChange={(e) => handleBooleanChange("useRegisteredApi", e.target.checked)}
              />
            </div>
            {config.useRegisteredApi && (
              <div className="form-group">
                <label>Registered API ID:</label>
                <select
                  value={config.registeredApiId || ""}
                  onChange={(e) => onUseRegisteredAPI(e.target.value)}
                >
                  <option value="">Select API...</option>
                  {availableAPIs &&
                    Array.isArray(availableAPIs) &&
                    availableAPIs.map((api) => (
                      <option key={api.apiId} value={api.apiId}>
                        {api.name} ({api.apiId})
                      </option>
                    ))}
                </select>
              </div>
            )}
            {!config.useRegisteredApi && (
              <>
                <div className="form-group">
                  <label>Method:</label>
                  <select
                    value={config.method || "POST"}
                    onChange={(e) => handleBasicChange("method", e.target.value)}
                  >
                    <option value="GET">GET</option>
                    <option value="POST">POST</option>
                    <option value="PUT">PUT</option>
                    <option value="DELETE">DELETE</option>
                    <option value="PATCH">PATCH</option>
                  </select>
                </div>
                <div className="form-group">
                  <label>Path:</label>
                  <input
                    type="text"
                    value={config.path || ""}
                    onChange={(e) => handleBasicChange("path", e.target.value)}
                    placeholder="/api/endpoint"
                  />
                </div>
              </>
            )}
          </div>
        );

      case "PASS":
        return (
          <div className="config-section">
            <h4>Basic Configuration</h4>
            <div className="form-group">
              <label>Pass Data (JSON):</label>
              <textarea
                value={(config.passData as string) || ""}
                onChange={(e) => handleBasicChange("passData", e.target.value)}
                placeholder='{"key": "value"}'
                rows={4}
              />
            </div>
          </div>
        );

      case "SCRIPT":
        return (
          <div className="config-section">
            <h4>Basic Configuration</h4>
            <div className="form-group">
              <label>Language:</label>
              <select
                value={config.language || "python"}
                onChange={(e) => handleBasicChange("language", e.target.value)}
              >
                <option value="python">Python</option>
                <option value="javascript">JavaScript</option>
                <option value="csharp">C#</option>
                <option value="powershell">PowerShell</option>
              </select>
            </div>
            <div className="form-group">
              <label>Timeout (seconds):</label>
              <input
                type="number"
                value={config.timeout || 30}
                onChange={(e) => handleBasicNumberChange("timeout", parseInt(e.target.value))}
              />
            </div>
          </div>
        );

      default:
        return null;
    }
  };

  const renderAdvancedTab = () => {
    const config = stepConfig.configuration as any;

    switch (stepType) {
      case "AI":
        return (
          <div className="config-section">
            <h4>Advanced Configuration</h4>
            <div className="form-group">
              <label>System Prompt:</label>
              <textarea
                value={config.systemPrompt || ""}
                onChange={(e) => handleBasicChange("systemPrompt", e.target.value)}
                placeholder="You are an AI assistant..."
                rows={3}
              />
            </div>
            <div className="form-group">
              <label>Context Window:</label>
              <input
                type="number"
                value={config.contextWindow || 4096}
                onChange={(e) => handleBasicNumberChange("contextWindow", parseInt(e.target.value))}
              />
            </div>
            <div className="form-group">
              <label>Function Calls:</label>
              <input
                type="checkbox"
                checked={config.functionCalls || false}
                onChange={(e) => handleBooleanChange("functionCalls", e.target.checked)}
              />
            </div>
            <div className="form-group">
              <label>Output Schema (JSON):</label>
              <textarea
                value={JSON.stringify(config.outputSchema, null, 2) || "{}"}
                onChange={(e) => handleBasicChange("outputSchema", JSON.parse(e.target.value))}
                placeholder='{"decision": "string"}'
                rows={3}
              />
            </div>
          </div>
        );

      case "SQL":
        return (
          <div className="config-section">
            <h4>Advanced Configuration</h4>
            <div className="form-group">
              <label>SQL Query:</label>
              <textarea
                value={config.query || "SELECT * FROM table"}
                onChange={(e) => handleBasicChange("query", e.target.value)}
                placeholder="SELECT ... FROM ..."
                rows={6}
              />
            </div>
            <div className="form-group">
              <label>Parameters (JSON):</label>
              <textarea
                value={JSON.stringify(config.parameters, null, 2) || "{}"}
                onChange={(e) => handleBasicChange("parameters", JSON.parse(e.target.value))}
                placeholder='{"param1": "value1"}'
                rows={3}
              />
            </div>
            <div className="form-group">
              <label>Columns to Return (comma-separated):</label>
              <input
                type="text"
                value={config.columnsToReturn?.join(", ") || ""}
                onChange={(e) =>
                  handleBasicChange("columnsToReturn", e.target.value.split(",").map((c) => c.trim()))
                }
                placeholder="column1, column2, column3"
              />
            </div>
            <div className="form-group">
              <label>Use Caching:</label>
              <input
                type="checkbox"
                checked={config.useCaching || false}
                onChange={(e) => handleBooleanChange("useCaching", e.target.checked)}
              />
            </div>
            <div className="form-group">
              <label>Cache TTL (minutes):</label>
              <input
                type="number"
                value={config.cacheTTL || 60}
                onChange={(e) => handleBasicNumberChange("cacheTTL", parseInt(e.target.value))}
              />
            </div>
          </div>
        );

      case "API":
        return (
          <div className="config-section">
            <h4>Advanced Configuration</h4>
            <div className="form-group">
              <label>Headers (JSON):</label>
              <textarea
                value={JSON.stringify(config.headers, null, 2) || '{}'}
                onChange={(e) => handleBasicChange("headers", JSON.parse(e.target.value))}
                rows={4}
              />
            </div>
            <div className="form-group">
              <label>Request Payload (JSON):</label>
              <textarea
                value={JSON.stringify(config.requestPayload, null, 2) || "{}"}
                onChange={(e) => handleBasicChange("requestPayload", JSON.parse(e.target.value))}
                rows={4}
              />
            </div>
            <div className="form-group">
              <label>Response Mapping (JSON):</label>
              <textarea
                value={JSON.stringify(config.responseMapping, null, 2) || "{}"}
                onChange={(e) => handleBasicChange("responseMapping", JSON.parse(e.target.value))}
                rows={3}
              />
            </div>
            <div className="form-group">
              <label>Timeout (seconds):</label>
              <input
                type="number"
                value={config.timeout || 30}
                onChange={(e) => handleBasicNumberChange("timeout", parseInt(e.target.value))}
              />
            </div>
            <div className="form-group">
              <label>Retries:</label>
              <input
                type="number"
                value={config.retries || 3}
                onChange={(e) => handleBasicNumberChange("retries", parseInt(e.target.value))}
              />
            </div>
            <div className="form-group">
              <label>Retry Delay (ms):</label>
              <input
                type="number"
                value={config.retryDelay || 1000}
                onChange={(e) => handleBasicNumberChange("retryDelay", parseInt(e.target.value))}
              />
            </div>
            <div className="form-group">
              <label>Error Handling:</label>
              <select
                value={config.errorHandling || "continue"}
                onChange={(e) => handleBasicChange("errorHandling", e.target.value)}
              >
                <option value="continue">Continue</option>
                <option value="stop">Stop</option>
                <option value="log">Log</option>
              </select>
            </div>
            <div className="form-group">
              <label>Authenticated:</label>
              <input
                type="checkbox"
                checked={config.authenticated || false}
                onChange={(e) => handleBooleanChange("authenticated", e.target.checked)}
              />
            </div>
          </div>
        );

      default:
        return null;
    }
  };

  const renderRulesTab = () => {
    const config = stepConfig.configuration as any;

    if (stepType !== "RULE") {
      return <div className="tab-content-not-supported">Rule configuration is only available for RULE steps.</div>;
    }

    return (
      <div className="config-section">
        <h4>Rules Configuration</h4>
        <div className="rules-list">
          {config.rules && Array.isArray(config.rules) ? (
            config.rules.map((rule: any, index: number) => (
              <div key={index} className="rule-item">
                <div className="rule-header">
                  <input
                    type="text"
                    value={rule.name || `Rule ${index + 1}`}
                    onChange={(e) => {
                      const rules = [...config.rules];
                      rules[index].name = e.target.value;
                      onConfigChange({ rules });
                    }}
                    placeholder="Rule Name"
                  />
                </div>
                <div className="rule-description">
                  <textarea
                    value={rule.description || ""}
                    onChange={(e) => {
                      const rules = [...config.rules];
                      rules[index].description = e.target.value;
                      onConfigChange({ rules });
                    }}
                    placeholder="Rule description..."
                    rows={2}
                  />
                </div>
                <div className="rule-outcome">
                  <select
                    value={rule.outcome || "pass"}
                    onChange={(e) => {
                      const rules = [...config.rules];
                      rules[index].outcome = e.target.value;
                      onConfigChange({ rules });
                    }}
                  >
                    <option value="pass">Pass</option>
                    <option value="fail">Fail</option>
                    <option value="exception">Exception</option>
                  </select>
                </div>
              </div>
            ))
          ) : (
            <div className="no-rules">No rules defined. Add rules using conditions below.</div>
          )}
        </div>
        <div className="rules-add-btn">
          <button onClick={() => {
            const rules = (config.rules || []) as any[];
            rules.push({ name: "", description: "", outcome: "pass" });
            onConfigChange({ rules });
          }}>
            + Add Rule
          </button>
        </div>
      </div>
    );
  };

  const renderTabs = () => (
    <div className="config-tabs">
      <button
        className={`config-tab ${activeTab === "basic" ? "active" : ""}`}
        onClick={() => setActiveTab("basic")}
      >
        Basic
      </button>
      <button
        className={`config-tab ${activeTab === "advanced" ? "active" : ""}`}
        onClick={() => setActiveTab("advanced")}
      >
        Advanced
      </button>
      <button
        className={`config-tab ${activeTab === "rules" ? "active" : ""}`}
        onClick={() => setActiveTab("rules")}
      >
        Rules
      </button>
    </div>
  );

  return (
    <div className="step-config-form">
      <div className="form-header">
        <h3>{stepType} Step Configuration</h3>
        {renderTabs()}
      </div>

      <div className="form-content">
        {activeTab === "basic" && renderBasicTab()}
        {activeTab === "advanced" && renderAdvancedTab()}
        {activeTab === "rules" && renderRulesTab()}
      </div>

      <div className="form-footer">
        <div className="form-hint">
          Configure the step based on its type. Use the tabs to switch between basic, advanced, and rules configurations.
        </div>
      </div>
    </div>
  );
};
