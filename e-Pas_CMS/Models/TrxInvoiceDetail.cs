using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxInvoiceDetail
{
    public string Id { get; set; } = null!;

    public string TrxInvoiceId { get; set; } = null!;

    public string TrxAuditId { get; set; } = null!;

    public decimal AuditFee { get; set; }

    public decimal? LumpsumFee { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime UpdatedDate { get; set; }

    public virtual TrxInvoice Invoice { get; set; } = null!;

    public virtual ICollection<TrxInvoiceApprovalDetail> TrxInvoiceApprovalDetails { get; set; } = new List<TrxInvoiceApprovalDetail>();
}