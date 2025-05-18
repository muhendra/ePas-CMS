using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxAuditMedium
{
    public string Id { get; set; } = null!;

    public string TrxAuditId { get; set; } = null!;

    public string? Type { get; set; }

    public string? MasterQuestionerDetailId { get; set; }

    public string MediaType { get; set; } = null!;

    public string MediaPath { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual MasterQuestionerDetail? MasterQuestionerDetail { get; set; }

    public virtual TrxAudit TrxAudit { get; set; } = null!;
}
