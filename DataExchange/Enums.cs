using System.Text.Json.Serialization;

namespace StepFlow.DataModel.Entities
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ActionStage
    {
        DataTreatment,
        PreRouting,
        Routing,
        PostRouting
    }
 
    public enum ActionType
    {
        /// <summary>
        /// Executes a function that performs validation, calculation or other logic. The return value depends on the Function Type. 
        /// </summary>
        Logic,
        /// <summary>
        /// Transforms data from a source schema to a target schema using a map.
        /// </summary>
        Transformation,
        /// <summary>
        /// Enriches data by calling an external service (the endpoint). The result is added to the data row.
        /// </summary>
        EnrichmentLookup,
        /// <summary>
        /// Dispatches data to an external endpoint without expecting a response.
        /// </summary>
        Dispatch,
        ExecutePipeline
    }
    
    /// <summary>
    /// Defines the primitive data type of an Attribute.
    /// </summary>
    public enum AttributeDataType
    {
        String,
        Boolean,
        Number, // Could be float, int, decimal
        Date,
        Object,
        Array
    }

    public enum MergeStrategy
    {
        /// <summary>
        /// Only adds new keys from the lookup that do not already exist in the data row.
        /// This is the safest default behavior and prevents data overwrites.
        /// </summary>
        AddNewOnly = 0,

        /// <summary>
        /// Adds new keys and overwrites the values of any existing keys
        /// with the data returned from the lookup.
        /// </summary>
        OverwriteExisting = 1,

        /// <summary>
        /// If a key already exists, it appends the new value to the existing one,
        /// typically converting the value to a list or a concatenated string.
        /// (Note: The implementation will use string concatenation for simplicity).
        /// </summary>
        AppendValue = 2
    }

    public enum DataSourceMediumType
    {
        Api,
        ApiOAuth,
        Database,
        File
    }

    public enum LookupType
    {
        Api,
        SqlDatabase
    }

    public enum RuleType
    {
        Validation,   // Returns a boolean (true/false)
        Calculation,  // Returns a calculated value
        Selection     // Returns a string/object for routing decisions
    }

    public enum TransformType
    {
        /// <summary>
        /// A direct copy from a single source field.
        /// </summary>
        DirectCopy,

        /// <summary>
        /// Combines multiple source fields into one, separated by a character.
        /// Requires a "separator" parameter (e.g., " ").
        /// </summary>
        Combine,

        /// <summary>
        /// Performs a multiplication on two source fields.
        /// </summary>
        Multiply,

        /// <summary>
        /// Sets a fixed, default value. The value is specified in a "defaultValue" parameter.
        /// SourceFields is ignored.
        /// </summary>
        SetDefault,

        /// <summary>
        /// Trims leading and trailing whitespace from a single source field.
        /// </summary>
        Trim,

        /// <summary>
        /// Converts a single source field to uppercase.
        /// </summary>
        ToUpper,

        /// <summary>
        /// Formats a date/time value from a source field using a format string.
        /// Requires a "format" parameter (e.g., "yyyy-MM-dd").
        /// </summary>
        FormatDate
   }
  

}