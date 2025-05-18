using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class AppRole
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string App { get; set; } = null!;

    public string? MenuFunction { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual ICollection<AppUserRole> AppUserRoles { get; set; } = new List<AppUserRole>();
}
