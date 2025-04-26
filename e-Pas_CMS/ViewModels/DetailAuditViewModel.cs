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

        public decimal TotalScore { get; set; }
        public decimal MaxScore { get; set; }
        public decimal FinalScore { get; set; }

        public List<AuditChecklistNode> Elements { get; set; } = new(); // ✅ versi rekursif
    }

    public class UpdateScoreRequest
    {
        public string TacId { get; set; }
        public string ScoreInput { get; set; }
    }

    public class MediaItem
    {
        public string MediaType { get; set; } // "IMAGE" or "VIDEO"
        public string MediaPath { get; set; }
    }

}
