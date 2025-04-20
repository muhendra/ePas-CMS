using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class master_audit_flow
{
    public string id { get; set; } = null!;

    public string audit_level { get; set; } = null!;

    public string passed_audit_level { get; set; } = null!;

    public string failed_audit_level { get; set; } = null!;

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }
}
