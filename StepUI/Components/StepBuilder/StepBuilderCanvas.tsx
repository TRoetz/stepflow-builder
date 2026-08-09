import React, { useState, useRef, useCallback } from "react";
import { StepType, StepConfig, StepConfigFactory, StepValidationResult } from "../../../Types/stepConfig";
import { APIRegistry, APIRegistryEntry } from "../../../Types/apiRegistry";
import { StepNode } from "./StepNode";
import { StepConnections } from "./StepConnections";

export interface StepBuilderCanvasProps {
  stepId?: string;
  title?: string;
  description?: string;
  existingSteps?: StepConfig[];
  connections?: Connection[];
  apiRegistry: APIRegistry;
  availableAPIs: APIRegistryEntry[];
  onStepAdded: (step: StepConfig) => void;
  onStepRemoved: (stepId: string) => void;
  onConnectionCreated: (connection: Connection) => void;
  onConnectionRemoved: (connectionId: string) => void;
  onFlowNameChanged: (name: string) => void;
  onFlowDescriptionChanged: (description: string) => void;
  onSave?: (flow: FlowDefinition) => Promise<void>;
  onCancel?: () => void;
}

export interface Connection {
  id: string;
  fromStepId: string;
  toStepId: string;
  type: "normal" | "conditional" | "error";
}

export interface FlowDefinition {
  id: string;
  title: string;
  description: string;
  stepId: string;
  steps: StepConfig[];
  connections: Connection[];
  metadata: {
    createdAt: string;
    lastModified: string;
  };
}

export const StepBuilderCanvas: React.FC<StepBuilderCanvasProps> = ({
  stepId,
  title: initialTitle = "New Flow",
  description: initialDescription = "",
  existingSteps = [],
  connections = [],
  apiRegistry,
  availableAPIs,
  onStepAdded,
  onStepRemoved,
  onConnectionCreated,
  onConnectionRemoved,
  onFlowNameChanged,
  onFlowDescriptionChanged,
  onSave,
  onCancel,
}) => {
  const [flowId, setFlowId] = useState<string>(() => `flow_${Date.now()}`);
  const [title, setTitle] = useState(initialTitle);
  const [description, setDescription] = useState(initialDescription);
  const [stepPositions, setStepPositions] = useState<{ [key: string]: { x: number; y: number } }>({});
  const [draggingStep, setDraggingStep] = useState<string | null>(null);
  const [selectedStep, setSelectedStep] = useState<string | null>(null);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const canvasRef = useRef<HTMLDivElement>(null);

  // Initialize steps from existing steps
  React.useEffect(() => {
    if (existingSteps.length > 0) {
      const positions: { [key: string]: { x: number; y: number } } = {};
      existingSteps.forEach((step, index) => {
        const x = index * 300 + 100;
        const y = 100 + (index % 3) * 150;
        positions[step.stepId] = { x, y };
        onStepAdded(step);
      });
      setStepPositions(positions);

      // Create connections
      existingSteps.forEach((step, index) => {
        if (existingSteps[index + 1]) {
          onConnectionCreated({
            id: `${step.stepId}_${existingSteps[index + 1].stepId}`,
            fromStepId: step.stepId,
            toStepId: existingSteps[index + 1].stepId,
            type: "normal",
          });
        }
      });
    }
  }, [existingSteps, onStepAdded, onConnectionCreated]);

  // Handle drag start
  const handleDragStart = useCallback(
    (e: React.MouseEvent, stepId: string) => {
      const step = existingSteps.find((s) => s.stepId === stepId);
      if (!step) return;

      const rect = canvasRef.current?.getBoundingClientRect();
      if (!rect) return;

      const offsetX = e.clientX - rect.left - stepPositions[stepId].x;
      const offsetY = e.clientY - rect.top - stepPositions[stepId].y;
      setDragOffset({ x: offsetX, y: offsetY });
      setDraggingStep(stepId);
    },
    [existingSteps, stepPositions]
  );

  // Handle drag move
  const handleDragMove = useCallback(
    (e: React.MouseEvent) => {
      if (!draggingStep || !canvasRef.current) return;

      const rect = canvasRef.current.getBoundingClientRect();
      const x = e.clientX - rect.left - dragOffset.x;
      const y = e.clientY - rect.top - dragOffset.y;

      setStepPositions((prev) => ({
        ...prev,
        [draggingStep]: { x, y },
      }));
    },
    [draggingStep, dragOffset]
  );

  // Handle drag end
  const handleDragEnd = useCallback(() => {
    setDraggingStep(null);
  }, []);

  // Add new step
  const addStep = useCallback(
    async (stepType: StepType, position: { x: number; y: number }) => {
      let newStep: StepConfig;
      let stepId = `step_${Date.now()}`;

      switch (stepType) {
        case "AI":
          newStep = StepConfigFactory.createAIStep({
            title: "AI Decision",
            resource: "ai://decision",
            configuration: {
              model: "gpt-4-turbo",
              prompt: "Analyze input and provide decision",
              promptVariables: {},
              llmService: "azureOpenAI",
            },
          });
          break;
        case "RULE":
          newStep = StepConfigFactory.createRuleStep({
            title: "Rule Check",
            resource: "rule://default",
            configuration: {
              engine: "microsoftRulesEngine",
              rules: [],
              defaultOutcome: "pass",
              testMode: false,
            },
          });
          break;
        case "SQL":
          newStep = StepConfigFactory.createSQLStep({
            title: "SQL Lookup",
            resource: "duckdb://lookup",
            configuration: {
              database: "default.db",
              query: "SELECT * FROM table",
              parameters: {},
              columnsToReturn: [],
              databaseType: "duckdb",
            },
          });
          break;
        case "API":
          newStep = StepConfigFactory.createAPIStep({
            title: "API Call",
            resource: "",
            configuration: {
              method: "POST",
              path: "",
              headers: {},
              parameters: {},
              useRegisteredApi: false,
            },
          });
          break;
        case "PASS":
          newStep = StepConfigFactory.createPassStep({
            title: "Pass Step",
            resource: "pass://default",
            configuration: {},
          });
          break;
        case "SCRIPT":
          newStep = StepConfigFactory.createScriptStep({
            title: "Script Execution",
            resource: "script://default",
            configuration: {
              language: "python",
              script: "",
              parameters: {},
            },
          });
          break;
        case "HTTP":
          newStep = StepConfigFactory.createAPIStep({
            title: "HTTP Request",
            resource: "",
            configuration: {
              method: "GET",
              path: "",
              headers: {},
              parameters: {},
              useRegisteredApi: false,
            },
          });
          break;
        case "DUCKDB":
          newStep = StepConfigFactory.createSQLStep({
            title: "DuckDB Query",
            resource: "duckdb://query",
            configuration: {
              database: "default.db",
              query: "SELECT * FROM table",
              parameters: {},
              columnsToReturn: [],
              databaseType: "duckdb",
            },
          });
          break;
        default:
          return;
      }

      // Update step ID with position
      newStep.stepId = stepId;
      onStepAdded(newStep);
      setStepPositions((prev) => ({ ...prev, [stepId]: position }));
      setSelectedStep(stepId);
    },
    [onStepAdded]
  );

  // Remove step
  const removeStep = useCallback(
    (stepId: string) => {
      onStepRemoved(stepId);
      setStepPositions((prev) => {
        const newPos = { ...prev };
        delete newPos[stepId];
        return newPos;
      });
    },
    [onStepRemoved]
  );

  // Create connection
  const createConnection = useCallback(
    (fromStepId: string, toStepId: string, type: Connection["type"]) => {
      const connectionId = `${fromStepId}_${toStepId}_${Date.now()}`;
      const newConnection: Connection = {
        id: connectionId,
        fromStepId,
        toStepId,
        type,
      };
      onConnectionCreated(newConnection);
    },
    [onConnectionCreated]
  );

  // Remove connection
  const removeConnection = useCallback(
    (connectionId: string) => {
      onConnectionRemoved(connectionId);
    },
    [onConnectionRemoved]
  );

  // Handle canvas click (clear selection)
  const handleCanvasClick = useCallback(() => {
    setSelectedStep(null);
  }, []);

  // Handle save
  const handleSave = useCallback(async () => {
    if (!onSave) return;

    const flow: FlowDefinition = {
      id: flowId,
      title,
      description,
      stepId: existingSteps[0]?.stepId || "start",
      steps: existingSteps,
      connections,
      metadata: {
        createdAt: new Date().toISOString(),
        lastModified: new Date().toISOString(),
      },
    };

    await onSave(flow);
  }, [flowId, title, description, existingSteps, connections, onSave]);

  // Render step templates
  const renderStepTemplates = () => (
    <div className="step-templates">
      <div className="step-template-item" onClick={() => addStep("AI", { x: 0, y: 0 })}>
        <div className="template-icon ai">AI</div>
        <span>AI/LLM</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("RULE", { x: 0, y: 0 })}>
        <div className="template-icon rule">Rule</div>
        <span>Rule Engine</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("SQL", { x: 0, y: 0 })}>
        <div className="template-icon sql">SQL</div>
        <span>SQL Lookup</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("API", { x: 0, y: 0 })}>
        <div className="template-icon api">API</div>
        <span>API Call</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("PASS", { x: 0, y: 0 })}>
        <div className="template-icon pass">Pass</div>
        <span>Pass Step</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("SCRIPT", { x: 0, y: 0 })}>
        <div className="template-icon script">Script</div>
        <span>Script</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("DUCKDB", { x: 0, y: 0 })}>
        <div className="template-icon duckdb">DuckDB</div>
        <span>DuckDB Query</span>
      </div>
      <div className="step-template-item" onClick={() => addStep("HTTP", { x: 0, y: 0 })}>
        <div className="template-icon http">HTTP</div>
        <span>HTTP Request</span>
      </div>
    </div>
  );

  return (
    <div className="step-builder-canvas">
      <div className="canvas-header">
        <h2>
          <input
            type="text"
            value={title}
            onChange={(e) => onFlowNameChanged(e.target.value)}
            placeholder="Flow Name"
            style={{ width: 200 }}
          />
        </h2>
        <div className="canvas-actions">
          <button onClick={handleCancel}>Cancel</button>
          <button onClick={handleSave}>Save Flow</button>
        </div>
      </div>

      <div className="canvas-content">
        <div className="canvas-legend">
          <h3>Available Step Types</h3>
          {renderStepTemplates()}
        </div>

        <div
          ref={canvasRef}
          className="canvas-area"
          onClick={handleCanvasClick}
          onMouseMove={handleDragMove}
          onMouseUp={handleDragEnd}
        >
          {existingSteps.map((step) => (
            <StepNode
              key={step.stepId}
              step={step}
              position={stepPositions[step.stepId] || { x: 0, y: 0 }}
              selected={selectedStep === step.stepId}
              dragging={draggingStep === step.stepId}
              onDragStart={(e) => handleDragStart(e, step.stepId)}
              onRemove={() => removeStep(step.stepId)}
              onConnect={() => createConnection(step.stepId, step.stepId, "normal")}
              apiRegistry={apiRegistry}
              availableAPIs={availableAPIs}
            />
          ))}

          {connections.map((connection) => (
            <StepConnections
              key={connection.id}
              connection={connection}
              onRemove={() => removeConnection(connection.id)}
            />
          ))}
        </div>
      </div>
    </div>
  );
};
