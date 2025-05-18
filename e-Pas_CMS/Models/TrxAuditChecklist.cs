using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxAuditChecklist
{
    public string Id { get; set; } = null!;

    public string TrxAuditId { get; set; } = null!;

    public string MasterQuestionerDetailId { get; set; } = null!;

    public string? ScoreInput { get; set; }

    public decimal? ScoreAf { get; set; }

    public decimal? ScoreX { get; set; }

    public string? Comment { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual MasterQuestionerDetail MasterQuestionerDetail { get; set; } = null!;

    public virtual TrxAudit TrxAudit { get; set; } = null!;
}
