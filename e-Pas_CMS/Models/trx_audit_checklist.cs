using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class trx_audit_checklist
{
    public string id { get; set; } = null!;

    public string trx_audit_id { get; set; } = null!;

    public string master_questioner_detail_id { get; set; } = null!;

    public string? score_input { get; set; }

    public decimal? score_af { get; set; }

    public decimal? score_x { get; set; }

    public string? comment { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual master_questioner_detail master_questioner_detail { get; set; } = null!;

    public virtual trx_audit trx_audit { get; set; } = null!;
}
