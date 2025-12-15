using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class trx_audit_not_started_log
{
    public string id { get; set; } = null!;

    public string trx_audit_id { get; set; } = null!;

    public string? spbu_id { get; set; }

    public string? old_status { get; set; }

    public string new_status { get; set; } = null!;

    public string? old_form_status_auditor1 { get; set; }

    public string? new_form_status_auditor1 { get; set; }

    public string changed_by { get; set; } = null!;

    public DateTime changed_date { get; set; }

    public string? note { get; set; }

    /* =========================
     * Navigation Properties
     * ========================= */

    public virtual trx_audit trx_audit { get; set; } = null!;

    public virtual spbu? spbu { get; set; }
}
