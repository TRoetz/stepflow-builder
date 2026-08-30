using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Regression tests for checkpoint round-trip null semantics.
    /// Newtonsoft writes C#-null JToken properties as explicit JSON null, which used to come back
    /// as JValue(null) — failing every `== null` check in the engine (e.g. ApplyParameters treating
    /// "no Parameters" as a template evaluating to null after resume). NullableJTokenConverter maps
    /// both directions to C# null; these tests pin that contract.
    /// </summary>
    public class CheckpointNullSemanticsTests
    {
        private static FlowStateRecord BuildRecord() => new()
        {
            ExecutionId = "e1",
            Definition = new StateMachineDefinition
            {
                StartAt = "A",
                States = new System.Collections.Generic.Dictionary<string, StateDefinition>
                {
                    ["C"] = new() { Type = StateType.Task, Resource = "test://c", End = true } // all JToken props C#-null
                }
            },
            Execution = new Execution
            {
                ExecutionId = "e1",
                StateMachineName = "probe",
                Status = ExecutionStatus.Suspended,
                CurrentState = "C"
            }
        };

        private static FlowStateRecord RoundTrip(FlowStateRecord record) =>
            JsonConvert.DeserializeObject<FlowStateRecord>(JsonConvert.SerializeObject(record))!;

        [Fact]
        public void AbsentJTokenProperties_RoundTripAsCSharpNull()
        {
            var record = BuildRecord();
            Assert.Null(record.Definition.States["C"].Parameters); // precondition: built in-memory as null

            var back = RoundTrip(record);

            var c = back.Definition.States["C"];
            Assert.Null(c.Parameters);      // was JValue(null) before the fix -> broke ApplyParameters on resume
            Assert.Null(c.ResultSelector);
            Assert.Null(c.Task);
            Assert.Null(c.Completion);
            Assert.Null(c.Result);
            Assert.Null(back.Execution.PendingInput);
        }

        [Fact]
        public void SetValues_SurviveRoundTrip()
        {
            var record = BuildRecord();
            record.Definition.States["C"].Parameters = new JObject { ["orderId"] = 42 };
            record.Execution.PendingInput = new JObject { ["resumed"] = true };

            var back = RoundTrip(record);

            Assert.Equal(new JObject { ["orderId"] = 42 }, back.Definition.States["C"].Parameters);
            Assert.Equal(new JObject { ["resumed"] = true }, back.Execution.PendingInput);
        }

        [Fact]
        public void NestedJsonNulls_InTokenContent_ArePreserved()
        {
            // A JSON null *inside* a token's content is data, not absence — must survive untouched.
            var record = BuildRecord();
            record.Definition.States["C"].Parameters = JObject.Parse("{\"a\":null,\"b\":1}");

            var back = RoundTrip(record);

            Assert.NotNull(back.Definition.States["C"].Parameters);
            Assert.Equal(JTokenType.Null, back.Definition.States["C"].Parameters!["a"]!.Type);
            Assert.Equal(1, (int)back.Definition.States["C"].Parameters!["b"]!);
        }

        [Fact]
        public void ExplicitJsonNullInSource_DeserializesAsCSharpNull()
        {
            // Flow files / API payloads may contain explicit nulls; they must mean "absent".
            var json = "{\"StartAt\":\"A\",\"States\":{\"C\":{\"Type\":\"Task\",\"Resource\":\"test://c\",\"End\":true,\"Parameters\":null}}}";

            var def = JsonConvert.DeserializeObject<StateMachineDefinition>(json)!;

            Assert.Null(def.States["C"].Parameters);
        }
    }
}
