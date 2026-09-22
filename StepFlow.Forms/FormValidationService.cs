using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FORM VALIDATION — the engine half of UI Forms: coerces submitted values to their
    // AttributeDataType and enforces ValidationSchemaJson constraints (required,
    // minimum/maximum, minLength/maxLength, pattern). Transport-agnostic so it can be
    // reused by any host (the builder's FormCaptureController is one consumer).
    // ═══════════════════════════════════════════════════════════════════════════════

    public static class FormValidationService
    {
        /// <summary>
        /// Validates + coerces a submitted values object against the bound attribute contract.
        /// A null contract (unbound form) accepts any JSON object as-is. Absent optional values
        /// coerce to null and are skipped in the result object.
        /// </summary>
        public static (JObject Coerced, Dictionary<string, string> Errors) Validate(JToken? valuesToken, EntityAttribute[]? contract)
        {
            var coerced = new JObject();
            var errors = new Dictionary<string, string>();

            if (contract == null)
            {
                // Unbound form: accept any JSON object as-is.
                foreach (var prop in ((JObject)valuesToken).Properties())
                    coerced[prop.Name] = prop.Value;
                return (coerced, errors);
            }

            foreach (var attr in contract)
            {
                var raw = valuesToken[attr.AttributeName];
                if (!TryCoerceAttribute(attr, raw, out var value, out var error))
                    errors[attr.AttributeName] = error;
                else if (value != null)
                    coerced[attr.AttributeName] = value;
            }

            return (coerced, errors);
        }

        /// <summary>
        /// Coerces one submitted value to its AttributeDataType and enforces the ValidationSchemaJson
        /// constraints. Absent optional values coerce to null (skipped in the result object).
        /// </summary>
        public static bool TryCoerceAttribute(EntityAttribute attr, JToken? raw, out JToken? coercedValue, out string error)
        {
            coercedValue = null;
            error = "";

            var validation = ParseValidation(attr.ValidationSchemaJson);
            var required = attr.PrimaryKey || (validation?["required"]?.Type == JTokenType.Boolean && validation!["required"]!.Value<bool>());

            if (raw == null || raw.Type == JTokenType.Null)
            {
                if (required) error = "Required attribute";
                return error.Length == 0;
            }

            // Newtonsoft's DateParseHandling.Auto turns ISO-8601-looking strings into JTokenType.Date;
            // restore canonical round-trip text so every branch below sees stable, culture-free input.
            if (raw.Type == JTokenType.Date) raw = new JValue(raw.Value<DateTime>().ToString("o"));

            switch (attr.DataType)
            {
                case AttributeDataType.String:
                {
                    var s = raw.Type == JTokenType.String ? raw.Value<string>()! : raw.ToString(Formatting.None);
                    if (!CheckStringConstraints(s, validation, out error)) return false;
                    coercedValue = new JValue(s);
                    break;
                }
                case AttributeDataType.Number:
                {
                    double? number = null;
                    if (raw.Type == JTokenType.Integer || raw.Type == JTokenType.Float) number = raw.Value<double>();
                    else if (raw.Type == JTokenType.String && double.TryParse(raw.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        number = parsed;
                    if (number == null) { error = "Must be a number"; return false; }
                    if (!CheckNumberConstraints(number.Value, validation, out error)) return false;
                    coercedValue = new JValue(number.Value);
                    break;
                }
                case AttributeDataType.Boolean:
                {
                    bool? flag = null;
                    if (raw.Type == JTokenType.Boolean) flag = raw.Value<bool>();
                    else if (raw.Type == JTokenType.String && bool.TryParse(raw.Value<string>(), out var b)) flag = b;
                    if (flag == null) { error = "Must be a boolean"; return false; }
                    coercedValue = new JValue(flag.Value);
                    break;
                }
                case AttributeDataType.Date:
                {
                    string? s = raw.Type == JTokenType.String ? raw.Value<string>() : raw.ToString(Formatting.None);
                    if (s == null || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    { error = "Must be an ISO 8601 date"; return false; }
                    coercedValue = new JValue(dt.ToString("o"));
                    break;
                }
                default: // Object / Array — pass through uncast.
                    coercedValue = raw.DeepClone();
                    break;
            }

            return true;
        }

        private static JObject? ParseValidation(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var token = JToken.Parse(json);
                return token as JObject;
            }
            catch (JsonException)
            {
                return null; // malformed validation schema ⇒ no constraints enforced
            }
        }

        private static bool CheckStringConstraints(string s, JObject? v, out string error)
        {
            error = "";
            if (v != null)
            {
                if (v["minLength"]?.Type == JTokenType.Integer && s.Length < v["minLength"]!.Value<int>())
                    return Fail($"Must be at least {v["minLength"]} characters", out error);
                if (v["maxLength"]?.Type == JTokenType.Integer && s.Length > v["maxLength"]!.Value<int>())
                    return Fail($"Must be at most {v["maxLength"]} characters", out error);
                var pattern = v["pattern"]?.Type == JTokenType.String ? v["pattern"]!.Value<string>() : null;
                if (!string.IsNullOrEmpty(pattern) && !Regex.IsMatch(s, pattern))
                    return Fail("Does not match the required pattern", out error);
            }
            return true;

            static bool Fail(string message, out string err) { err = message; return false; }
        }

        private static bool CheckNumberConstraints(double n, JObject? v, out string error)
        {
            error = "";
            if (v != null)
            {
                if (v["minimum"]?.Type == JTokenType.Integer && n < v["minimum"]!.Value<double>())
                    return Fail($"Must be at least {v["minimum"]}", out error);
                if (v["maximum"]?.Type == JTokenType.Integer && n > v["maximum"]!.Value<double>())
                    return Fail($"Must be at most {v["maximum"]}", out error);
            }
            return true;

            static bool Fail(string message, out string err) { err = message; return false; }
        }
    }
}
