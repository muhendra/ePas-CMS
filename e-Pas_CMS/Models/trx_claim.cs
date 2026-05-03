using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class trx_claim
{
    public string id { get; set; } = null!;

    public string trx_invoice_id { get; set; } = null!;

    public string? app_user_id { get; set; }

    public DateTime claim_date { get; set; }

    public DateTime? completed_date { get; set; }

    public int? claim_media_upload { get; set; }

    public int? claim_media_total { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime updated_date { get; set; }

    public virtual TrxInvoice TrxInvoice { get; set; } = null!;

    public virtual ICollection<trx_claim_detail> TrxClaimDetails { get; set; } = new List<trx_claim_detail>();

    public virtual ICollection<trx_claim_media> TrxClaimMedia { get; set; } = new List<trx_claim_media>();

    public virtual ICollection<TrxInvoiceApproval> TrxInvoiceApprovals { get; set; } = new List<TrxInvoiceApproval>();
}