using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class app_role
{
    public string id { get; set; } = null!;

    public string name { get; set; } = null!;

    public string app { get; set; } = null!;

    public string? menu_function { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual ICollection<app_user_role> app_user_roles { get; set; } = new List<app_user_role>();
}
