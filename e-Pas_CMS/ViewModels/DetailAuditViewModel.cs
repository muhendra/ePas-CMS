namespace e_Pas_CMS.ViewModels
{
    public class DetailAuditViewModel
    {
        public string ReportNo { get; set; }
        public string NamaAuditor { get; set; }
        public DateTime? TanggalSubmit { get; set; }
        public string Status { get; set; }

        public string SpbuNo { get; set; }
        public string Provinsi { get; set; }
        public string Kota { get; set; }
        public string Alamat { get; set; }

        public List<AuditChecklistNode> Elements { get; set; } = new(); // ✅ versi rekursif
    }
}
