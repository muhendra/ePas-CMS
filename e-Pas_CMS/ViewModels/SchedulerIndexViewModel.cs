namespace e_Pas_CMS.ViewModels
{
    public class SchedulerIndexViewModel
    {
        public List<SchedulerItemViewModel> Items { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int PageSize { get; set; }
        public string SearchTerm { get; set; }
    }

    public class SchedulerItemViewModel
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string AppUserName { get; set; }
        public DateTime AuditScheduleDate { get; set; }
        public string AuditType { get; set; }
        public string AuditLevel { get; set; }
        public string SpbuNo { get; set; }
    }

}
