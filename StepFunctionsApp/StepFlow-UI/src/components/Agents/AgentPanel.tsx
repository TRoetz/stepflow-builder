import { useState, useCallback } from 'react';
import {
  Puzzle,
  Download,
  Trash2,
  Play,
  X,
  ChevronRight,
  Database,
  GitBranch,
  Cloud,
  FileJson,
  Search,
  Zap,
  CheckCircle2,
  AlertCircle,
  Loader2,
} from 'lucide-react';
import { useAgentStore, AgentDefinition } from '@stores/useAgentStore';

const iconMap: Record<string, React.ElementType> = {
  database: Database,
  'git-branch': GitBranch,
  cloud: Cloud,
  'file-json': FileJson,
  search: Search,
  zap: Zap,
};

const categoryLabels: Record<string, string> = {
  converter: 'Converter',
  generator: 'Generator',
  analyzer: 'Analyzer',
  optimizer: 'Optimizer',
};

const categoryColors: Record<string, string> = {
  converter: 'text-blue-400 bg-blue-500/10 border-blue-500/20',
  generator: 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20',
  analyzer: 'text-amber-400 bg-amber-500/10 border-amber-500/20',
  optimizer: 'text-purple-400 bg-purple-500/10 border-purple-500/20',
};

export function AgentPanel() {
  const {
    agents,
    isOpen,
    isRunning,
    runResults,
    toggleOpen,
    installAgent,
    uninstallAgent,
    runAgent,
  } = useAgentStore();

  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [agentInput, setAgentInput] = useState('');
  const [filter, setFilter] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<string>('all');

  const selectedAgent = agents.find((a) => a.id === selectedAgentId);

  const handleInstall = useCallback(
    (agentId: string) => {
      installAgent(agentId);
    },
    [installAgent]
  );

  const handleUninstall = useCallback(
    (agentId: string) => {
      uninstallAgent(agentId);
      if (selectedAgentId === agentId) {
        setSelectedAgentId(null);
        setAgentInput('');
      }
    },
    [uninstallAgent, selectedAgentId]
  );

  const handleRun = useCallback(async () => {
    if (!selectedAgentId) return;
    const result = await runAgent(selectedAgentId, agentInput);
    if (result.success) {
      // Could show result in a toast or inline
    }
  }, [selectedAgentId, agentInput, runAgent]);

  const filteredAgents = agents.filter((agent) => {
    const matchesFilter =
      !filter ||
      agent.name.toLowerCase().includes(filter.toLowerCase()) ||
      agent.description.toLowerCase().includes(filter.toLowerCase());
    const matchesCategory =
      categoryFilter === 'all' || agent.category === categoryFilter;
    return matchesFilter && matchesCategory;
  });

  if (!isOpen) {
    return null;
  }

  return (
    <div className="w-80 bg-gray-900 border-l border-gray-800 flex flex-col h-full">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <div className="flex items-center gap-2">
          <Puzzle className="w-4 h-4 text-indigo-400" />
          <span className="text-sm font-semibold text-gray-200">
            AI Agents
          </span>
        </div>
        <button
          onClick={toggleOpen}
          className="p-1 rounded-md hover:bg-gray-800 text-gray-400 hover:text-gray-200 transition-colors"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* Filters */}
      <div className="px-4 py-3 border-b border-gray-800 space-y-2">
        <div className="relative">
          <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-gray-500" />
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Search agents..."
            className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg pl-9 pr-3 py-2 text-sm text-gray-200 placeholder-gray-500 focus:outline-none focus:border-indigo-500/50 transition-colors"
          />
        </div>
        <div className="flex gap-1 flex-wrap">
          <button
            onClick={() => setCategoryFilter('all')}
            className={`px-2 py-1 rounded-md text-xs font-medium transition-colors ${
              categoryFilter === 'all'
                ? 'bg-indigo-600/20 text-indigo-400'
                : 'bg-gray-800/50 text-gray-400 hover:text-gray-200'
            }`}
          >
            All
          </button>
          {Object.entries(categoryLabels).map(([key, label]) => (
            <button
              key={key}
              onClick={() => setCategoryFilter(key)}
              className={`px-2 py-1 rounded-md text-xs font-medium transition-colors ${
                categoryFilter === key
                  ? 'bg-indigo-600/20 text-indigo-400'
                  : 'bg-gray-800/50 text-gray-400 hover:text-gray-200'
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {selectedAgent ? (
        <AgentDetailPanel
          agent={selectedAgent}
          result={runResults[selectedAgent.id]}
          input={agentInput}
          onInputChange={setAgentInput}
          onRun={handleRun}
          onBack={() => setSelectedAgentId(null)}
          onUninstall={handleUninstall}
          isRunning={isRunning}
        />
      ) : (
        <>
          {/* Agent List */}
          <div className="flex-1 overflow-y-auto">
            {filteredAgents.length === 0 ? (
              <div className="px-4 py-8 text-center text-sm text-gray-500">
                No agents found
              </div>
            ) : (
              filteredAgents.map((agent) => (
                <AgentListItem
                  key={agent.id}
                  agent={agent}
                  isSelected={selectedAgentId === agent.id}
                  onSelect={() => setSelectedAgentId(agent.id)}
                  onInstall={handleInstall}
                  onUninstall={handleUninstall}
                />
              ))
            )}
          </div>

          {/* Footer */}
          <div className="px-4 py-3 border-t border-gray-800 text-xs text-gray-500">
            {agents.filter((a) => a.installed).length} of {agents.length} agents installed
          </div>
        </>
      )}
    </div>
  );
}

function AgentListItem({
  agent,
  isSelected,
  onSelect,
  onInstall,
  onUninstall,
}: {
  agent: AgentDefinition;
  isSelected: boolean;
  onSelect: () => void;
  onInstall: (id: string) => void;
  onUninstall: (id: string) => void;
}) {
  const Icon = iconMap[agent.icon] || Puzzle;
  const categoryColor = categoryColors[agent.category] || categoryColors.converter;

  return (
    <button
      onClick={onSelect}
      className={`w-full flex items-start gap-3 px-4 py-3 text-left transition-colors border-b border-gray-800/50 ${
        isSelected ? 'bg-gray-800/50' : 'hover:bg-gray-800/30'
      }`}
    >
      <div className="w-8 h-8 rounded-lg bg-gray-800 flex items-center justify-center flex-shrink-0">
        <Icon className="w-4 h-4 text-gray-400" />
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm font-medium text-gray-200 truncate">
            {agent.name}
          </span>
          <span className={`text-[10px] px-1.5 py-0.5 rounded-full border ${categoryColor}`}>
            {categoryLabels[agent.category]}
          </span>
        </div>
        <p className="text-xs text-gray-500 mt-0.5 line-clamp-2">
          {agent.description}
        </p>
        <div className="flex items-center gap-2 mt-2">
          {agent.installed ? (
            <button
              onClick={(e) => {
                e.stopPropagation();
                onUninstall(agent.id);
              }}
              className="flex items-center gap-1 text-[10px] text-gray-500 hover:text-red-400 transition-colors"
            >
              <Trash2 className="w-3 h-3" />
              Uninstall
            </button>
          ) : (
            <button
              onClick={(e) => {
                e.stopPropagation();
                onInstall(agent.id);
              }}
              className="flex items-center gap-1 text-[10px] text-gray-500 hover:text-emerald-400 transition-colors"
            >
              <Download className="w-3 h-3" />
              Install
            </button>
          )}
          <ChevronRight className="w-3 h-3 text-gray-600" />
        </div>
      </div>
    </button>
  );
}

function AgentDetailPanel({
  agent,
  result,
  input,
  onInputChange,
  onRun,
  onBack,
  onUninstall: _onUninstall,
  isRunning,
}: {
  agent: AgentDefinition;
  result: { success: boolean; message: string; output?: string } | null | undefined;
  input: string;
  onInputChange: (v: string) => void;
  onRun: () => void;
  onBack: () => void;
  onUninstall: (id: string) => void;
  isRunning: boolean;
}) {
  const Icon = iconMap[agent.icon] || Puzzle;

  return (
    <div className="flex-1 overflow-y-auto">
      {/* Back button */}
      <div className="px-4 pt-3">
        <button
          onClick={onBack}
          className="flex items-center gap-1 text-xs text-gray-400 hover:text-gray-200 transition-colors"
        >
          <ChevronRight className="w-3 h-3 rotate-180" />
          Back to agents
        </button>
      </div>

      {/* Agent info */}
      <div className="px-4 py-4 border-b border-gray-800">
        <div className="flex items-start gap-3">
          <div className="w-10 h-10 rounded-xl bg-gray-800 flex items-center justify-center">
            <Icon className="w-5 h-5 text-indigo-400" />
          </div>
          <div className="flex-1">
            <h3 className="text-sm font-semibold text-gray-100">
              {agent.name}
            </h3>
            <p className="text-xs text-gray-400 mt-1 leading-relaxed">
              {agent.description}
            </p>
          </div>
        </div>

        {/* Metadata */}
        <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
          <div>
            <span className="text-gray-500">Version</span>
            <span className="ml-2 text-gray-300">{agent.version}</span>
          </div>
          <div>
            <span className="text-gray-500">Author</span>
            <span className="ml-2 text-gray-300">{agent.author}</span>
          </div>
          <div>
            <span className="text-gray-500">Source</span>
            <span className="ml-2 text-gray-300">
              {agent.sourceFormats.join(', ')}
            </span>
          </div>
          <div>
            <span className="text-gray-500">Target</span>
            <span className="ml-2 text-gray-300">{agent.targetFormat}</span>
          </div>
        </div>

        {/* Install/Uninstall */}
        <div className="mt-3">
          {agent.installed ? (
            <span className="inline-flex items-center gap-1 text-xs text-emerald-400">
              <CheckCircle2 className="w-3.5 h-3.5" />
              Installed
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 text-xs text-gray-500">
              <AlertCircle className="w-3.5 h-3.5" />
              Not installed
            </span>
          )}
        </div>
      </div>

      {/* Input area */}
      <div className="px-4 py-4 border-b border-gray-800 space-y-3">
        <label className="text-xs font-medium text-gray-400 uppercase tracking-wider">
          Input
        </label>
        <textarea
          value={input}
          onChange={(e) => onInputChange(e.target.value)}
          placeholder={`Paste your ${agent.sourceFormats[0]} content here...`}
          rows={6}
          className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg px-3 py-2 text-sm text-gray-200 placeholder-gray-500 font-mono focus:outline-none focus:border-indigo-500/50 transition-colors resize-none"
        />
        <button
          onClick={onRun}
          disabled={!agent.installed || isRunning}
          className="w-full flex items-center justify-center gap-2 px-3 py-2 rounded-lg bg-indigo-600/20 text-indigo-400 hover:bg-indigo-600/30 disabled:opacity-30 disabled:cursor-not-allowed transition-colors text-sm font-medium"
        >
          {isRunning ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Play className="w-4 h-4" />
          )}
          {isRunning ? 'Running...' : 'Run Agent'}
        </button>
      </div>

      {/* Results */}
      {result && (
        <div className="px-4 py-4">
          <label className="text-xs font-medium text-gray-400 uppercase tracking-wider">
            Result
          </label>
          <div
            className={`mt-2 rounded-lg border px-3 py-2 text-sm ${
              result.success
                ? 'bg-emerald-500/5 border-emerald-500/20 text-emerald-300'
                : 'bg-red-500/5 border-red-500/20 text-red-300'
            }`}
          >
            <div className="font-medium">{result.message}</div>
            {result.output && (
              <pre className="mt-2 text-xs whitespace-pre-wrap font-mono text-gray-400">
                {result.output}
              </pre>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
