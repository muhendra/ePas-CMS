namespace e_Pas_CMS.ViewModels
{
    public class SchedulerIndexViewModel
    {
        public List<SchedulerItemViewModel> Items { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int PageSize { get; set; }
        public string SearchTerm { get; set; }
        public int TotalItems { get; set; }
    }

    public class SchedulerItemViewModel
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string AppUserName { get; set; }
        public DateTime AuditDate { get; set; }
        public string AuditType { get; set; }
        public string AuditLevel { get; set; }
        public string SpbuNo { get; set; }
        public string ReportNo { get; set; }
        public string SBM { get; set; }
    }

    public class TrxAuditNotStartedLogViewModel
    {
        public string Id { get; set; } = null!;
        public string TrxAuditId { get; set; } = null!;
        public string? ReportNo { get; set; }
        public string SpbuId { get; set; } = null!;
        public string? SpbuNo { get; set; }
        public string? Region { get; set; }
        public string? Kota { get; set; }
        public string? OldStatus { get; set; }
        public string? NewStatus { get; set; }
        public string? OldFormStatusAuditor1 { get; set; }
        public string? NewFormStatusAuditor1 { get; set; }
        public string? ChangedBy { get; set; }
        public DateTime ChangedDateUtc { get; set; }
        public DateTime ChangedDateWib => ChangedDateUtc.AddHours(7);
        public string? Note { get; set; }
    }
}
