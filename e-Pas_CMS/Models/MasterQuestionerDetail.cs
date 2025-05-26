using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class MasterQuestionerDetail
{
    public string Id { get; set; } = null!;

    public string MasterQuestionerId { get; set; } = null!;

    public string? ParentId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? ScoreOption { get; set; }

    public decimal? Weight { get; set; }

    public int OrderNo { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public bool? IsPenalty { get; set; }

    public string? PenaltyAlert { get; set; }

    public bool is_relaksasi { get; set; }

    public string? number { get; set; }
    public virtual ICollection<MasterQuestionerDetail> InverseParent { get; set; } = new List<MasterQuestionerDetail>();

    public virtual MasterQuestioner MasterQuestioner { get; set; } = null!;

    public virtual MasterQuestionerDetail? Parent { get; set; }

    public virtual ICollection<TrxAuditChecklist> TrxAuditChecklists { get; set; } = new List<TrxAuditChecklist>();

    public virtual ICollection<TrxAuditMedium> TrxAuditMedia { get; set; } = new List<TrxAuditMedium>();
}
