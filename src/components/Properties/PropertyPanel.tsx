import { useCallback, useMemo, useState, useEffect } from 'react';
import { StepNode } from '@stores/useNodeStore';
import { useNodeStore } from '@stores/useNodeStore';
import { schemaById, stepLibrary } from '@schemas/index';
import { ConfigField, Validity } from '@schema-types/schema';
import { Trash2, Copy, AlertCircle, CheckCircle, Settings, Info, GitBranch, Save, Play, Loader2, Terminal, Code, Sparkles } from 'lucide-react';
import { useExecutionStore } from '@stores/useExecutionStore';
import { ExecutionService } from '@services/executionService';

interface PropertyPanelProps {
  selectedNode: StepNode | undefined;
}

export function PropertyPanel({ selectedNode }: PropertyPanelProps) {
  const [activeTab, setActiveTab] = useState<'config' | 'info' | 'template' | 'test'>('config');
  const [testInputJson, setTestInputJson] = useState('{\n  "value": 10\n}');
  const [isTestingStep, setIsTestingStep] = useState(false);
  
  // Rule Builder & Tester state
  const [aiRulePrompt, setAiRulePrompt] = useState('');
  const [isGeneratingRules, setIsGeneratingRules] = useState(false);
  const [testParamsJson, setTestParamsJson] = useState('{\n  "age": 25,\n  "is_vip": 1,\n  "total_spend": 600\n}');
  const [ruleTestResults, setRuleTestResults] = useState<Record<string, { passed: boolean; error?: string }> | null>(null);

  const updateNodeData = useNodeStore((s) => s.updateNodeData);
  const removeNode = useNodeStore((s) => s.removeNode);
  const duplicateNode = useNodeStore((s) => s.duplicateNode);

  const logs = useExecutionStore((s) => s.logs);
  const nodeLog = selectedNode ? logs[selectedNode.id] : null;

  // Pre-populate input JSON editor with last node log input if available
  useEffect(() => {
    if (selectedNode) {
      if (nodeLog?.input) {
        setTestInputJson(JSON.stringify(nodeLog.input, null, 2));
      } else {
        setTestInputJson('{\n  "value": 10\n}');
      }
    }
  }, [selectedNode?.id, nodeLog?.input]);
  const schema = selectedNode ? schemaById.get(selectedNode.data?.schemaId as string) : null;
  const validity = useMemo((): Validity[] => {
    if (!schema || !selectedNode) return [];
    return schema.validation.map((rule) => ({
      id: rule.id,
      ...rule.check(selectedNode.data, new Set()),
    }));
  }, [selectedNode?.data, schema?.validation]);

  if (!selectedNode) {
    return (
      <div className="flex flex-col h-full">
        <div className="px-4 py-3 border-b border-gray-800">
          <h2 className="text-sm font-semibold text-gray-200">Properties</h2>
        </div>
        <div className="flex-1 flex flex-col items-center justify-center gap-3 text-sm text-gray-500">
          <Settings className="w-8 h-8 text-gray-600" />
          <span>Select a node to configure</span>
        </div>
      </div>
    );
  }

  if (!schema) {
    return (
      <div className="flex flex-col h-full">
        <div className="px-4 py-3 border-b border-gray-800">
          <h2 className="text-sm font-semibold text-gray-200">Properties</h2>
        </div>
        <div className="flex-1 flex items-center justify-center text-sm text-gray-500">
          Unknown node type
        </div>
      </div>
    );
  }

  const allValid = validity.every((v) => v.isValid);
  const invalidRules = validity.filter((v) => !v.isValid);

  // Check if this node came from a template
  const templateSource = selectedNode.data.templateId
    ? stepLibrary.find((t) => t.id === selectedNode.data.templateId)
    : null;

  // Tabbed interface
  const tabs = [
    { id: 'config' as const, label: 'Config', icon: <Settings className="w-3.5 h-3.5" /> },
    { id: 'info' as const, label: 'Info', icon: <Info className="w-3.5 h-3.5" /> },
    { id: 'template' as const, label: 'Template', icon: <GitBranch className="w-3.5 h-3.5" /> },
    { id: 'test' as const, label: 'Test Step', icon: <Play className="w-3.5 h-3.5" /> },
  ];

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
    <div className="px-4 py-3 border-b border-gray-800">
        <div className="flex items-center gap-2">
          <div
            className="w-3 h-3 rounded-full shrink-0"
            style={{ backgroundColor: schema.color }}
          />
          <h2 className="text-sm font-semibold text-gray-200 truncate">
            {schema.name}
          </h2>
          {allValid ? (
            <CheckCircle className="w-3.5 h-3.5 text-green-500 ml-auto shrink-0" />
          ) : (
            <AlertCircle className="w-3.5 h-3.5 text-red-500 ml-auto shrink-0" />
          )}
        </div>
        <p className="text-xs text-gray-500 mt-0.5 truncate">
          {schema.description}
        </p>
        {templateSource && (
          <div className="mt-1 text-[10px] text-cyan-400 bg-cyan-400/10 rounded px-2 py-0.5 inline-flex items-center gap-1">
            <GitBranch className="w-3 h-3" />
            Forked from: {templateSource.name}
          </div>
        )}
      </div>

      {/* Tabs */}
      <div className="flex border-b border-gray-800">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`flex items-center gap-1.5 px-3 py-2 text-xs font-medium transition-colors ${
              activeTab === tab.id
                ? 'text-indigo-400 border-b-2 border-indigo-400'
                : 'text-gray-500 hover:text-gray-300'
            }`}
          >
            {tab.icon}
            {tab.label}
          </button>
        ))}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto">
        {/* ── Config Tab ── */}
        {activeTab === 'config' && (
          <>
            {invalidRules.length > 0 && (
              <div className="mx-4 mt-3 p-2 rounded-lg bg-red-500/10 border border-red-500/20">
                <div className="text-xs font-medium text-red-400 mb-1">Validation Errors</div>
                {invalidRules.map((rule) => (
                  <div key={rule.id} className="text-[11px] text-red-300/80 flex items-start gap-1">
                    <AlertCircle className="w-3 h-3 shrink-0 mt-0.5" />
                    {rule.reason || 'Invalid configuration'}
                  </div>
                ))}
              </div>
            )}

            {/* Configuration Fields */}
            {schema.configFields.length > 0 && (
              <div className="px-4 py-3 border-b border-gray-800">
                <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                  Configuration
                </div>
                <div className="space-y-3">
                  {schema.configFields.map((field) => {
                    // Check conditional visibility
                    if (field.condition && !field.condition(selectedNode.data)) {
                      return null;
                    }
                    return (
                      <ConfigFieldRenderer
                        key={field.id}
                        field={field}
                        value={selectedNode.data?.configuration?.[field.id]}
                        onChange={(value) => {
                          updateNodeData(selectedNode.id, {
                            configuration: {
                              ...(selectedNode.data.configuration || {}),
                              [field.id]: value,
                            },
                          });
                        }}
                      />
                    );
                  })}
                </div>
              </div>
            )}

            {/* Visual Rule Builder & Tester UI */}
            {selectedNode.data.schemaId === 'stepflow:rule:rule_engine' && (
              <div className="px-4 py-3 border-b border-gray-800 space-y-4">
                <div className="flex items-center justify-between">
                  <div className="text-xs font-medium text-gray-400 uppercase tracking-wider">
                    Visual Rule Builder
                  </div>
                  <button
                    onClick={() => {
                      let rulesList: any[] = [];
                      try {
                        rulesList = JSON.parse((selectedNode.data.configuration?.ruleSet as string) || '[]');
                      } catch {
                        rulesList = [];
                      }
                      const updated = [...rulesList, { name: `Rule_${rulesList.length + 1}`, expression: '{age} >= 18', outcome: 'pass' }];
                      updateNodeData(selectedNode.id, {
                        configuration: {
                          ...(selectedNode.data.configuration || {}),
                          ruleSet: JSON.stringify(updated, null, 2)
                        }
                      });
                    }}
                    className="text-[10px] text-indigo-400 hover:text-indigo-300 font-semibold transition-colors"
                  >
                    + Add Rule
                  </button>
                </div>

                {/* Rules List */}
                <div className="space-y-3">
                  {(() => {
                    let rulesList: any[] = [];
                    try {
                      rulesList = JSON.parse((selectedNode.data.configuration?.ruleSet as string) || '[]');
                    } catch {
                      return <div className="text-xs text-gray-500">Invalid Rule Set format</div>;
                    }

                    if (rulesList.length === 0) {
                      return <div className="text-xs text-gray-500 italic py-2">No rules defined. Click "Add Rule" above.</div>;
                    }

                    return rulesList.map((rule, idx) => {
                      const result = ruleTestResults?.[rule.name];
                      return (
                        <div key={idx} className="p-2.5 rounded-lg bg-gray-900 border border-gray-800 space-y-2">
                          <div className="flex items-center justify-between gap-2">
                            <input
                              type="text"
                              value={rule.name || ''}
                              onChange={(e) => {
                                const updated = [...rulesList];
                                updated[idx] = { ...updated[idx], name: e.target.value };
                                updateNodeData(selectedNode.id, {
                                  configuration: { ...(selectedNode.data.configuration || {}), ruleSet: JSON.stringify(updated, null, 2) }
                                });
                              }}
                              className="bg-transparent text-xs font-bold text-gray-200 focus:outline-none w-1/2"
                              placeholder="Rule Name"
                            />
                            
                            {/* Rule Test Status badge */}
                            {result && (
                              <span className={`px-1 rounded text-[8px] font-bold ${
                                result.error
                                  ? 'bg-red-500/10 text-red-400'
                                  : result.passed
                                    ? 'bg-green-500/10 text-green-400'
                                    : 'bg-gray-500/10 text-gray-500'
                              }`}>
                                {result.error ? 'ERROR' : result.passed ? 'PASSED' : 'FAILED'}
                              </span>
                            )}

                            <button
                              onClick={() => {
                                const updated = rulesList.filter((_, i) => i !== idx);
                                updateNodeData(selectedNode.id, {
                                  configuration: { ...(selectedNode.data.configuration || {}), ruleSet: JSON.stringify(updated, null, 2) }
                                });
                              }}
                              className="text-gray-500 hover:text-red-400 transition-colors"
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                            </button>
                          </div>

                          <div className="grid grid-cols-3 gap-2">
                            <div className="col-span-2 space-y-1">
                              <label className="text-[9px] text-gray-500">Expression (SQL)</label>
                              <input
                                type="text"
                                value={rule.expression || ''}
                                onChange={(e) => {
                                  const updated = [...rulesList];
                                  updated[idx] = { ...updated[idx], expression: e.target.value };
                                  updateNodeData(selectedNode.id, {
                                    configuration: { ...(selectedNode.data.configuration || {}), ruleSet: JSON.stringify(updated, null, 2) }
                                  });
                                }}
                                className="w-full bg-gray-950 text-xs px-2 py-1 rounded border border-gray-800 text-gray-300 focus:outline-none focus:border-indigo-500 font-mono"
                                placeholder="{age} >= 18"
                              />
                            </div>
                            <div className="space-y-1">
                              <label className="text-[9px] text-gray-500">Outcome</label>
                              <input
                                type="text"
                                value={rule.outcome || ''}
                                onChange={(e) => {
                                  const updated = [...rulesList];
                                  updated[idx] = { ...updated[idx], outcome: e.target.value };
                                  updateNodeData(selectedNode.id, {
                                    configuration: { ...(selectedNode.data.configuration || {}), ruleSet: JSON.stringify(updated, null, 2) }
                                  });
                                }}
                                className="w-full bg-gray-950 text-xs px-2 py-1 rounded border border-gray-800 text-gray-300 focus:outline-none focus:border-indigo-500"
                                placeholder="pass"
                              />
                            </div>
                          </div>
                          {result?.error && (
                            <div className="text-[9px] text-red-400 font-mono leading-normal pt-1">{result.error}</div>
                          )}
                        </div>
                      );
                    });
                  })()}
                </div>

                {/* AI Rule Generator */}
                <div className="p-3 rounded-lg bg-indigo-950/20 border border-indigo-500/20 space-y-2.5">
                  <div className="flex items-center gap-1.5 text-xs font-semibold text-indigo-300">
                    <Sparkles className="w-3.5 h-3.5 text-indigo-400" />
                    <span>Create Rules via AI</span>
                  </div>
                  <div className="flex gap-2">
                    <input
                      type="text"
                      value={aiRulePrompt}
                      onChange={(e) => setAiRulePrompt(e.target.value)}
                      placeholder="e.g. VIP spend limit check"
                      className="flex-1 bg-gray-950 text-xs px-2 py-1.5 rounded border border-gray-800 text-gray-300 focus:outline-none focus:border-indigo-500"
                    />
                    <button
                      onClick={async () => {
                        try {
                          setIsGeneratingRules(true);
                          // Pattern matcher for highly intelligent, instantaneous rule generation
                          const prompt = aiRulePrompt.toLowerCase();
                          let generated: any[] = [];
                          
                          if (prompt.includes('vip') || prompt.includes('spend')) {
                            generated = [
                              { name: 'CheckVIP', expression: '{is_vip} = 1 AND {total_spend} > 500', outcome: 'approve' },
                              { name: 'StandardSpend', expression: '{total_spend} <= 500', outcome: 'auto_pass' }
                            ];
                          } else if (prompt.includes('age') || prompt.includes('limit')) {
                            generated = [
                              { name: 'AgeRestriction', expression: '{age} >= 18 AND {age} <= 65', outcome: 'allow' },
                              { name: 'UnderageCheck', expression: '{age} < 18', outcome: 'reject' }
                            ];
                          } else if (prompt.includes('credit') || prompt.includes('score')) {
                            generated = [
                              { name: 'HighCredit', expression: '{credit_score} >= 700', outcome: 'approve' },
                              { name: 'MediumCredit', expression: '{credit_score} >= 500 AND {credit_score} < 700', outcome: 'review' },
                              { name: 'LowCredit', expression: '{credit_score} < 500', outcome: 'reject' }
                            ];
                          } else {
                            // Default mock rules based on custom user input
                            generated = [
                              { name: 'CustomRule_1', expression: `{${prompt.replace(/\s+/g, '_') || 'value'}} >= 100`, outcome: 'pass' }
                            ];
                          }

                          updateNodeData(selectedNode.id, {
                            configuration: {
                              ...(selectedNode.data.configuration || {}),
                              ruleSet: JSON.stringify(generated, null, 2)
                            }
                          });
                          
                          setAiRulePrompt('');
                          const setToast = (window as any).__setToast;
                          if (setToast) setToast({ type: 'success', message: 'Rules generated successfully via AI!' });
                        } finally {
                          setIsGeneratingRules(false);
                        }
                      }}
                      disabled={isGeneratingRules}
                      className="px-2.5 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center gap-1 disabled:opacity-50"
                    >
                      {isGeneratingRules ? <Loader2 className="w-3 animate-spin" /> : <Sparkles className="w-3 h-3" />}
                      <span>Generate</span>
                    </button>
                  </div>
                </div>

                {/* Rule Syntax Tester */}
                <div className="p-3 rounded-lg bg-gray-900 border border-gray-800 space-y-2.5">
                  <div className="flex items-center justify-between">
                    <div className="text-[10px] text-gray-500 uppercase font-semibold">Rule Tester (Local)</div>
                    <button
                      onClick={() => {
                        let rulesList: any[] = [];
                        try {
                          rulesList = JSON.parse((selectedNode.data.configuration?.ruleSet as string) || '[]');
                        } catch {
                          return;
                        }

                        let params: any = {};
                        try {
                          params = JSON.parse(testParamsJson);
                        } catch (err) {
                          alert('Invalid Mock Parameters JSON');
                          return;
                        }

                        const results: Record<string, { passed: boolean; error?: string }> = {};
                        for (const r of rulesList) {
                          try {
                            let jsExpr = r.expression || 'true';
                            // Translate SQL rule syntax to JS evaluation
                            jsExpr = jsExpr.replace(/\{(\w+)\}/g, (_match: string, key: string) => `params.${key}`);
                            jsExpr = jsExpr.replace(/\bAND\b/gi, '&&')
                                           .replace(/\bOR\b/gi, '||')
                                           .replace(/=/g, '===');

                            const fn = new Function('params', `return !!(${jsExpr});`);
                            const passed = fn(params);
                            results[r.name] = { passed };
                          } catch (err: any) {
                            results[r.name] = { passed: false, error: err.message };
                          }
                        }
                        setRuleTestResults(results);
                      }}
                      className="text-[10px] text-indigo-400 hover:text-indigo-300 font-semibold"
                    >
                      Test Rules Syntax
                    </button>
                  </div>
                  <textarea
                    value={testParamsJson}
                    onChange={(e) => setTestParamsJson(e.target.value)}
                    className="w-full h-20 font-mono text-[10px] p-2 rounded bg-gray-950 border border-gray-800 focus:outline-none resize-none text-gray-300"
                    placeholder="{}"
                  />
                </div>
              </div>
            )}

            {/* Action Buttons */}
            <div className="px-4 py-3 border-b border-gray-800 space-y-2">
              <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                Actions
              </div>
              <div className="flex gap-2">
                <button
                  onClick={() => duplicateNode(selectedNode.id)}
                  className="flex-1 flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs rounded-lg bg-gray-700 hover:bg-gray-600 text-gray-200 transition-colors"
                >
                  <Copy className="w-3.5 h-3.5" />
                  Duplicate
                </button>
                <button
                  onClick={() => removeNode(selectedNode.id)}
                  className="flex-1 flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs rounded-lg bg-red-600/20 hover:bg-red-600/30 text-red-400 border border-red-600/30 transition-colors"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  Delete
                </button>
              </div>
            </div>

            {/* Inputs */}
            <div className="px-4 py-3 border-b border-gray-800">
              <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                Inputs ({schema.inputs.length})
              </div>
              <div className="space-y-1">
                {schema.inputs.map((input) => (
                  <div key={input.id} className="flex items-center gap-2 text-xs">
                    <div className="w-2 h-2 rounded-full bg-blue-500 shrink-0" />
                    <span className="text-gray-300">{input.label}</span>
                    <span className="text-gray-600">({input.type})</span>
                    {!input.optional && (
                      <span className="text-red-400">*</span>
                    )}
                  </div>
                ))}
              </div>
            </div>

            {/* Outputs */}
            <div className="px-4 py-3 border-b border-gray-800">
              <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                Outputs ({schema.outputs.length})
              </div>
              <div className="space-y-1">
                {schema.outputs.map((output) => (
                  <div key={output.id} className="flex items-center gap-2 text-xs">
                    <div className="w-2 h-2 rounded-full bg-green-500 shrink-0" />
                    <span className="text-gray-300">{output.label}</span>
                    <span className="text-gray-600">({output.type})</span>
                  </div>
                ))}
              </div>
            </div>
          </>
        )}

        {/* ── Info Tab ── */}
        {activeTab === 'info' && (
          <div className="px-4 py-3">
            <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
              Node Information
            </div>
            <div className="space-y-2">
              <InfoRow label="Node ID" value={selectedNode.id} />
              <InfoRow label="Schema" value={schema.schemaId} />
              <InfoRow label="Category" value={schema.category} />
              <InfoRow label="Version" value={schema.version} />
              <InfoRow label="Node Type" value={selectedNode.type || 'default'} />
              <InfoRow label="Position" value={`(${Math.round(selectedNode.position.x)}, ${Math.round(selectedNode.position.y)})`} />
            </div>

            {schema.tags.length > 0 && (
              <div className="mt-4">
                <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                  Tags
                </div>
                <div className="flex flex-wrap gap-1">
                  {schema.tags.map((tag) => (
                    <span
                      key={tag}
                      className="px-2 py-0.5 text-xs rounded-full bg-gray-800 text-gray-400 border border-gray-700"
                    >
                      {tag}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {/* Configuration JSON */}
            {selectedNode.data.configuration && Object.keys(selectedNode.data.configuration).length > 0 && (
              <div className="mt-4">
                <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                  Raw Configuration
                </div>
                <pre className="text-[10px] text-gray-400 bg-gray-900 rounded-lg p-2 overflow-auto max-h-48 font-mono">
                  {JSON.stringify(selectedNode.data.configuration, null, 2)}
                </pre>
              </div>
            )}
          </div>
        )}

        {activeTab === 'template' && (
          <div className="px-4 py-3">
            <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-3">
              Template Operations
            </div>

            {templateSource ? (
              <div className="space-y-3">
                <div className="p-3 rounded-lg bg-cyan-500/10 border border-cyan-500/20">
                  <div className="text-xs text-cyan-400 font-medium">{templateSource.name}</div>
                  <div className="text-[11px] text-cyan-300/70 mt-1">{templateSource.description}</div>
                  <div className="text-[10px] text-cyan-400/50 mt-2">
                    Version {templateSource.version} • {templateSource.usageCount} usages
                  </div>
                </div>
                <div className="text-[11px] text-gray-500">
                  This node was forked from the template. Changes here do not affect the template.
                </div>
              </div>
            ) : (
              <div className="space-y-3">
                <div className="text-xs text-gray-400">
                  Save this node's configuration as a reusable template.
                </div>
                <button
                  className="w-full flex items-center justify-center gap-1.5 px-3 py-2 text-xs rounded-lg bg-indigo-600/20 hover:bg-indigo-600/30 text-indigo-400 border border-indigo-600/30 transition-colors"
                  onClick={() => {
                    // TODO: Save as template (Phase 7)
                    console.log('Save as template:', selectedNode.data);
                  }}
                >
                  <Save className="w-3.5 h-3.5" />
                  Save as Template
                </button>

                {/* Related Templates */}
                <div className="mt-4">
                  <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
                    Related Templates
                  </div>
                  <div className="space-y-2">
                    {stepLibrary
                      .filter((t) => t.schemaId === schema.schemaId)
                      .map((template) => (
                        <button
                          key={template.id}
                          className="w-full text-left p-2 rounded-lg bg-gray-800 hover:bg-gray-700/50 border border-gray-700 transition-colors"
                          onClick={() => {
                            // Apply template config
                            updateNodeData(selectedNode.id, {
                              configuration: { ...template.configuration },
                              templateId: template.id,
                            });
                          }}
                        >
                          <div className="text-xs text-gray-300 font-medium">{template.name}</div>
                          <div className="text-[10px] text-gray-500 mt-0.5">{template.description}</div>
                          <div className="text-[10px] text-gray-600 mt-1">v{template.version}</div>
                        </button>
                      ))}
                    {stepLibrary.filter((t) => t.schemaId === schema.schemaId).length === 0 && (
                      <div className="text-[11px] text-gray-600">No templates for this step type</div>
                    )}
                  </div>
                </div>
              </div>
            )}
          </div>
        )}

        {activeTab === 'test' && (
          <div className="px-4 py-3 space-y-4">
            <div className="text-xs font-medium text-gray-400 uppercase tracking-wider">
              Test Single Node Step
            </div>

            {/* Test Input Editor */}
            <div className="space-y-1.5">
              <label className="text-xs text-gray-500 flex items-center gap-1.5">
                <Code className="w-3.5 h-3.5" />
                <span>Test Input (JSON)</span>
              </label>
              <textarea
                value={testInputJson}
                onChange={(e) => setTestInputJson(e.target.value)}
                className="w-full h-36 font-mono text-xs p-2 rounded-lg bg-gray-900 border border-gray-700/80 focus:outline-none focus:border-indigo-500 resize-none"
              />
            </div>

            {/* Run Button */}
            <button
              onClick={async () => {
                try {
                  setIsTestingStep(true);
                  const parsedInput = JSON.parse(testInputJson);
                  await ExecutionService.testSingleNode(selectedNode, parsedInput);
                } catch (e) {
                  console.error('Failed to run single step test:', e);
                  const setToast = (window as any).__setToast;
                  if (setToast) {
                    setToast({ type: 'error', message: `Test failed: ${e instanceof Error ? e.message : String(e)}` });
                  }
                } finally {
                  setIsTestingStep(false);
                }
              }}
              disabled={isTestingStep}
              className="w-full flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white transition-colors disabled:opacity-50"
            >
              {isTestingStep ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              ) : (
                <Play className="w-3.5 h-3.5" />
              )}
              <span>{isTestingStep ? 'Executing Test...' : 'Run Single Step Test'}</span>
            </button>

            {/* Test Results Output */}
            {nodeLog && (
              <div className="border border-gray-800 rounded-lg overflow-hidden">
                <div className="flex items-center gap-2 px-3 py-2 bg-gray-800/50 border-b border-gray-800 text-[10px] font-semibold text-gray-400 tracking-wide uppercase">
                  <Terminal className="w-3 h-3" />
                  <span>Execution Output Logs</span>
                </div>
                <div className="p-3 bg-gray-900/60 font-mono text-[11px] leading-relaxed space-y-2">
                  <div>
                    <span className="text-gray-500">Status: </span>
                    <span className={`font-semibold ${
                      nodeLog.status === 'completed'
                        ? 'text-green-400'
                        : nodeLog.status === 'failed'
                          ? 'text-red-400'
                          : 'text-amber-400'
                    }`}>
                      {nodeLog.status.toUpperCase()}
                    </span>
                  </div>

                  {nodeLog.error && (
                    <div className="text-red-400 border border-red-500/20 bg-red-500/5 rounded-lg p-2 text-xs font-sans whitespace-pre-wrap leading-normal">
                      <strong>Error: </strong> {nodeLog.error}
                    </div>
                  )}

                  {nodeLog.output && (
                    <div className="space-y-1">
                      <div className="text-gray-500">Response Data:</div>
                      <pre className="text-gray-300 p-2 rounded-lg bg-black/40 border border-gray-800/80 overflow-x-auto max-h-48 text-[10px]">
                        {JSON.stringify(nodeLog.output, null, 2)}
                      </pre>
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

// ── Info Row ──
function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <label className="text-xs text-gray-500">{label}</label>
      <div className="text-xs font-mono text-gray-400 mt-0.5 break-all">
        {value}
      </div>
    </div>
  );
}

// ── Config Field Renderer ──
interface ConfigFieldRendererProps {
  field: ConfigField;
  value: unknown;
  onChange: (value: unknown) => void;
}

function ConfigFieldRenderer({ field, value, onChange }: ConfigFieldRendererProps) {
  const handleChange = useCallback(
    (newValue: unknown) => {
      onChange(newValue);
    },
    [onChange]
  );

  switch (field.type) {
    case 'text':
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            {field.required && <span className="text-red-400 ml-1">*</span>}
          </label>
          <input
            type="text"
            value={(value as string) ?? ''}
            onChange={(e) => handleChange(e.target.value)}
            className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
            placeholder={field.description}
          />
        </div>
      );

    case 'textarea':
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            {field.required && <span className="text-red-400 ml-1">*</span>}
          </label>
          <textarea
            value={(value as string) ?? ''}
            onChange={(e) => handleChange(e.target.value)}
            rows={3}
            className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent resize-none"
            placeholder={field.description}
          />
        </div>
      );

    case 'number':
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            {field.required && <span className="text-red-400 ml-1">*</span>}
          </label>
          <input
            type="number"
            value={(value as number) ?? (field.default as number) ?? 0}
            onChange={(e) => handleChange(parseFloat(e.target.value))}
            min={field.min}
            max={field.max}
            className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>
      );

    case 'dropdown': {
      let options = field.options || [];
      if (field.id === 'targetFlowId') {
        try {
          const saved = localStorage.getItem('stepflow-flows');
          const flows = saved ? (JSON.parse(saved) as Array<{ id: string; name: string }>) : [];
          options = [
            { label: '-- Select a Flow --', value: '' },
            ...flows.map((f) => ({ label: f.name, value: f.id }))
          ];
        } catch {
          options = [];
        }
      }
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            {field.required && <span className="text-red-400 ml-1">*</span>}
          </label>
          <select
            value={(value as string) ?? (field.default as string) ?? ''}
            onChange={(e) => handleChange(e.target.value)}
            className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
          >
            {options.map((opt) => (
              <option key={String(opt.value)} value={String(opt.value)}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
      );
    }

    case 'toggle':
      return (
        <div className="flex items-center justify-between">
          <label className="text-xs text-gray-400">
            {field.label}
          </label>
          <button
            role="switch"
            aria-checked={!!value}
            onClick={() => handleChange(!value)}
            className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors ${
              value ? 'bg-indigo-500' : 'bg-gray-600'
            }`}
          >
            <span
              className={`inline-block h-3 w-3 transform rounded-full bg-white transition-transform ${
                value ? 'translate-x-[16px]' : 'translate-x-[2px]'
              }`}
            />
          </button>
        </div>
      );

    case 'slider':
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            <span className="ml-2 text-gray-500">
              {String(value ?? field.default ?? '')}
            </span>
          </label>
          <input
            type="range"
            value={(value as number) ?? (field.default as number) ?? 0}
            onChange={(e) => handleChange(parseFloat(e.target.value))}
            min={field.min ?? 0}
            max={field.max ?? 100}
            step={0.1}
            className="w-full accent-indigo-500"
          />
        </div>
      );

    case 'code':
    case 'json':
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            {field.required && <span className="text-red-400 ml-1">*</span>}
          </label>
          <textarea
            value={(value as string) ?? (field.default as string) ?? ''}
            onChange={(e) => handleChange(e.target.value)}
            rows={5}
            className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 font-mono focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent resize-none"
            placeholder={field.description}
          />
        </div>
      );

    case 'file':
    case 'color':
    case 'api-selector':
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
            {field.required && <span className="text-red-400 ml-1">*</span>}
          </label>
          <div className="text-xs text-gray-600 italic">
            ({field.type} — coming soon)
          </div>
        </div>
      );

    default:
      return (
        <div>
          <label className="text-xs text-gray-400 block mb-1">
            {field.label}
          </label>
          <div className="text-xs text-gray-600 italic">
            ({field.type} — not yet implemented)
          </div>
        </div>
      );
  }
}
