using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxInvoice
{
    public string Id { get; set; } = null!;

    public string? AppUserId { get; set; }

    public string? InvoicePrefix { get; set; }

    public string InvoiceNo { get; set; } = null!;

    public DateTime InvoicePeriodStart { get; set; }

    public DateTime InvoicePeriodEnd { get; set; }

    public DateTime? IssuedDate { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? CompletedDate { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime UpdatedDate { get; set; }

    public virtual ICollection<TrxInvoiceDetail> Details { get; set; } = new List<TrxInvoiceDetail>();

    public virtual ICollection<trx_claim> TrxClaims { get; set; } = new List<trx_claim>();

    public virtual ICollection<TrxInvoiceApproval> TrxInvoiceApprovals { get; set; } = new List<TrxInvoiceApproval>();
}