using System.Collections.Generic;
using System.Xml.Serialization;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // BPMN 2.0 XML MODELS (minimal subset for Camunda 8 import)
    // ═══════════════════════════════════════════════════════════════════════════════

    [XmlRoot("definitions", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
    public class BpmnDefinitions
    {
        [XmlAttribute("targetNamespace")]
        public string? TargetNamespace { get; set; }

        [XmlAttribute("id")]
        public string? Id { get; set; }

        [XmlElement("process", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnProcess> Processes { get; set; } = new();

        [XmlElement("bpmndi", Namespace = "http://www.omg.org/spec/BPMN/20100524/DI")]
        public object? BpmnDi { get; set; } // ignored for conversion
    }

    public class BpmnProcess
    {
        [XmlAttribute("id")]
        public string Id { get; set; } = "";

        [XmlAttribute("name")]
        public string? Name { get; set; }

        [XmlAttribute("isExecutable")]
        public string? IsExecutable { get; set; }

        // Flow nodes
        [XmlElement("startEvent", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnStartEvent> StartEvents { get; set; } = new();

        [XmlElement("endEvent", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnEndEvent> EndEvents { get; set; } = new();

        [XmlElement("task", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnTask> Tasks { get; set; } = new();

        [XmlElement("serviceTask", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnServiceTask> ServiceTasks { get; set; } = new();

        [XmlElement("scriptTask", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnScriptTask> ScriptTasks { get; set; } = new();

        [XmlElement("receiveTask", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnReceiveTask> ReceiveTasks { get; set; } = new();

        [XmlElement("sendTask", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnSendTask> SendTasks { get; set; } = new();

        // Gateways
        [XmlElement("exclusiveGateway", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnExclusiveGateway> ExclusiveGateways { get; set; } = new();

        [XmlElement("parallelGateway", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnParallelGateway> ParallelGateways { get; set; } = new();

        [XmlElement("inclusiveGateway", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnInclusiveGateway> InclusiveGateways { get; set; } = new();

        // Sub-process
        [XmlElement("subProcess", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnSubProcess> SubProcesses { get; set; } = new();

        // Multi-instance
        [XmlElement("multiInstance", Namespace = "http://camunda.org/schema/1.0/bpmn")]
        public BpmnMultiInstance? MultiInstance { get; set; }

        // Sequence flows
        [XmlElement("sequenceFlow", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnSequenceFlow> SequenceFlows { get; set; } = new();

        // IO
        [XmlElement("ioSpecification", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public BpmnIoSpecification? IoSpecification { get; set; }

        // Properties / extensions
        [XmlElement("extensionElements", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public BpmnExtensionElements? ExtensionElements { get; set; }

        [XmlElement("property", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnProperty> Properties { get; set; } = new();
    }

    // ── Flow Nodes ──────────────────────────────────────────────────────────────

    public class BpmnStartEvent
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlElement("extensionElements", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public BpmnExtensionElements? ExtensionElements { get; set; }
    }

    public class BpmnEndEvent
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
    }

    public class BpmnTask
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlElement("extensionElements", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public BpmnExtensionElements? ExtensionElements { get; set; }
        [XmlElement("multiInstance", Namespace = "http://camunda.org/schema/1.0/bpmn")]
        public BpmnMultiInstance? MultiInstance { get; set; }
    }

    public class BpmnServiceTask : BpmnTask
    {
        [XmlAttribute("calledElement")]
        public string? CalledElement { get; set; }

        [XmlAttribute("expression")]
        public string? Expression { get; set; }

        // Zeebe-specific
        [XmlElement("zeebe:taskDefinition", Namespace = "http://camunda.org/schema/zeebe/1.0")]
        public BpmnZeebeTaskDefinition? ZeebeTaskDefinition { get; set; }

        [XmlElement("zeebe:ioMapping", Namespace = "http://camunda.org/schema/zeebe/1.0")]
        public BpmnZeebeIoMapping? ZeebeIoMapping { get; set; }

        [XmlElement("extensionElements", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public new BpmnExtensionElements? ExtensionElements { get; set; }
    }

    public class BpmnScriptTask : BpmnTask
    {
        [XmlAttribute("scriptFormat")] public string? ScriptFormat { get; set; }
        [XmlElement("script", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public string? Script { get; set; }
    }

    public class BpmnReceiveTask : BpmnTask
    {
        [XmlAttribute("name")] public new string? Name { get; set; }
    }

    public class BpmnSendTask : BpmnTask
    {
        [XmlAttribute("name")] public new string? Name { get; set; }
        [XmlElement("extensionElements", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public new BpmnExtensionElements? ExtensionElements { get; set; }
    }

    // ── Gateways ────────────────────────────────────────────────────────────────

    public class BpmnExclusiveGateway
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlAttribute("default")] public string? DefaultFlow { get; set; }
    }

    public class BpmnParallelGateway
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
    }

    public class BpmnInclusiveGateway
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlAttribute("default")] public string? DefaultFlow { get; set; }
    }

    // ── Sub-process ─────────────────────────────────────────────────────────────

    public class BpmnSubProcess : BpmnProcess
    {
        [XmlAttribute("triggeredByEvent")] public string? TriggeredByEvent { get; set; }
    }

    // ── Sequence Flows ──────────────────────────────────────────────────────────

    public class BpmnSequenceFlow
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("sourceRef")] public string SourceRef { get; set; } = "";
        [XmlAttribute("targetRef")] public string TargetRef { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
        [XmlElement("conditionExpression", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public string? ConditionExpression { get; set; }
    }

    // ── Multi-Instance ──────────────────────────────────────────────────────────

    public class BpmnMultiInstance
    {
        [XmlAttribute("collection")] public string? Collection { get; set; }
        [XmlAttribute("elementVariable")] public string? ElementVariable { get; set; }
        [XmlAttribute("cardinality")] public string? Cardinality { get; set; }
        [XmlAttribute("sequential")] public string? Sequential { get; set; }
        [XmlAttribute("completionCondition")] public string? CompletionCondition { get; set; }
    }

    // ── IO Specification ────────────────────────────────────────────────────────

    public class BpmnIoSpecification
    {
        [XmlElement("dataInput", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnDataInput> DataInputs { get; set; } = new();

        [XmlElement("dataOutput", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnDataOutput> DataOutputs { get; set; } = new();

        [XmlElement("inputOutput", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public BpmnInputOutput? InputOutput { get; set; }
    }

    public class BpmnDataInput
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
    }

    public class BpmnDataOutput
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string? Name { get; set; }
    }

    public class BpmnInputOutput
    {
        [XmlElement("inputOutputParameter", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public List<BpmnInputOutputParameter> Parameters { get; set; } = new();
    }

    public class BpmnInputOutputParameter
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("source")] public string? Source { get; set; }
        [XmlAttribute("target")] public string? Target { get; set; }
    }

    // ── Extension Elements ──────────────────────────────────────────────────────

    public class BpmnExtensionElements
    {
        [XmlElement("camunda:properties", Namespace = "http://camunda.org/schema/1.0/bpmn")]
        public BpmnProperties? CamundaProperties { get; set; }

        [XmlElement("zeebe:properties", Namespace = "http://camunda.org/schema/zeebe/1.0")]
        public BpmnProperties? ZeebeProperties { get; set; }

        [XmlElement("extensionElements", Namespace = "http://www.omg.org/spec/BPMN/20100524/MODEL")]
        public BpmnExtensionElements? NestedExtensionElements { get; set; }
    }

    public class BpmnProperties
    {
        [XmlElement("property", Namespace = "http://camunda.org/schema/1.0/bpmn")]
        public List<BpmnPropertyItem> Items { get; set; } = new();

        [XmlElement("property", Namespace = "http://camunda.org/schema/zeebe/1.0")]
        public List<BpmnPropertyItem> ZeebeItems { get; set; } = new();
    }

    public class BpmnPropertyItem
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string Name { get; set; } = "";
        [XmlAttribute("value")] public string Value { get; set; } = "";
    }

    public class BpmnProperty
    {
        [XmlAttribute("id")] public string Id { get; set; } = "";
        [XmlAttribute("name")] public string Name { get; set; } = "";
    }

    // ── Zeebe-specific elements ─────────────────────────────────────────────────

    public class BpmnZeebeTaskDefinition
    {
        [XmlAttribute("type")] public string? Type { get; set; }
        [XmlAttribute("retries")] public string? Retries { get; set; }
    }

    public class BpmnZeebeIoMapping
    {
        [XmlElement("zeebe:input", Namespace = "http://camunda.org/schema/zeebe/1.0")]
        public List<BpmnZeebeInput> Inputs { get; set; } = new();

        [XmlElement("zeebe:output", Namespace = "http://camunda.org/schema/zeebe/1.0")]
        public List<BpmnZeebeOutput> Outputs { get; set; } = new();
    }

    public class BpmnZeebeInput
    {
        [XmlAttribute("source")] public string? Source { get; set; }
        [XmlAttribute("target")] public string? Target { get; set; }
    }

    public class BpmnZeebeOutput
    {
        [XmlAttribute("source")] public string? Source { get; set; }
        [XmlAttribute("target")] public string? Target { get; set; }
    }
}
