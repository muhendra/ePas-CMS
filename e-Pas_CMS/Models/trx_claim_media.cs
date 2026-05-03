using System;

namespace e_Pas_CMS.Models;

public partial class trx_claim_media
{
    public string id { get; set; } = null!;

    public string trx_claim_id { get; set; } = null!;

    public string claim_item_type { get; set; } = null!;

    public string media_type { get; set; } = null!;

    public string media_path { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public virtual trx_claim TrxClaim { get; set; } = null!;
}