import React from "react";
import { StepConfig, StepType } from "../../../Types/stepConfig";
import { APIRegistry } from "../../../Types/apiRegistry";

export interface StepNodeProps {
  step: StepConfig;
  position: { x: number; y: number };
  selected: boolean;
  dragging: boolean;
  onDragStart: (e: React.MouseEvent) => void;
  onRemove: () => void;
  onConnect: () => void;
  apiRegistry: APIRegistry;
  availableAPIs: unknown;
}

export const StepNode: React.FC<StepNodeProps> = ({
  step,
  position,
  selected,
  dragging,
  onDragStart,
  onRemove,
  onConnect,
  apiRegistry,
  availableAPIs,
}) => {
  const getStepIcon = (type: StepType): string => {
    switch (type) {
      case "AI":
        return "🤖";
      case "RULE":
        return "⚖️";
      case "SQL":
        return "📊";
      case "API":
        return "🔌";
      case "PASS":
        return "⏭️";
      case "SCRIPT":
        return "💻";
      case "DUCKDB":
        return "🦆";
      case "HTTP":
        return "🌐";
      default:
        return "🔲";
    }
  };

  const getStepColor = (type: StepType): string => {
    switch (type) {
      case "AI":
        return "#8B5CF6"; // Purple
      case "RULE":
        return "#F59E0B"; // Amber
      case "SQL":
        return "#3B82F6"; // Blue
      case "API":
        return "#10B981"; // Emerald
      case "PASS":
        return "#9CA3AF"; // Gray
      case "SCRIPT":
        return "#EF4444"; // Red
      case "DUCKDB":
        return "#8B5CF6"; // Purple
      case "HTTP":
        return "#F59E0B"; // Amber
      default:
        return "#6B7280"; // Default Gray
    }
  };

  const getStepBorder = (type: StepType): string => {
    switch (type) {
      case "AI":
        return "border-purple-500";
      case "RULE":
        return "border-amber-500";
      case "SQL":
        return "border-blue-500";
      case "API":
        return "border-emerald-500";
      case "PASS":
        return "border-gray-500";
      case "SCRIPT":
        return "border-red-500";
      case "DUCKDB":
        return "border-purple-500";
      case "HTTP":
        return "border-amber-500";
      default:
        return "border-gray-500";
    }
  };

  return (
    <div
      className={`step-node ${selected ? "selected" : ""} ${dragging ? "dragging" : ""}`}
      style={{
        left: position.x,
        top: position.y,
        width: 200,
        zIndex: selected ? 10 : 1,
      }}
      draggable
      onDragStart={onDragStart}
    >
      <div
        className={`step-node-header ${getStepBorder(step.stepType)}`}
        style={{
          backgroundColor: getStepColor(step.stepType),
          color: "white",
        }}
      >
        <span className="step-icon">{getStepIcon(step.stepType)}</span>
        <span className="step-title">{step.title}</span>
        <button
          className="step-remove-btn"
          onClick={(e) => {
            e.stopPropagation();
            onRemove();
          }}
          style={{
            backgroundColor: "rgba(0,0,0,0.2)",
            color: "white",
            border: "none",
            cursor: "pointer",
          }}
        >
          ×
        </button>
      </div>

      <div className="step-node-body">
        <div className="step-node-description">{step.description || ""}</div>

        <div className="step-node-actions">
          <button
            className="step-connect-btn"
            onClick={(e) => {
              e.stopPropagation();
              onConnect();
            }}
          >
            Connect →
          </button>

          <div className="step-api-selector" style={{ marginTop: 8 }}>
            <span className="api-label">Registered APIs:</span>
            <div className="api-list">
              {apiRegistry &&
                Object.values(apiRegistry).map((api) => (
                  <div
                    key={api.apiId}
                    className="api-item"
                    style={{
                      backgroundColor: api.isActive ? "#E5E7EB" : "#F3F4F6",
                      padding: "4px 8px",
                      margin: "2px 0",
                      borderRadius: "4px",
                      fontSize: "11px",
                      cursor: "pointer",
                      border: api.isActive ? "1px solid #D1D5DB" : "1px solid #E5E7EB",
                    }}
                  >
                    {api.name}
                  </div>
                ))}
            </div>
          </div>
        </div>
      </div>

      <div className="step-node-footer">
        <span className="step-step-type">{step.stepType}</span>
        <span className="step-resource">{step.resource || ""}</span>
      </div>
    </div>
  );
};
