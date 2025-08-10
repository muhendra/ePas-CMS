using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxAudit
{
    public string Id { get; set; } = null!;

    public string? ReportPrefix { get; set; }

    public string? ReportNo { get; set; }

    public string SpbuId { get; set; } = null!;

    public string? AppUserId { get; set; }

    public string? MasterQuestionerIntroId { get; set; }

    public string? MasterQuestionerChecklistId { get; set; }

    public string AuditLevel { get; set; } = null!;

    public string AuditType { get; set; } = null!;

    public DateOnly? AuditScheduleDate { get; set; }

    public DateTime? AuditExecutionTime { get; set; }

    public int? AuditMediaUpload { get; set; }

    public int? AuditMediaTotal { get; set; }

    public string? AuditMomIntro { get; set; }

    public string? AuditMomFinal { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }
    public DateTime? approval_date { get; set; }
    public string? approval_by { get; set; }

    public virtual AppUser? AppUser { get; set; }

    public virtual MasterQuestioner? MasterQuestionerChecklist { get; set; }

    public virtual MasterQuestioner? MasterQuestionerIntro { get; set; }

    public virtual spbu Spbu { get; set; } = null!;

    public virtual ICollection<TrxAuditChecklist> TrxAuditChecklists { get; set; } = new List<TrxAuditChecklist>();

    public virtual ICollection<TrxAuditMedium> TrxAuditMedia { get; set; } = new List<TrxAuditMedium>();

    public virtual ICollection<TrxAuditQq> TrxAuditQqs { get; set; } = new List<TrxAuditQq>();

    public virtual ICollection<TrxFeedback> TrxFeedbacks { get; set; } = new List<TrxFeedback>();
}
