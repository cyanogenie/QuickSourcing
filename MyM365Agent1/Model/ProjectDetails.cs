namespace MyM365Agent1.Model
{
    /// <summary>
    /// Project details for sourcing project creation
    /// </summary>
    public class ProjectDetails
    {
        public string ProjectTitle { get; set; } = "";
        public string ProjectDescription { get; set; } = "";
        public string EmailId { get; set; } = "";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal ApproxTotalBudget { get; set; }
    }
}