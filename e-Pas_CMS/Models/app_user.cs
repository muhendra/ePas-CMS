using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class app_user
{
    public string id { get; set; } = null!;

    public string username { get; set; } = null!;

    public string password_hash { get; set; } = null!;

    public string name { get; set; } = null!;

    public string? phone_number { get; set; }

    public string? email { get; set; }

    public string? notification_token { get; set; }

    public DateTime? last_change_passwd_dt { get; set; }

    public DateTime? last_login_dt { get; set; }

    public string? suffix_refresh_token { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual ICollection<app_user_role> app_user_roles { get; set; } = new List<app_user_role>();

    public virtual ICollection<trx_audit> trx_audits { get; set; } = new List<trx_audit>();
}
