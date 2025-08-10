using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxFeedback
{
    public string Id { get; set; } = null!;

    public string TicketNo { get; set; } = null!;

    public string AppUserId { get; set; } = null!;

    public string TrxAuditId { get; set; } = null!;

    public string FeedbackType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public string? ApprovalBy { get; set; }

    public DateTime? ApprovalDate { get; set; }

    public virtual app_user AppUser { get; set; } = null!;

    public virtual trx_audit TrxAudit { get; set; } = null!;

    public virtual ICollection<TrxFeedbackPoint> TrxFeedbackPoints { get; set; } = new List<TrxFeedbackPoint>();
}
