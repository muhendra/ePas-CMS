using System;
using System.Collections.Generic;

namespace e_Pas_CMS.ViewModels
{
    public class RoleAuditorIndexViewModel
    {
        public List<RoleAuditorViewModel> Items { get; set; } = new List<RoleAuditorViewModel>();
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public string SearchTerm { get; set; } = "";
    }

}
