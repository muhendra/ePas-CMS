namespace e_Pas_CMS.ViewModels
{
    public class AuditReportListViewModel
    {
        public string ReportNo { get; set; }
        public string SpbuNo { get; set; }
        public string Region { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string SBM { get; set; }
        public string SAM { get; set; }
        public string Province { get; set; }
        public int Year { get; set; }
        public DateTime? AuditDate { get; set; }
        public DateTime? SubmitDate { get; set; }
        public string Auditor { get; set; }
        public string GoodStatus { get; set; }
        public string ExcellentStatus { get; set; }
        public decimal? Score { get; set; }
        public decimal? WTMS { get; set; }
        public decimal? QQ { get; set; }
        public decimal? WMEF { get; set; }
        public decimal? FormatFisik { get; set; }
        public decimal? CPO { get; set; }
        public string KelasSpbu { get; set; }
        public string TrxAuditId { get; set; }

        // Properti detail audit (dari DetailAuditViewModel)
        public string NamaAuditor { get; set; }
        public string Status { get; set; }
        public string Provinsi { get; set; }
        public string AlamatDetail { get; set; } // rename agar tidak bentrok dengan Address dari summary
        public decimal TotalScore { get; set; }
        public decimal MaxScore { get; set; }
        public decimal FinalScore { get; set; }
        public string AuditType { get; set; }
        public List<MediaItem> MediaNotes { get; set; } = new();
        public List<MediaItem> FinalDocuments { get; set; } = new();
        public List<AuditChecklistNode> Elements { get; set; } = new();
        public List<AuditQqCheckItem> QqChecks { get; set; } = new();
        public string Notes { get; set; }
    }

}
