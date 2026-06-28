using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class master_closing_date
{
    public string id { get; set; } = null!;

    public int closing_day { get; set; }

    public string? description { get; set; }

    public bool is_active { get; set; }

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual ICollection<trx_audit> trx_audits { get; set; } = new List<trx_audit>();
}