import { create } from 'zustand';

// ── Types ──

export interface AgentDefinition {
  id: string;
  name: string;
  description: string;
  icon: string;
  category: 'converter' | 'generator' | 'analyzer' | 'optimizer';
  sourceFormats: string[];
  targetFormat: string;
  version: string;
  author: string;
  installed: boolean;
  status: 'ready' | 'running' | 'error';
}

export interface AgentRunResult {
  success: boolean;
  message: string;
  output?: string;
}

interface AgentState {
  agents: AgentDefinition[];
  isOpen: boolean;
  isRunning: boolean;
  activeAgentId: string | null;
  runResults: Record<string, AgentRunResult | null>;

  // Actions
  toggleOpen: () => void;
  installAgent: (agentId: string) => void;
  uninstallAgent: (agentId: string) => void;
  runAgent: (agentId: string, input: string) => Promise<AgentRunResult>;
  setActiveAgent: (agentId: string | null) => void;
}

// ── Available Agent Catalog ──

const agentCatalog: AgentDefinition[] = [
  {
    id: 'ssis-to-stepflow',
    name: 'SSIS to StepFlow',
    description: 'Convert SQL Server Integration Services (SSIS) packages to StepFlow workflows. Maps Data Flow tasks to Transform states, Control Flow to Choice/Parallel states, and Script tasks to Script states.',
    icon: 'database',
    category: 'converter',
    sourceFormats: ['dtsx', 'SSIS Package XML'],
    targetFormat: 'StepFlow JSON',
    version: '1.0.0',
    author: 'StepFlow Team',
    installed: false,
    status: 'ready',
  },
  {
    id: 'airflow-to-stepflow',
    name: 'Airflow to StepFlow',
    description: 'Convert Apache Airflow DAGs to StepFlow workflows. Maps PythonOperators to Script states, BashOperators to HTTP Request states, and branching logic to Choice states.',
    icon: 'git-branch',
    category: 'converter',
    sourceFormats: ['Python DAG', 'Airflow YAML'],
    targetFormat: 'StepFlow JSON',
    version: '1.0.0',
    author: 'StepFlow Team',
    installed: false,
    status: 'ready',
  },
  {
    id: 'terraform-to-stepflow',
    name: 'Terraform to StepFlow',
    description: 'Convert Terraform provisioner configurations to StepFlow workflows. Maps resource dependencies to state connections and provisioner scripts to Script states.',
    icon: 'cloud',
    category: 'converter',
    sourceFormats: ['HCL', 'Terraform JSON'],
    targetFormat: 'StepFlow JSON',
    version: '1.0.0',
    author: 'StepFlow Team',
    installed: false,
    status: 'ready',
  },
  {
    id: 'json-to-stepflow',
    name: 'JSON Schema to StepFlow',
    description: 'Generate StepFlow workflows from JSON Schema definitions. Creates data validation and transformation states based on schema structure.',
    icon: 'file-json',
    category: 'generator',
    sourceFormats: ['JSON Schema', 'OpenAPI Spec'],
    targetFormat: 'StepFlow JSON',
    version: '1.0.0',
    author: 'StepFlow Team',
    installed: false,
    status: 'ready',
  },
  {
    id: 'flow-analyzer',
    name: 'Flow Analyzer',
    description: 'Analyze existing StepFlow workflows for potential issues: dead ends, unconnected nodes, circular dependencies, and performance bottlenecks.',
    icon: 'search',
    category: 'analyzer',
    sourceFormats: ['StepFlow JSON'],
    targetFormat: 'Analysis Report',
    version: '1.0.0',
    author: 'StepFlow Team',
    installed: false,
    status: 'ready',
  },
  {
    id: 'flow-optimizer',
    name: 'Flow Optimizer',
    description: 'Optimize StepFlow workflows by suggesting parallel execution paths, reducing redundant states, and improving data flow efficiency.',
    icon: 'zap',
    category: 'optimizer',
    sourceFormats: ['StepFlow JSON'],
    targetFormat: 'Optimized StepFlow JSON',
    version: '1.0.0',
    author: 'StepFlow Team',
    installed: false,
    status: 'ready',
  },
];

export const useAgentStore = create<AgentState>((set, get) => ({
  agents: agentCatalog,
  isOpen: false,
  isRunning: false,
  activeAgentId: null,
  runResults: {},

  toggleOpen: () => set((s) => ({ isOpen: !s.isOpen })),

  installAgent: (agentId) => {
    set((state) => ({
      agents: state.agents.map((a) =>
        a.id === agentId ? { ...a, installed: true } : a
      ),
    }));
  },

  uninstallAgent: (agentId) => {
    set((state) => ({
      agents: state.agents.map((a) =>
        a.id === agentId ? { ...a, installed: false, status: 'ready' } : a
      ),
      runResults: Object.fromEntries(
        Object.entries(state.runResults).filter(([k]) => k !== agentId)
      ),
    }));
  },

  runAgent: async (agentId, _input) => {
    set({ isRunning: true, activeAgentId: agentId });

    // Simulate agent processing
    await new Promise((r) => setTimeout(r, 1500 + Math.random() * 2000));

    const agent = get().agents.find((a) => a.id === agentId);

    let result: AgentRunResult;

    if (agentId === 'ssis-to-stepflow') {
      result = {
        success: true,
        message: 'SSIS package converted successfully',
        output: `Converted SSIS package to StepFlow workflow:\n\n- Data Flow tasks → Transform states\n- Control Flow → Choice/Parallel states\n- Script Tasks → Script states\n- Execute SQL Tasks → SQL Query states\n- HTTP Requests → HTTP Request states\n\nThe converted workflow preserves the original data flow logic and error handling patterns.`,
      };
    } else if (agentId === 'airflow-to-stepflow') {
      result = {
        success: true,
        message: 'Airflow DAG converted successfully',
        output: `Converted Airflow DAG to StepFlow workflow:\n\n- PythonOperators → Script states\n- BashOperators → HTTP Request states\n- BranchPythonOperator → Choice states\n- TriggerDagRunOperator → Sub-Flow states\n- Sensors → Wait states\n\nDAG dependencies and scheduling metadata mapped to StepFlow connections.`,
      };
    } else if (agentId === 'flow-analyzer') {
      result = {
        success: true,
        message: 'Flow analysis complete',
        output: `Analysis Results:\n\n✅ Workflow structure is valid\n⚠️  Consider adding error handling branches\n⚠️  2 states could benefit from parallel execution\nℹ️  Estimated execution time: ~15s\n\nRecommendations:\n1. Add Fail states for error paths\n2. Parallelize independent branches\n3. Add Wait states between API calls`,
      };
    } else if (agentId === 'flow-optimizer') {
      result = {
        success: true,
        message: 'Flow optimization suggestions generated',
        output: `Optimization Suggestions:\n\n1. Combine sequential Transform states into single Script state\n2. Parallelize independent API calls using Parallel state\n3. Cache repeated SQL queries using Data Store states\n4. Reduce Choice nesting by flattening condition logic\n\nEstimated improvement: 40% faster execution.`,
      };
    } else {
      result = {
        success: true,
        message: `${agent?.name || 'Agent'} completed successfully`,
        output: `Processing complete. The ${agent?.name || 'agent'} has processed your input and generated the output.`,
      };
    }

    set((state) => ({
      isRunning: false,
      activeAgentId: null,
      runResults: { ...state.runResults, [agentId]: result },
    }));

    return result;
  },

  setActiveAgent: (agentId) => set({ activeAgentId: agentId }),
}));
