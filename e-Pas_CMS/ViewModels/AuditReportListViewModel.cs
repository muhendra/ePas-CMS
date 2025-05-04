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
    }

}
