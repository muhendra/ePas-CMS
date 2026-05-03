namespace e_Pas_CMS.ViewModels
{
    public class DetailAuditViewModel
    {
        public string ReportNo { get; set; }
        public string NamaAuditor { get; set; }

        public string Auditor2 { get; set; }
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

        public List<AuditChecklistNode> Elements { get; set; } = new();

        public List<AuditQqCheckItem> QqChecks { get; set; }

        public string Notes { get; set; }
        public List<MediaItem> FinalDocuments { get; set; }
        public string PenaltyAlert { get; set; }
    }

    public class MediaItem
    {
        public string Id { get; set; }
        public string MediaType { get; set; }
        public string MediaPath { get; set; }
        public string FileName { get; set; }
        public string SizeReadable { get; set; }
        public bool IsStar { get; set; }
    }

    public class SetPrimaryMediaRequest
    {
        public string AuditId { get; set; }
        public string NodeId { get; set; }
        public string MediaId { get; set; }
    }

    public class UpdateScoreRequest
    {
        public string NodeId { get; set; }
        public string AuditId { get; set; }
        public string Score { get; set; }
        public string Comment { get; set; }
    }

    public class UpdateQqRequest
    {
        public string QqId { get; set; }
        public string AuditId { get; set; }

        public string NozzleNumber { get; set; }
        public string DuMake { get; set; }
        public string DuSerialNo { get; set; }
        public string Product { get; set; }
        public string Mode { get; set; }

        public decimal? QuantityVariationWithMeasure { get; set; }
        public decimal? QuantityVariationInPercentage { get; set; }

        public decimal? ObservedDensity { get; set; }
        public decimal? ObservedTemp { get; set; }
        public decimal? ObservedDensity15Degree { get; set; }
        public decimal? ReferenceDensity15Degree { get; set; }

        public int? TankNumber { get; set; }
        public decimal? DensityVariation { get; set; }
    }

    public class ScoreSnapshotDto
    {
        public string ScoreInput { get; set; }
        public decimal? ScoreX { get; set; }
        public decimal? Weight { get; set; }
        public bool? IsRelaksasi { get; set; }
        public bool IsBandingNode { get; set; }
    }
}