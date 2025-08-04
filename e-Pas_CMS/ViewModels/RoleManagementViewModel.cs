using Microsoft.AspNetCore.Mvc.Rendering;

namespace e_Pas_CMS.ViewModels
{
    public class RoleManagementItemViewModel
    {
        public string Id { get; set; }
        public string NamaRole { get; set; }
        public string App { get; set; }
        public string MenuFunction { get; set; }
        public string Status { get; set; }
        public bool IsActive => Status == "ACTIVE";
    }

    public class RoleManagementIndexViewModel
    {
        public List<RoleManagementItemViewModel> Items { get; set; } = new List<RoleManagementItemViewModel>();
        public string SearchTerm { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }

    public class RoleManagementEditViewModel
    {
        public string Id { get; set; }
        public string NamaRole { get; set; }
        public string App { get; set; }
        public string Status { get; set; }

        public string MenuFunction { get; set; } // Combined Value (separated by #)
        public List<string> SelectedMenuFunctions { get; set; } = new List<string>();
        public List<SelectListItem> MenuFunctionList { get; set; } = new List<SelectListItem>();
    }


}
