using Newtonsoft.Json.Linq;
using Jsonata.Net.Native;
using Jsonata.Net.Native.JsonNet;
using System;

namespace StepFunctionsApp.StepFunctions
{
    public static class JsonataProcessor
    {
        public static JToken Evaluate(string expression, JToken input)
        {
            if (string.IsNullOrWhiteSpace(expression)) return input;
            
            try
            {
                var query = new JsonataQuery(expression);
                var jsonataInput = Jsonata.Net.Native.JsonNet.JsonataExtensions.FromNewtonsoft(input);
                var result = query.Eval(jsonataInput, new EvaluationEnvironment());
                return Jsonata.Net.Native.JsonNet.JsonataExtensions.ToNewtonsoft(result);
            }
            catch (Exception ex)
            {
                throw new StepEngineException("States.JSONataEvaluationFailed", $"JSONata evaluation failed: {ex.Message}");
            }
        }

        public static JToken EvaluatePayloadTemplate(JToken template, JToken input)
        {
            var expression = template.ToString(Newtonsoft.Json.Formatting.None);
            return Evaluate(expression, input);
        }
    }
}
