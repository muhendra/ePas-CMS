using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class app_user_role
{
    public string id { get; set; } = null!;

    public string app_user_id { get; set; } = null!;

    public string app_role_id { get; set; } = null!;

    public string? spbu_id { get; set; }

    public string? region { get; set; }
    public string? sbm { get; set; }

    public virtual app_role app_role { get; set; } = null!;

    public virtual app_user app_user { get; set; } = null!;

    public virtual spbu? spbu { get; set; }
}
