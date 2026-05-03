using Microsoft.AspNetCore.Mvc.Rendering;

namespace e_Pas_CMS.ViewModels
{
    public class RoleManagementItemViewModel
    {
        public string Id { get; set; }
        public string NamaRole { get; set; }
        public string App { get; set; }

        public string MenuFunction { get; set; }

        public List<string> MenuFunctionLabels { get; set; } = new List<string>();

        public int TotalPermission { get; set; }

        public string Status { get; set; }

        public bool IsActive =>
            string.Equals(Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    public class RoleManagementIndexViewModel
    {
        public List<RoleManagementItemViewModel> Items { get; set; } = new List<RoleManagementItemViewModel>();

        public string SearchTerm { get; set; } = "";

        public int CurrentPage { get; set; }

        public int PageSize { get; set; } = 10;

        public int TotalRecords { get; set; }

        public int TotalPages { get; set; }
    }

    public class RoleManagementEditViewModel
    {
        public string Id { get; set; }

        public string NamaRole { get; set; }

        public string App { get; set; }

        public string Status { get; set; }

        public string MenuFunction { get; set; }

        public List<string> SelectedMenuFunctions { get; set; } = new List<string>();

        public List<SelectListItem> MenuFunctionList { get; set; } = new List<SelectListItem>();
    }
}