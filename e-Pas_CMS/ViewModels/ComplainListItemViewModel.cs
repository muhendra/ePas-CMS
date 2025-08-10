namespace e_Pas_CMS.ViewModels
{
    public class ComplainListItemViewModel
    {
        public string FeedbackId { get; set; }
        public string TicketNo { get; set; }
        public string AuditId { get; set; }
        public string NoSpbu { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string Auditor { get; set; }
        public DateTime TanggalAudit { get; set; }
        public string TipeAudit { get; set; }
        public string AuditLevel { get; set; }
        public decimal Score { get; set; }
    }
}
