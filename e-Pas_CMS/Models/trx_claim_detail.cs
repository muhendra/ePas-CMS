using System;

namespace e_Pas_CMS.Models;

public partial class trx_claim_detail
{
    public string id { get; set; } = null!;

    public string trx_claim_id { get; set; } = null!;

    public string claim_item_type { get; set; } = null!;

    public string? description { get; set; }

    public decimal amount { get; set; }

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime updated_date { get; set; }

    public virtual trx_claim TrxClaim { get; set; } = null!;
}