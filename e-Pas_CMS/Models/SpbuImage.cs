using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class SpbuImage
{
    public string Id { get; set; } = null!;

    public string SpbuId { get; set; } = null!;

    public string Filepath { get; set; } = null!;

    public virtual spbu Spbu { get; set; } = null!;
}
