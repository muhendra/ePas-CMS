namespace e_Pas_CMS.Models
{
    public class TrxFeedbackPointApproval
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TrxFeedbackPointId { get; set; }
        public string Status { get; set; } // APPROVED / REJECTED
        public string? Notes { get; set; }
        public string ApprovedBy { get; set; }
        public DateTime ApprovedDate { get; set; } = DateTime.Now;
    }

}
