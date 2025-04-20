using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class trx_audit_medium
{
    public string id { get; set; } = null!;

    public string trx_audit_id { get; set; } = null!;

    public string? type { get; set; }

    public string? master_questioner_detail_id { get; set; }

    public string media_type { get; set; } = null!;

    public string media_path { get; set; } = null!;

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual master_questioner_detail? master_questioner_detail { get; set; }

    public virtual trx_audit trx_audit { get; set; } = null!;
}
