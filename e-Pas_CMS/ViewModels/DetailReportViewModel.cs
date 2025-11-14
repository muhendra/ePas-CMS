using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;

namespace e_Pas_CMS.ViewModels
{
    public class DetailReportViewModel
    {
        public string AuditId { get; set; }
        public string ReportNo { get; set; }
        public string NamaAuditor { get; set; }

        public string NamaAuditor2 { get; set; }
        public DateTime? TanggalSubmit { get; set; }
        public DateTime? TanggalAudit { get; set; }
        public string Status { get; set; }
        public string SpbuNo { get; set; }
        public string Provinsi { get; set; }
        public string Kota { get; set; }
        public string Alamat { get; set; }
        public string Notes { get; set; }
        public string AuditType { get; set; }
        public List<AuditChecklistNode> Elements { get; set; }
        public List<MediaItem> MediaNotes { get; set; }
        public List<AuditQqCheckItem> QqChecks { get; set; }
        public List<MediaItem> FinalDocuments { get; set; }
        public decimal TotalScore { get; set; }
        public decimal MaxScore { get; set; }
        public decimal Score { get; set; }
        public decimal FinalScore { get; set; }

        public List<AuditLevelSummary> LevelSummaries { get; set; }

        public string KomentarStaf { get; set; }
        public string KomentarQuality { get; set; }
        public string KomentarHSSE { get; set; }
        public string KomentarVisual { get; set; }
        public string KomentarManager { get; set; }
        public string PenawaranKomperhensif { get; set; }
        public string Region { get; set; }
        public string OwnerName { get; set; }
        public string ManagerName { get; set; }
        public string OwnershipType { get; set; }
        public string Quarter { get; set; }
        public int Year { get; set; }
        public string MOR { get; set; }
        public string SalesArea { get; set; }
        public string SBM { get; set; }
        public string ClassSPBU { get; set; }
        public string Phone { get; set; }
        public decimal MinPassingScore { get; set; }    // nilai minimum Pasti Pas
        public decimal MinPassingScoreGood { get; set; }
        public string PenaltyAlerts { get; set; }
        public string PenaltyAlertsGood { get; set; }
        public string GoodStatus { get; set; }
        public string ExcellentStatus { get; set; }

        public string AuditCurrent { get; set; }

        public string AuditNext { get; set; }

        public string? ApproveBy { get; set; }

        public decimal? SSS { get; set; }
        public decimal? EQnQ { get; set; }
        public decimal? RFS { get; set; }
        public decimal? VFC { get; set; }
        public decimal? EPO { get; set; }

        public List<AuditLevelSummaryGroup> LevelSummaryGroups { get; set; }

        public List<FotoTemuan> FotoTemuan { get; set; } = new();

        public DateTime? CreatedDateBanding { get; set; }
    }

    public class FotoTemuan
    {
        public string Path { get; set; }
        public string Caption { get; set; }
    }


    public class AuditHeaderDto
    {
        public string Id { get; set; }
        public string ReportNo { get; set; }
        public string AuditType { get; set; }
        public string Auditlevel { get; set; }
        public DateTime? SubmitDate { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public string SpbuNo { get; set; }
        public string Region { get; set; }
        public string Kota { get; set; }
        public string Alamat { get; set; }
        public string OwnerName { get; set; }
        public string ManagerName { get; set; }
        public string OwnershipType { get; set; }
        public int? Quarter { get; set; }
        public int? Year { get; set; }
        public string Mor { get; set; }
        public string SalesArea { get; set; }
        public string Sbm { get; set; }
        public string ClassSpbu { get; set; }
        public string Phone { get; set; }
        public string KomentarStaf { get; set; }
        public string KomentarQuality { get; set; }
        public string KomentarHSSE { get; set; }
        public string KomentarVisual { get; set; }
        public string KomentarManager { get; set; }
        public string PenawaranKomperhensif { get; set; }
        public string AuditCurrent { get; set; }

        public string AuditNext { get; set; }

        public DateTime? ApproveDate { get; set; }

        public string? ApproveBy { get; set; }
        public DateTime? UpdatedDate { get; set; }

        public decimal score { get; set; }

        public string NamaAuditor { get; set; }

        public string NamaAuditor2 { get; set; }

    }


    public class AuditLevelSummaryGroup
    {
        public string GroupTitle { get; set; }
        public List<AuditLevelSummary> Items { get; set; }
    }

    public class AuditLevelSummary
    {
        public string Title { get; set; }
        public int Weight { get; set; }
        public decimal ActualScore { get; set; }
        public decimal MinimumScore { get; set; }
        public string Level { get; set; }
        public string Description { get; set; }  // optional subtitle
    }

public class MediaItemReport
    {
        public string MediaType { get; set; }
        public string MediaPath { get; set; }
    }

    public class AuditQqCheckItemReport
    {
        public string NozzleNumber { get; set; }
        public string DuMake { get; set; }
        public string DuSerialNo { get; set; }
        public string Product { get; set; }
        public string Mode { get; set; }
        public string QuantityVariationWithMeasure { get; set; }
        public string QuantityVariationInPercentage { get; set; }
        public string ObservedDensity { get; set; }
        public string ObservedTemp { get; set; }
        public string ObservedDensity15Degree { get; set; }
        public string ReferenceDensity15Degree { get; set; }
        public string TankNumber { get; set; }
        public string DensityVariation { get; set; }
    }

}
