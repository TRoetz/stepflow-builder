using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EAV MAPPER — pure mapping from a dynamic JToken payload to a strict EAV
    // dictionary, given an entity definition. Extracted verbatim from
    // EavRegistryService so any IEavEntityProvider (registry file or attribute
    // domains) can feed the same rule://?eav= path.
    // ═══════════════════════════════════════════════════════════════════════════════

    public static class EavMapper
    {
        /// <summary>Transforms a raw StepFlow JToken into a strict EAV dictionary per the entity's attribute contract.</summary>
        public static Dictionary<string, object> Map(EavEntityDefinition entity, JToken payload)
        {
            var result = new Dictionary<string, object>();

            foreach (var attr in entity.Attributes)
            {
                var path = attr.JsonPathMapping;
                if (!path.StartsWith("$")) path = "$." + path;

                var token = payload.SelectToken(path);

                if (token == null && attr.IsRequired)
                    throw new StepEngineException("EAV.MissingRequiredAttribute", $"Required attribute '{attr.AttributeName}' not found at path '{attr.JsonPathMapping}'.");

                if (token != null)
                {
                    result[attr.AttributeName] = CastToEavType(token, attr.DataType);
                }
                else if (attr.DefaultValue != null)
                {
                    result[attr.AttributeName] = attr.DefaultValue;
                }
            }

            return result;
        }

        /// <summary>Coerces a JToken to the EAV data type ("string", "number", "boolean", "date"; anything else passes through as JSON text).</summary>
        public static object CastToEavType(JToken token, string dataType)
        {
            try
            {
                return dataType.ToLower() switch
                {
                    "number" => token.Value<double>(),
                    "boolean" => token.Value<bool>(),
                    "date" => token.Value<DateTime>(),
                    _ => token.ToString()
                };
            }
            catch (Exception ex)
            {
                throw new StepEngineException("EAV.TypeMismatch", $"Failed to cast attribute to {dataType}: {ex.Message}");
            }
        }
    }
}
