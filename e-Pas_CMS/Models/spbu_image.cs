using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class spbu_image
{
    public string id { get; set; } = null!;

    public string spbu_id { get; set; } = null!;

    public string filepath { get; set; } = null!;

    public virtual spbu spbu { get; set; } = null!;
}
