// ═══════════════════════════════════════════════════════════════════
//  BPMN Designer for StepFlow
//  Uses bpmn-js (bpmn-modeler) — same engine as Camunda Modeler
// ═══════════════════════════════════════════════════════════════════

const API = '/api';

// ── Default BPMN template with swimlanes ──────────────────────────
const DEFAULT_BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn">

  <bpmn:process id="my-process" name="My Process" isExecutable="true">

    <bpmn:laneSet id="LaneSet_1">
      <bpmn:lane id="Lane_Requester" name="Requester">
        <bpmn:flowNodeRef>StartEvent_Request</bpmn:flowNodeRef>
        <bpmn:flowNodeRef>Gateway_Route</bpmn:flowNodeRef>
      </bpmn:lane>
      <bpmn:lane id="Lane_Processor" name="Processor">
        <bpmn:flowNodeRef>Task_Process</bpmn:flowNodeRef>
        <bpmn:flowNodeRef>Task_Validate</bpmn:flowNodeRef>
        <bpmn:flowNodeRef>Task_Approve</bpmn:flowNodeRef>
      </bpmn:lane>
      <bpmn:lane id="Lane_Responder" name="Responder">
        <bpmn:flowNodeRef>Task_Notify</bpmn:flowNodeRef>
        <bpmn:flowNodeRef>EndEvent_Done</bpmn:flowNodeRef>
      </bpmn:lane>
    </bpmn:laneSet>

    <bpmn:startEvent id="StartEvent_Request" name="Request Received">
      <bpmn:outgoing>Flow_ToGateway</bpmn:outgoing>
    </bpmn:startEvent>

    <bpmn:exclusiveGateway id="Gateway_Route" name="Route Request">
      <bpmn:incoming>Flow_ToGateway</bpmn:incoming>
      <bpmn:outgoing>Flow_ToProcess</bpmn:outgoing>
      <bpmn:outgoing>Flow_ToValidate</bpmn:outgoing>
    </bpmn:exclusiveGateway>

    <bpmn:sequenceFlow id="Flow_ToGateway" sourceRef="StartEvent_Request" targetRef="Gateway_Route" />

    <bpmn:sequenceFlow id="Flow_ToProcess" name="Standard" sourceRef="Gateway_Route" targetRef="Task_Process">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=priority == "standard"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>

    <bpmn:serviceTask id="Task_Process" name="Process Request">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="http-call" />
        <zeebe:input source="http://localhost:5001/api/echo" target="url" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_ToProcess</bpmn:incoming>
      <bpmn:outgoing>Flow_ToNotify</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:sequenceFlow id="Flow_ToValidate" name="Urgent" sourceRef="Gateway_Route" targetRef="Task_Validate">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=priority == "urgent"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>

    <bpmn:serviceTask id="Task_Validate" name="Validate with AI">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="ai-prompt" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_ToValidate</bpmn:incoming>
      <bpmn:outgoing>Flow_ToApprove</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:sequenceFlow id="Flow_ToApprove" sourceRef="Task_Validate" targetRef="Task_Approve" />

    <bpmn:serviceTask id="Task_Approve" name="Approve">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="rule-evaluate" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_ToApprove</bpmn:incoming>
      <bpmn:outgoing>Flow_ToNotify</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:sequenceFlow id="Flow_ToNotify" sourceRef="Task_Process" targetRef="Task_Notify" />
    <bpmn:sequenceFlow id="Flow_ToNotify2" sourceRef="Task_Approve" targetRef="Task_Notify" />

    <bpmn:serviceTask id="Task_Notify" name="Send Notification">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="http-call" />
        <zeebe:input source="http://localhost:5001/api/echo" target="url" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_ToNotify</bpmn:incoming>
      <bpmn:incoming>Flow_ToNotify2</bpmn:incoming>
      <bpmn:outgoing>Flow_ToEnd</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:sequenceFlow id="Flow_ToEnd" sourceRef="Task_Notify" targetRef="EndEvent_Done" />

    <bpmn:endEvent id="EndEvent_Done" name="Done">
      <bpmn:incoming>Flow_ToEnd</bpmn:incoming>
    </bpmn:endEvent>

  </bpmn:process>

  <bpmn:collaboration id="Collaboration_1">
    <bpmn:participant id="Participant_1" name="My Process" processRef="my-process" />
  </bpmn:collaboration>

  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Collaboration_1">
      <bpmndi:BPMNShape id="Participant_1_di" bpmnElement="Participant_1" isHorizontal="true">
        <dc:Bounds x="150" y="50" width="900" height="550" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Lane_Requester_di" bpmnElement="Lane_Requester" isHorizontal="true">
        <dc:Bounds x="250" y="50" width="800" height="180" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Lane_Processor_di" bpmnElement="Lane_Processor" isHorizontal="true">
        <dc:Bounds x="250" y="230" width="800" height="200" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Lane_Responder_di" bpmnElement="Lane_Responder" isHorizontal="true">
        <dc:Bounds x="250" y="430" width="800" height="170" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="StartEvent_Request_di" bpmnElement="StartEvent_Request">
        <dc:Bounds x="302" y="112" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Gateway_Route_di" bpmnElement="Gateway_Route" isMarkerVisible="true">
        <dc:Bounds x="432" y="102" width="50" height="50" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Task_Process_di" bpmnElement="Task_Process">
        <dc:Bounds x="570" y="270" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Task_Validate_di" bpmnElement="Task_Validate">
        <dc:Bounds x="740" y="270" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Task_Approve_di" bpmnElement="Task_Approve">
        <dc:Bounds x="900" y="270" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Task_Notify_di" bpmnElement="Task_Notify">
        <dc:Bounds x="570" y="470" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="EndEvent_Done_di" bpmnElement="EndEvent_Done">
        <dc:Bounds x="742" y="472" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_ToGateway_di" bpmnElement="Flow_ToGateway">
        <di:waypoint x="338" y="130" />
        <di:waypoint x="432" y="130" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToProcess_di" bpmnElement="Flow_ToProcess">
        <di:waypoint x="457" y="152" />
        <di:waypoint x="457" y="310" />
        <di:waypoint x="570" y="310" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToValidate_di" bpmnElement="Flow_ToValidate">
        <di:waypoint x="482" y="130" />
        <di:waypoint x="790" y="130" />
        <di:waypoint x="790" y="270" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToApprove_di" bpmnElement="Flow_ToApprove">
        <di:waypoint x="840" y="310" />
        <di:waypoint x="900" y="310" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToNotify_di" bpmnElement="Flow_ToNotify">
        <di:waypoint x="620" y="350" />
        <di:waypoint x="620" y="470" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToNotify2_di" bpmnElement="Flow_ToNotify2">
        <di:waypoint x="950" y="310" />
        <di:waypoint x="1050" y="310" />
        <di:waypoint x="1050" y="510" />
        <di:waypoint x="670" y="510" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToEnd_di" bpmnElement="Flow_ToEnd">
        <di:waypoint x="670" y="510" />
        <di:waypoint x="742" y="490" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;


// ═══════════════════════════════════════════════════════════════════
//  Global State
// ═══════════════════════════════════════════════════════════════════

let modeler = null;
let currentSelection = null;
let gridVisible = true;
let lastConvertedJson = null;
let lastRegisteredName = null;

const logEl = document.getElementById('log-content');

function log(msg, level = 'info') {
    const entry = document.createElement('div');
    entry.className = `log-entry ${level}`;
    const time = new Date().toLocaleTimeString();
    entry.textContent = `[${time}] ${msg}`;
    logEl.appendChild(entry);
    logEl.scrollTop = logEl.scrollHeight;
}

// ═══════════════════════════════════════════════════════════════════
//  Modeler Initialization
// ═══════════════════════════════════════════════════════════════════

async function initModeler() {
    log('Initializing BPMN Modeler...');

    modeler = new BpmnJS({
        container: '#bpmn-canvas',
        keyboard: { bindTo: document },
        additionalModules: [],
        canvas: { deferUpdate: false }
    });

    try {
        const saved = localStorage.getItem('bpmn-designer-xml');
        const xml = saved || DEFAULT_BPMN;
        await modeler.importXML(xml);
        log(`Loaded BPMN diagram — ${modeler.get('canvas').getAllElements().length} elements`, 'success');
        updateProcessNameBadge();
        updatePreview();
    } catch (err) {
        log('Failed to import BPMN: ' + err.message, 'error');
        await modeler.importXML(DEFAULT_BPMN);
        log('Loaded default template', 'success');
    }

    const zoomScroll = modeler.get('zoomScroll');
    zoomScroll.zoom('fit-viewport', 'dvRoot');

    modeler.on('selection.changed', ({ newSelection }) => {
        currentSelection = newSelection[0] || null;
        onSelectionChanged(currentSelection);
    });

    modeler.on('canvas.viewbox.changed', ({ viewbox }) => {
        document.getElementById('canvas-zoom').textContent = Math.round(viewbox.scale * 100) + '%';
    });

    modeler.on('import.done', () => {
        updateProcessNameBadge();
        updatePreview();
        log('Diagram updated', 'info');
    });

    modeler.on('commandStack.changed', () => {
        updateUndoRedoButtons();
    });

    log('BPMN Modeler ready', 'success');
}

// ═══════════════════════════════════════════════════════════════════
//  Process Name Badge
// ═══════════════════════════════════════════════════════════════════

function updateProcessNameBadge() {
    const definitions = modeler.get('definitions');
    if (definitions && definitions.rootElements) {
        const process = definitions.rootElements.find(e => e.type === 'bpmn:Process');
        if (process) {
            document.getElementById('process-name-badge').textContent = process.name || process.id;
            return;
        }
    }
    document.getElementById('process-name-badge').textContent = 'Untitled Process';
}

// ═══════════════════════════════════════════════════════════════════
//  Properties Panel
// ═══════════════════════════════════════════════════════════════════

const TASK_TYPE_OPTIONS = [
    { value: '', label: '\u2014 Standard Task \u2014' },
    { value: 'http-call', label: '\ud83c\udf10 HTTP API Call' },
    { value: 'http-get', label: '\ud83c\udf10 HTTP GET' },
    { value: 'http-post', label: '\ud83c\udf10 HTTP POST' },
    { value: 'ai-prompt', label: '\ud83e\udd16 AI / LLM Prompt' },
    { value: 'ai-classify', label: '\ud83e\udd16 AI Classification' },
    { value: 'ai-summarize', label: '\ud83e\udd16 AI Summarize' },
    { value: 'rule-evaluate', label: '\ud83d\udccb Rule Engine' },
    { value: 'decision-table', label: '\ud83d\udccb Decision Table' },
    { value: 'flow-sub', label: '\ud83d\udd04 Sub-Flow / Nested' },
    { value: 'echo', label: '\ud83d\udd01 Echo / Passthrough' },
];

function onSelectionChanged(element) {
    const form = document.getElementById('properties-form');
    const placeholder = document.getElementById('properties-placeholder');

    if (!element) {
        form.style.display = 'none';
        placeholder.style.display = 'block';
        return;
    }

    placeholder.style.display = 'none';
    form.style.display = 'flex';

    const moddleElement = element.businessObject || element;
    const type = element.type;
    let html = '';

    html += propField('text', 'id', 'Element ID', moddleElement.id || '', true);

    if (moddleElement.name !== undefined) {
        html += propField('text', 'name', 'Name', moddleElement.name || '');
    }

    const docs = moddleElement.documentation;
    const docText = docs && docs.length > 0 ? (docs[0].text || docs[0].value || '') : '';
    html += propField('textarea', 'documentation', 'Documentation / Comment', docText);

    if (type === 'bpmn:ServiceTask' || type === 'bpmn:BusinessRuleTask' ||
        type === 'bpmn:ScriptTask' || type === 'bpmn:SendTask' ||
        type === 'bpmn:ReceiveTask' || type === 'bpmn:UserTask') {

        html += '<div class="prop-section-title">Task Configuration</div>';
        const zeebeDef = findZeebeExtension(moddleElement, 'taskDefinition');
        const taskType = zeebeDef ? (zeebeDef.type || '') : '';
        html += propSelect('taskType', 'Task Type', TASK_TYPE_OPTIONS, taskType);

        if (taskType && taskType.startsWith('http')) {
            const urlInput = findZeebeInput(moddleElement, 'url');
            html += propField('text', 'httpUrl', 'Target URL', urlInput || '');
        }
        if (taskType && taskType.startsWith('ai')) {
            const modelInput = findZeebeInput(moddleElement, 'model');
            html += propField('text', 'aiModel', 'AI Model', modelInput || 'gpt-4');
        }
        if (taskType && (taskType.startsWith('rule') || taskType.startsWith('decision'))) {
            const ruleInput = findZeebeInput(moddleElement, 'ruleId');
            html += propField('text', 'ruleId', 'Rule / Decision ID', ruleInput || '');
        }
        if (taskType && taskType.startsWith('flow')) {
            const flowInput = findZeebeInput(moddleElement, 'flowName');
            html += propField('text', 'flowName', 'Target Flow Name', flowInput || '');
        }
        if (type === 'bpmn:ScriptTask') {
            const script = moddleElement.script || '';
            html += propField('textarea', 'script', 'Script Body', script);
            const fmt = moddleElement.scriptFormat || 'javascript';
            html += propField('text', 'scriptFormat', 'Script Format', fmt);
        }
    }

    if (type === 'bpmn:ExclusiveGateway' || type === 'bpmn:InclusiveGateway' ||
        type === 'bpmn:ParallelGateway' || type === 'bpmn:EventBasedGateway') {
        html += '<div class="prop-section-title">Gateway Configuration</div>';
        const gatewayType = type.replace('bpmn:', '');
        html += `<p style="font-size:0.8rem;color:var(--text-dim);margin-bottom:8px;">
            Type: <strong>${gatewayType}</strong><br>
            ${type === 'bpmn:ExclusiveGateway' ? 'Use FEEL conditions on outgoing flows.' : ''}
            ${type === 'bpmn:InclusiveGateway' ? 'Multiple paths can be taken.' : ''}
            ${type === 'bpmn:ParallelGateway' ? 'All paths execute in parallel.' : ''}
        </p>`;
    }

    if (type === 'bpmn:SequenceFlow') {
        html += '<div class="prop-section-title">Flow Configuration</div>';
        const condition = moddleElement.conditionExpression;
        const condText = condition ? (condition.body || condition.value || '') : '';
        html += propField('text', 'condition', 'FEEL Condition', condText)
            + `<p style="font-size:0.7rem;color:var(--text-dim);margin-top:4px;">
                Examples: <code>creditScore &gt;= 700</code>, <code>status == "approved"</code>
              </p>`;
        const sourceRef = moddleElement.sourceRef ? (moddleElement.sourceRef.id || moddleElement.sourceRef) : '';
        const targetRef = moddleElement.targetRef ? (moddleElement.targetRef.id || moddleElement.targetRef) : '';
        html += `<div class="prop-group">
            <label>Source</label>
            <input type="text" value="${sourceRef}" readonly style="opacity:0.6">
        </div>
        <div class="prop-group">
            <label>Target</label>
            <input type="text" value="${targetRef}" readonly style="opacity:0.6">
        </div>`;
    }

    if (type === 'bpmn:StartEvent') {
        html += '<div class="prop-section-title">Start Event</div>';
        html += `<p style="font-size:0.8rem;color:var(--text-dim);">
            Entry point of the process. Maps to a <code>Pass</code> state in StepFlow.
        </p>`;
    }

    if (type === 'bpmn:EndEvent') {
        html += '<div class="prop-section-title">End Event</div>';
        html += `<p style="font-size:0.8rem;color:var(--text-dim);">
            Termination point. Maps to a <code>Succeed</code> state in StepFlow.
        </p>`;
    }

    if (type === 'bpmn:Lane') {
        html += '<div class="prop-section-title">Swimlane</div>';
        html += `<p style="font-size:0.8rem;color:var(--text-dim);">
            Swimlanes organize flow nodes by responsibility / department.
        </p>`;
    }

    form.innerHTML = html;
    bindPropertyChangeHandlers(form, element);
}

function propField(type, name, label, value, disabled = false) {
    const disabledAttr = disabled ? ' disabled' : '';
    if (type === 'textarea') {
        return `<div class="prop-group">
            <label for="prop-${name}">${label}</label>
            <textarea id="prop-${name}" name="${name}"${disabledAttr}>${escapeHtml(value)}</textarea>
        </div>`;
    }
    return `<div class="prop-group">
        <label for="prop-${name}">${label}</label>
        <input type="text" id="prop-${name}" name="${name}" value="${escapeHtml(value)}"${disabledAttr}>
    </div>`;
}

function propSelect(name, label, options, selected) {
    const opts = options.map(o =>
        `<option value="${o.value}" ${o.value === selected ? 'selected' : ''}>${o.label}</option>`
    ).join('');
    return `<div class="prop-group">
        <label for="prop-${name}">${label}</label>
        <select id="prop-${name}" name="${name}">${opts}</select>
    </div>`;
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
              .replace(/"/g, '&quot;').replace(/'/g, '&#039;');
}

// ═══════════════════════════════════════════════════════════════════
//  Zeebe Extension Helpers
// ═══════════════════════════════════════════════════════════════════

function findZeebeExtension(moddleElement, typeName) {
    const exts = moddleElement.extensionElements;
    if (!exts || !exts.values) return null;
    return exts.values.find(e => e.$type && e.$type.endsWith(typeName));
}

function findZeebeInput(moddleElement, targetName) {
    const exts = moddleElement.extensionElements;
    if (!exts || !exts.values) return '';
    const input = exts.values.find(e => e.$type && e.$type.endsWith('ZeebeInput') && e.target === targetName);
    return input ? (input.value || input.source || '') : '';
}

// ═══════════════════════════════════════════════════════════════════
//  Property Change Handlers
// ═══════════════════════════════════════════════════════════════════

function bindPropertyChangeHandlers(form, element) {
    const moddle = modeler.get('moddle');
    const modeling = modeler.get('modeling');
    const moddleElement = element.businessObject || element;

    form.querySelectorAll('input, textarea, select').forEach(input => {
        input.addEventListener('change', () => {
            const name = input.name;
            const value = input.value;

            try {
                switch (name) {
                    case 'id':
                        modeling.updateProperties(moddleElement, { id: value });
                        break;
                    case 'name':
                        modeling.updateProperties(moddleElement, { name: value || undefined });
                        break;
                    case 'documentation':
                        if (value) {
                            const doc = moddle.create('bpmn:Documentation', { text: value });
                            modeling.updateProperties(moddleElement, { documentation: [doc] });
                        } else {
                            modeling.updateProperties(moddleElement, { documentation: [] });
                        }
                        break;
                    case 'taskType':
                        updateTaskType(moddleElement, moddle, modeling, value);
                        break;
                    case 'httpUrl':
                        updateZeebeInput(moddleElement, moddle, modeling, 'url', value);
                        break;
                    case 'aiModel':
                        updateZeebeInput(moddleElement, moddle, modeling, 'model', value);
                        break;
                    case 'ruleId':
                        updateZeebeInput(moddleElement, moddle, modeling, 'ruleId', value);
                        break;
                    case 'flowName':
                        updateZeebeInput(moddleElement, moddle, modeling, 'flowName', value);
                        break;
                    case 'script':
                        modeling.updateProperties(moddleElement, { script: value });
                        break;
                    case 'scriptFormat':
                        modeling.updateProperties(moddleElement, { scriptFormat: value });
                        break;
                    case 'condition':
                        if (element.type === 'bpmn:SequenceFlow') {
                            updateConditionExpression(moddleElement, moddle, modeling, value);
                        }
                        break;
                }
                updateProcessNameBadge();
                updatePreview();
                log(`Property '${name}' updated`, 'success');
            } catch (err) {
                log(`Failed to update '${name}': ${err.message}`, 'error');
            }
        });
    });
}

function updateTaskType(moddleElement, moddle, modeling, type) {
    let taskDef = findZeebeExtension(moddleElement, 'taskDefinition');
    if (!taskDef && type) {
        taskDef = moddle.create('zeebe:TaskDefinition', { type: '' });
        if (!moddleElement.extensionElements) {
            moddleElement.extensionElements = moddle.create('bpmn:ExtensionElements', { values: [] });
        }
        moddleElement.extensionElements.values.push(taskDef);
    } else if (taskDef && !type) {
        const idx = moddleElement.extensionElements.values.indexOf(taskDef);
        if (idx >= 0) moddleElement.extensionElements.values.splice(idx, 1);
        return;
    }
    if (taskDef) taskDef.type = type;
}

function updateZeebeInput(moddleElement, moddle, modeling, target, value) {
    let existing = null;
    const exts = moddleElement.extensionElements;
    if (exts && exts.values) {
        existing = exts.values.find(e => e.$type && e.$type.endsWith('ZeebeInput') && e.target === target);
    }
    if (!existing && value) {
        existing = moddle.create('zeebe:ZeebeInput', { target, value: '' });
        if (!exts) {
            moddleElement.extensionElements = moddle.create('bpmn:ExtensionElements', { values: [] });
        }
        exts.values.push(existing);
    } else if (existing && !value) {
        if (exts) {
            const idx = exts.values.indexOf(existing);
            if (idx >= 0) exts.values.splice(idx, 1);
        }
        return;
    }
    if (existing) {
        existing.value = value;
        existing.source = value;
    }
}

function updateConditionExpression(moddleElement, moddle, modeling, value) {
    if (value) {
        const expr = moddle.create('bpmn:FormalExpression', { body: value });
        modeling.updateProperties(moddleElement, { conditionExpression: expr });
    } else {
        modeling.updateProperties(moddleElement, { conditionExpression: null });
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Toolbar Actions
// ═══════════════════════════════════════════════════════════════════

function updateUndoRedoButtons() {
    const commandStack = modeler.get('commandStack');
    document.getElementById('btn-undo').disabled = !commandStack.canUndo();
    document.getElementById('btn-redo').disabled = !commandStack.canRedo();
}

async function getCurrentXML() {
    return await modeler.saveXML({ format: true });
}

async function getCurrentXMLCompact() {
    return await modeler.saveXML({ format: false });
}

// ── Save ──
document.getElementById('btn-save').addEventListener('click', async () => {
    try {
        const { xml } = await getCurrentXML();
        localStorage.setItem('bpmn-designer-xml', xml);

        // Also save to server
        const definitions = modeler.get('definitions');
        const process = definitions.rootElements.find(e => e.type === 'bpmn:Process');
        const name = process ? (process.name || process.id) : 'untitled';

        const res = await fetch(`${API}/bpmn/save`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, xml })
        });

        if (res.ok) {
            log('Saved to server and localStorage', 'success');
        } else {
            log('Saved to localStorage (server save failed)', 'warning');
        }
    } catch (err) {
        log('Save failed: ' + err.message, 'error');
    }
});

// ── Undo / Redo ──
document.getElementById('btn-undo').addEventListener('click', () => {
    modeler.get('commandStack').undo();
});

document.getElementById('btn-redo').addEventListener('click', () => {
    modeler.get('commandStack').redo();
});

// ── Zoom ──
document.getElementById('btn-zoom-in').addEventListener('click', () => {
    modeler.get('zoomScroll').zoom('in');
});

document.getElementById('btn-zoom-out').addEventListener('click', () => {
    modeler.get('zoomScroll').zoom('out');
});

document.getElementById('btn-fit-view').addEventListener('click', () => {
    modeler.get('zoomScroll').zoom('fit-viewport', 'dvRoot');
});

// ── Toggle Grid ──
document.getElementById('btn-toggle-grid').addEventListener('click', () => {
    gridVisible = !gridVisible;
    const canvas = modeler.get('canvas');
    // bpmn-js doesn't have a direct grid toggle, so we toggle CSS
    const el = document.getElementById('bpmn-canvas');
    if (gridVisible) {
        el.style.backgroundImage = '';
    } else {
        el.style.backgroundImage = 'none';
    }
    document.getElementById('btn-toggle-grid').textContent = gridVisible ? '▦ Grid' : '▦ No Grid';
});

// ═══════════════════════════════════════════════════════════════════
//  XML Modal
// ═══════════════════════════════════════════════════════════════════

document.getElementById('btn-xml').addEventListener('click', async () => {
    try {
        const { xml } = await getCurrentXML();
        document.getElementById('xml-textarea').value = xml;
        document.getElementById('xml-modal').classList.add('visible');
    } catch (err) {
        log('Failed to export XML: ' + err.message, 'error');
    }
});

document.getElementById('btn-close-xml').addEventListener('click', () => {
    document.getElementById('xml-modal').classList.remove('visible');
});

document.getElementById('btn-copy-xml').addEventListener('click', () => {
    const xml = document.getElementById('xml-textarea').value;
    navigator.clipboard.writeText(xml);
    log('XML copied to clipboard', 'success');
});

document.getElementById('btn-download-xml').addEventListener('click', () => {
    const xml = document.getElementById('xml-textarea').value;
    const blob = new Blob([xml], { type: 'application/xml' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'process.bpmn';
    a.click();
    URL.revokeObjectURL(url);
    log('Downloaded BPMN file', 'success');
});

document.getElementById('btn-apply-xml').addEventListener('click', async () => {
    const xml = document.getElementById('xml-textarea').value;
    try {
        await modeler.importXML(xml);
        document.getElementById('xml-modal').classList.remove('visible');
        log('XML applied successfully', 'success');
        updateProcessNameBadge();
        updatePreview();
    } catch (err) {
        log('Failed to import XML: ' + err.message, 'error');
    }
});

// ═══════════════════════════════════════════════════════════════════
//  Convert to StepFlow
// ═══════════════════════════════════════════════════════════════════

document.getElementById('btn-convert').addEventListener('click', async () => {
    try {
        const { xml } = await getCurrentXML();
        const res = await fetch(`${API}/bpmn/convert`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/xml' },
            body: xml
        });

        if (!res.ok) {
            throw new Error(`HTTP ${res.status}: ${await res.text()}`);
        }

        const data = await res.json();
        lastConvertedJson = data.stateMachine || data;

        // Show in modal
        document.getElementById('convert-json-output').textContent = JSON.stringify(lastConvertedJson, null, 2);
        document.getElementById('convert-modal').classList.add('visible');

        // Also update preview panel
        renderPreview(lastConvertedJson);
        log(`Converted to StepFlow — ${Object.keys(lastConvertedJson.states || {}).length} states`, 'success');
    } catch (err) {
        log('Convert failed: ' + err.message, 'error');
    }
});

document.getElementById('btn-close-convert').addEventListener('click', () => {
    document.getElementById('convert-modal').classList.remove('visible');
});

document.getElementById('btn-copy-convert').addEventListener('click', () => {
    if (lastConvertedJson) {
        navigator.clipboard.writeText(JSON.stringify(lastConvertedJson, null, 2));
        log('StepFlow JSON copied to clipboard', 'success');
    }
});

// ═══════════════════════════════════════════════════════════════════
//  Register & Execute
// ═══════════════════════════════════════════════════════════════════

document.getElementById('btn-register-flow').addEventListener('click', async () => {
    if (!lastConvertedJson) {
        log('Convert first, then register', 'warning');
        return;
    }

    try {
        const definitions = modeler.get('definitions');
        const process = definitions.rootElements.find(e => e.type === 'bpmn:Process');
        const name = process ? (process.name || process.id) : 'dynamic-flow';

        const res = await fetch(`${API}/flows/register`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name,
                definition: lastConvertedJson
            })
        });

        const result = await res.json();
        lastRegisteredName = name;

        if (res.ok) {
            log(`Flow registered: ${name} (ID: ${result.id})`, 'success');
            document.getElementById('convert-modal').classList.remove('visible');
            document.getElementById('execute-modal').classList.add('visible');
        } else {
            log(`Register failed: ${result.error || 'Unknown error'}`, 'error');
        }
    } catch (err) {
        log('Register failed: ' + err.message, 'error');
    }
});

document.getElementById('btn-close-execute').addEventListener('click', () => {
    document.getElementById('execute-modal').classList.remove('visible');
});

document.getElementById('btn-run-execute').addEventListener('click', async () => {
    if (!lastRegisteredName) {
        log('Register flow first', 'warning');
        return;
    }

    const inputJson = document.getElementById('execute-input').value;
    let input;
    try {
        input = JSON.parse(inputJson);
    } catch {
        log('Invalid JSON input', 'error');
        return;
    }

    try {
        const res = await fetch(`${API}/flows/execute-sync/${lastRegisteredName}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(input)
        });

        const result = await res.json();

        // Show result
        const statusMap = { 0: 'Running', 1: 'Succeeded', 2: 'Failed' };
        const statusClass = result.status === 1 ? 'success' : result.status === 2 ? 'failed' : 'running';

        document.getElementById('result-summary').innerHTML = `
            <div class="result-summary">
                <span class="result-badge ${statusClass}">${statusMap[result.status] || 'Unknown'}</span>
                <span class="result-badge" style="background:rgba(148,163,184,0.15);color:#94a3b8;">
                    Execution: ${result.executionId}
                </span>
                <span class="result-badge" style="background:rgba(148,163,184,0.15);color:#94a3b8;">
                    State: ${result.currentState}
                </span>
            </div>
        `;
        document.getElementById('result-json').textContent = JSON.stringify(result, null, 2);
        document.getElementById('result-modal').classList.add('visible');

        log(`Execution ${result.status === 1 ? 'succeeded' : 'failed'} — ${result.currentState}`,
            result.status === 1 ? 'success' : 'error');
    } catch (err) {
        log('Execution failed: ' + err.message, 'error');
    }
});

document.getElementById('btn-close-result').addEventListener('click', () => {
    document.getElementById('result-modal').classList.remove('visible');
});

// ═══════════════════════════════════════════════════════════════════
//  Open / Import from file
// ═══════════════════════════════════════════════════════════════════

document.getElementById('btn-import-flow').addEventListener('click', () => {
    document.getElementById('file-input').click();
});

document.getElementById('file-input').addEventListener('change', async (e) => {
    const file = e.target.files[0];
    if (!file) return;

    try {
        const text = await file.text();
        await modeler.importXML(text);
        localStorage.setItem('bpmn-designer-xml', text);
        log(`Loaded: ${file.name}`, 'success');
        updateProcessNameBadge();
        updatePreview();
    } catch (err) {
        log('Failed to load file: ' + err.message, 'error');
    }
    e.target.value = '';
});

// ═══════════════════════════════════════════════════════════════════
//  Preview Panel
// ═══════════════════════════════════════════════════════════════════

function updatePreview() {
    // Lightweight preview — just show element counts
    const canvas = modeler.get('canvas');
    const elements = canvas.getAllElements();
    const counts = {};
    elements.forEach(el => {
        const t = el.type.replace('bpmn:', '').replace('bpmn:', '');
        counts[t] = (counts[t] || 0) + 1;
    });

    const previewEl = document.getElementById('preview-placeholder');
    let summary = `<p style="margin-bottom:8px;">${elements.length} elements total</p>`;
    for (const [type, count] of Object.entries(counts)) {
        summary += `<div style="font-size:0.75rem;color:var(--text-dim);margin:2px 0;">
            ${type}: ${count}
        </div>`;
    }
    previewEl.innerHTML = summary;
}

function renderPreview(definition) {
    const placeholder = document.getElementById('preview-placeholder');
    const content = document.getElementById('preview-content');

    placeholder.style.display = 'none';
    content.style.display = 'block';

    const states = definition.states || {};
    let html = `<div style="margin-bottom:12px;font-size:0.85rem;font-weight:600;">
        StartAt: <code>${definition.startAt || '—'}</code>
    </div>`;

    for (const [name, state] of Object.entries(states)) {
        const typeClass = `type-${state.type?.toLowerCase() || 'pass'}`;
        html += `<div class="preview-state">
            <div class="preview-state-header">
                <span class="preview-state-name">${name}</span>
                <span class="preview-state-type ${typeClass}">${state.type || 'Pass'}</span>
            </div>
            <div class="preview-detail">`;

        if (state.resource) {
            html += `<div>Resource: <code>${state.resource}</code></div>`;
        }
        if (state.next) {
            html += `<div>Next: <code>${state.next}</code></div>`;
        }
        if (state.choices && state.choices.length) {
            html += `<div>Choices: ${state.choices.length} rules</div>`;
        }
        if (state.defaultResult !== undefined) {
            html += `<div>Default: <code>${state.defaultResult}</code></div>`;
        }

        html += `</div></div>`;
    }

    content.innerHTML = html;
}

// ═══════════════════════════════════════════════════════════════════
//  Panel Tab Switching
// ═══════════════════════════════════════════════════════════════════

document.querySelectorAll('#panel-tabs .panel-tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('#panel-tabs .panel-tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');

        const panelId = tab.dataset.panel;
        document.querySelectorAll('#panel-content .panel-section').forEach(s => s.classList.remove('active'));
        document.getElementById(`panel-${panelId}`).classList.add('active');
    });
});

document.querySelectorAll('#right-panel-tabs .panel-tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('#right-panel-tabs .panel-tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');

        const panelId = tab.dataset.panel;
        document.querySelectorAll('#right-panel-content .panel-section').forEach(s => s.classList.remove('active'));
        document.getElementById(`panel-${panelId}`).classList.add('active');
    });
});

// ═══════════════════════════════════════════════════════════════════
//  Palette Info Panel
// ═══════════════════════════════════════════════════════════════════

function populatePaletteInfo() {
    const el = document.getElementById('panel-palette');
    el.innerHTML = `
        <div class="palette-info">
            <h3>Quick Start</h3>
            <p>Use the <strong>bpmn-js palette</strong> on the left side of the canvas to drag elements:</p>
            <ul>
                <li><strong>Events</strong> — Start, End, Intermediate</li>
                <li><strong>Tasks</strong> — Service, Script, User, Receive</li>
                <li><strong>Gateways</strong> — Exclusive, Parallel, Inclusive</li>
                <li><strong>Swimlanes</strong> — LaneSet &amp; Lane</li>
            </ul>

            <h3>Task Types (Properties Panel)</h3>
            <p>Select a task, then set its type in the Properties panel:</p>
            <ul>
                <li><code>http-call</code> — Call an API endpoint</li>
                <li><code>ai-prompt</code> — Call AI/LLM service</li>
                <li><code>rule-evaluate</code> — Run a rule engine check</li>
                <li><code>flow-sub</code> — Call another StepFlow</li>
            </ul>

            <h3>FEEL Conditions</h3>
            <p>Set conditions on <strong>Sequence Flows</strong> from gateways:</p>
            <ul>
                <li><code>creditScore &gt;= 700</code></li>
                <li><code>status == "approved"</code></li>
                <li><code>amount &gt; 1000 and type == "premium"</code></li>
            </ul>

            <h3>Keyboard Shortcuts</h3>
            <ul>
                <li><code>Ctrl+Z</code> / <code>Ctrl+Y</code> — Undo / Redo</li>
                <li><code>Ctrl+S</code> — Save</li>
                <li><code>Delete</code> — Remove selected</li>
                <li><code>Space+Drag</code> — Pan canvas</li>
                <li><code>Scroll</code> — Zoom in/out</li>
            </ul>
        </div>
    `;
}

// ═══════════════════════════════════════════════════════════════════
//  Keyboard Shortcuts
// ═══════════════════════════════════════════════════════════════════

document.addEventListener('keydown', (e) => {
    // Ctrl+S — Save
    if ((e.ctrlKey || e.metaKey) && e.key === 's') {
        e.preventDefault();
        document.getElementById('btn-save').click();
    }
});

// ═══════════════════════════════════════════════════════════════════
//  Modal Close on backdrop click
// ═══════════════════════════════════════════════════════════════════

document.querySelectorAll('.modal').forEach(modal => {
    modal.addEventListener('click', (e) => {
        if (e.target === modal) {
            modal.classList.remove('visible');
        }
    });
});

// ═══════════════════════════════════════════════════════════════════
//  Initialize
// ═══════════════════════════════════════════════════════════════════

populatePaletteInfo();
initModeler();
