import React from "react";

export interface StepConnectionsProps {
  connection: Connection;
  onRemove: () => void;
}

export interface Connection {
  id: string;
  fromStepId: string;
  toStepId: string;
  type: "normal" | "conditional" | "error";
}

export const StepConnections: React.FC<StepConnectionsProps> = ({
  connection,
  onRemove,
}) => {
  const getConnectionColor = (type: Connection["type"]): string => {
    switch (type) {
      case "conditional":
        return "#F59E0B"; // Amber
      case "error":
        return "#EF4444"; // Red
      default:
        return "#3B82F6"; // Blue
    }
  };

  const getConnectionStyle = (type: Connection["type"]): React.CSSProperties => {
    const baseStyle: React.CSSProperties = {
      stroke: getConnectionColor(type),
      strokeWidth: 2,
      fill: "none",
      markerEnd: "url(#arrowhead)",
    };

    if (type === "conditional") {
      baseStyle.dashArray = "5,5";
    }

    return baseStyle;
  };

  return (
    <>
      <svg
        className="connection-svg"
        style={{
          position: "absolute",
          top: 0,
          left: 0,
          width: "100%",
          height: "100%",
          pointerEvents: "none",
        }}
      >
        <defs>
          <marker
            id="arrowhead"
            markerWidth="10"
            markerHeight="7"
            refX="9"
            refY="3.5"
            orient="auto"
          >
            <polygon points="0 0, 10 3.5, 0 7" fill={getConnectionColor(connection.type)} />
          </marker>
        </defs>
        <path
          d="M 0 100 L 300 100"
          style={getConnectionStyle(connection.type)}
        />
      </svg>
      <button
        className="connection-remove-btn"
        onClick={onRemove}
        style={{
          position: "absolute",
          top: -15,
          right: -15,
          width: 30,
          height: 30,
          borderRadius: "50%",
          backgroundColor: "rgba(239, 68, 68, 0.8)",
          color: "white",
          border: "none",
          cursor: "pointer",
          fontSize: "16px",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        ×
      </button>
    </>
  );
};
