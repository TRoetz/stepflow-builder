using System.Data;

namespace StepFlow.DataModel.Entities.DataSource
{
    public interface IActionInvocation
    {
        void Send(DataTable table, DataRow row, string url);
    }
}