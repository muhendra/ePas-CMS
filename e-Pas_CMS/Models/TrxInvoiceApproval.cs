using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxInvoiceApproval
{
    public string Id { get; set; } = null!;

    public string TrxInvoiceId { get; set; } = null!;

    public string TrxClaimId { get; set; } = null!;

    public string ApprovalAction { get; set; } = null!;

    public decimal ClaimExpenseAmount { get; set; }

    public decimal TotalAuditFee { get; set; }

    public decimal TotalLumpsumFee { get; set; }

    public decimal TotalExpense { get; set; }

    public string? RejectionReason { get; set; }

    public string ApprovedBy { get; set; } = null!;

    public DateTime ApprovedDate { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public virtual trx_claim TrxClaim { get; set; } = null!;

    public virtual TrxInvoice TrxInvoice { get; set; } = null!;

    public virtual ICollection<TrxInvoiceApprovalDetail> TrxInvoiceApprovalDetails { get; set; } = new List<TrxInvoiceApprovalDetail>();
}