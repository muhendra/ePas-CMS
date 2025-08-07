using Microsoft.AspNetCore.Mvc.Rendering;

namespace e_Pas_CMS.ViewModels
{
    public class RoleAuditorViewModel
    {
        public string Id { get; set; }
        public string NamaRole { get; set; }
        public string Auditor { get; set; }
        public string Username { get; set; }
        public string email { get; set; }
        public string Region { get; set; }
        public List<string> SpbuList { get; set; } = new List<string>();
        public string Status { get; set; }
        public bool IsActive => Status == "ACTIVE";
    }

    public class RoleAuditorAddViewModel
    {
        // Input User Baru
        public string Name { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Handphone { get; set; }
        public string Email { get; set; }

        // Role & Region Selections (comma-separated IDs)
        public string SelectedRoleIds { get; set; }
        public string SelectedRegionIds { get; set; }
        public string SelectedSbmIds { get; set; }

        // Dropdown Data
        public List<SelectListItem> RoleList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> RegionList { get; set; } = new List<SelectListItem>();

        public List<SelectListItem> SbmList { get; set; } = new List<SelectListItem>();
    }

    public class RoleAuditorEditViewModel
    {
        public string AuditorId { get; set; }
        public string UserName { get; set; }
        public string AuditorName { get; set; }
        public string Handphone { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public List<string> SelectedSbmIds { get; set; }
        public List<string> SelectedSbmNames { get; set; }
        public List<SelectListItem> SbmList { get; set; }

        public List<string> SelectedRoleIds { get; set; } = new List<string>();
        public List<string> SelectedRoleNames { get; set; } = new List<string>();

        public List<string> SelectedRegionIds { get; set; } = new List<string>();
        public List<string> SelectedRegionNames { get; set; } = new List<string>();

        public List<SelectListItem> RoleList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> RegionList { get; set; } = new List<SelectListItem>();
    }

    public class RoleAuditorDetailViewModel
    {
        public string AuditorId { get; set; }
        public string AuditorName { get; set; }
        public string AuditorStatus { get; set; }
        public List<string> RoleNames { get; set; }
        public List<string> RegionNames { get; set; }
        public List<string> SpbuList { get; set; }
    }

}
