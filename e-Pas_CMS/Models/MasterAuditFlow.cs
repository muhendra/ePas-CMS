using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class MasterAuditFlow
{
    public string Id { get; set; } = null!;

    public string AuditLevel { get; set; } = null!;

    public string? PassedAuditLevel { get; set; }

    public string? FailedAuditLevel { get; set; }

    public string? PassedExcellent { get; set; }

    public string? PassedGood { get; set; }

    public int? RangeAuditMonth { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }
}
