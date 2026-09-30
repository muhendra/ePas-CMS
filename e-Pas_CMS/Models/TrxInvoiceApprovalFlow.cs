using System;

namespace e_Pas_CMS.Models;

public partial class TrxInvoiceApprovalFlow
{
    public string Id { get; set; } = null!;

    public string TrxInvoiceId { get; set; } = null!;

    public int ApprovalLevel { get; set; }

    public string ApproverUserId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? ActionBy { get; set; }

    public DateTime? ActionDate { get; set; }

    public string? RejectionReason { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime UpdatedDate { get; set; }
}
