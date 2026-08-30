using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StepFlow.DataModel.Entities.DataSource
{
    /*
    public enum ActionEndpointType
    {
        File,
        Database,
        OpenAPISpecification,
        Queue,
        Event,
        Redis
    }
    */
    // What do we do with this data we have...
    // We need an endpoint to send this data and to allow us to move on...
    // This endpoint could be a URL for and API or SQS Queue etc
    public class ActionEndpoint : IActionEndpoint
    {
        public int ActionEndpointId { get; set; }

        // The friendly name for the action endpoint
        public string ActionEndpointName { get; set; }

        public string ActionEndpointURL { get; set; }
        
        // This endpoint will consume the ActionSchema data to effect the transaction.
      //  public string SchemaUUID { get; set; }

        // The refernece to the AttributeDomain
        public int? AttributeDomainId { get; set; }

        // The Type of ActionEndpoint this would be
    //    public ActionEndpointType ActionEndpointType { get; set; }
        
        // The implemtation reference
 //       public string ActionInvocationReference { get; set; }

        // The attached object to deal with invoking a Action Endpoint
        [NotMapped]
        public IActionInvocation ActionInvocation { get; set; }
    }
}