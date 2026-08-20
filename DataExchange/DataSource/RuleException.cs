using System.ComponentModel.DataAnnotations;

namespace StepFlow.DataModel.Entities.DataSource
{
    public class RuleExceptions
    {
        public int Id { get; set; }

        // This is not a definitive list but covers most issues we can foresee
        public enum RuleExceptionTypes
        {
            RULE_CHECK_INVALIDATED, // When the rule did not pass its internal check/test
            RULE_SQL_ERROR, // The SQL statement executed has errors
            RULE_ATTRIBUTE_NOT_FOUND, // When a rule has a Attribute defined for an attribute outside its scope but not included in the schema domain.
            RULE_COLLISION, // When one or more rules are in conflict of one-another this is the flag to set.
            RULE_PARSER_ERROR, // When we have an internal parser error - example RISK !!= HRSK
            RULE_VALUE_OUT_OF_BOUNDS // When the value or an value type exceeds the value type expected.
        }

        // This inner class covers the rule exception details
        public class RuleException
        {
            public long OriginAttributeID; // The attribute that was updated from the UI that kicked off the rule execution.
            public String OriginAttributeName; // The attribute name that was loaded from the ImportSchema that kicked off the rule execution.
            public List<String> AffectedAttributeNames; // All the attributeNames that we should mark as being affected. These are found inside the rule that is being executed.
            public String Rule; // The actual rule syntax
            public String RuleExceptionMessage; // The friendly message if a rule has failed
            public RuleExceptionTypes RuleExceptionType; // The exception type
        }

        private List<RuleException> exceptions = new List<RuleException>();

        public void AddRuleException(String originAttributeName, List<String> affectedAttributeNames, String rule, String ruleExceptionMessage, RuleExceptionTypes ruleExceptionType)
        {
            RuleException ruleException = new RuleException();
            ruleException.OriginAttributeName = originAttributeName;
            ruleException.AffectedAttributeNames = affectedAttributeNames;
            ruleException.Rule = rule;
            ruleException.RuleExceptionMessage = ruleExceptionMessage;
            ruleException.RuleExceptionType = ruleExceptionType;
            exceptions.Add(ruleException);
        }

        public RuleException FindRuleExceptionByAttName(String AttributeName)
        {
            if (exceptions != null)
            {
                if (exceptions.Count >= 1)
                {
                    foreach (RuleException ruleException in exceptions)
                    {
                        foreach (String attributeName in ruleException.AffectedAttributeNames)
                        {
                            if (attributeName.ToUpper().Equals(AttributeName.ToUpper()))
                            {
                                return ruleException;
                            }
                        }
                    }
                }
            }
            return null;
        }

        public void ClearRuleExceptions()
        {
            exceptions.Clear();
        }
    }
}