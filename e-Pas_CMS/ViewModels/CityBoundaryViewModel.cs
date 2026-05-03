using System;
using System.Collections.Generic;

namespace e_Pas_CMS.ViewModels
{
    public class CityBoundaryListVm
    {
        public string Id { get; set; }
        public string AppUserId { get; set; }
        public string AuditorName { get; set; }
        public string AuditorUsername { get; set; }
        public DateTime? LastAuditDate { get; set; }
        public string BoundaryCity1 { get; set; }
        public string BoundaryCity2 { get; set; }
        public string BoundaryCity3 { get; set; }
        public string BoundaryCity4 { get; set; }
        public string Status { get; set; }
    }

    public class CityBoundaryDetailVm
    {
        public string Id { get; set; }
        public string AppUserId { get; set; }
        public string AuditorName { get; set; }
        public string AuditorUsername { get; set; }
        public string AuditorPhone { get; set; }
        public string AuditorEmail { get; set; }
        public string AuditorStatus { get; set; }
        public DateTime? LastAuditDate { get; set; }

        public string BoundaryCity1 { get; set; }
        public string BoundaryCity2 { get; set; }
        public string BoundaryCity3 { get; set; }
        public string BoundaryCity4 { get; set; }

        public string Status { get; set; }
        public string Notes { get; set; }
        public string ApprovalNotes { get; set; }

        public List<CityBoundaryAuditHistoryVm> AuditHistories { get; set; } = new();
    }

    public class CityBoundaryAuditHistoryVm
    {
        public string TrxAuditId { get; set; }
        public string ReportNo { get; set; }
        public DateTime? AuditDate { get; set; }
        public string AuditType { get; set; }
        public string AuditLevel { get; set; }
        public string SpbuNo { get; set; }
        public string SpbuAddress { get; set; }
        public decimal Amount { get; set; }
    }

    public class CityBoundaryApprovalVm
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string ApprovalNotes { get; set; }
    }

    public class CityBoundaryCreateVm
    {
        public string AppUserId { get; set; }
        public string BoundaryCity1 { get; set; }
        public string BoundaryCity2 { get; set; }
        public string BoundaryCity3 { get; set; }
        public string BoundaryCity4 { get; set; }
        public string Notes { get; set; }
    }
}