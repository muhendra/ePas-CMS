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
        public string AuditType { get; set; }
        public List<MediaItem> MediaNotes { get; set; } = new List<MediaItem>();


        public List<AuditChecklistNode> Elements { get; set; } = new(); // rekursif

        public List<AuditQqCheckItem> QqChecks { get; set; }

        public string Notes { get; set; } // Untuk isi catatan
        public List<MediaItem> FinalDocuments { get; set; } // Untuk list foto/video FINAL

    }

    public class MediaItem
    {
        public string MediaType { get; set; } // "IMAGE" or "VIDEO"
        public string MediaPath { get; set; }
    }

    public class UpdateScoreRequest
    {
        public string NodeId { get; set; }
        public string AuditId { get; set; }
        public string Score { get; set; }
    }

}
