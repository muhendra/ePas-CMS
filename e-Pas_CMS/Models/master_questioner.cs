using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class master_questioner
{
    public string id { get; set; } = null!;

    public string type { get; set; } = null!;

    public string category { get; set; } = null!;

    public int version { get; set; }

    public DateOnly effective_start_date { get; set; }

    public DateOnly effective_end_date { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual ICollection<master_questioner_detail> master_questioner_details { get; set; } = new List<master_questioner_detail>();

    public virtual ICollection<trx_audit> trx_auditmaster_questioner_checklists { get; set; } = new List<trx_audit>();

    public virtual ICollection<trx_audit> trx_auditmaster_questioner_intros { get; set; } = new List<trx_audit>();
}
