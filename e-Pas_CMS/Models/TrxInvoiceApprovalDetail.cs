using System;

namespace e_Pas_CMS.Models;

public partial class TrxInvoiceApprovalDetail
{
    public string Id { get; set; } = null!;

    public string TrxInvoiceApprovalId { get; set; } = null!;

    public string TrxInvoiceDetailId { get; set; } = null!;

    public string TrxAuditId { get; set; } = null!;

    public decimal AuditFee { get; set; }

    public decimal LumpsumFee { get; set; }

    public decimal LineTotal { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public virtual TrxInvoiceApproval TrxInvoiceApproval { get; set; } = null!;

    public virtual TrxInvoiceDetail TrxInvoiceDetail { get; set; } = null!;
}