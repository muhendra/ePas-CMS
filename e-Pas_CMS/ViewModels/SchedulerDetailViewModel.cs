namespace e_Pas_CMS.ViewModels
{
    public class SchedulerDetailViewModel
    {
        public string Id { get; set; }
        public string SpbuNo { get; set; }
        public string SpbuAddress { get; set; }
        public string AppUserName { get; set; }
        public DateTime? AuditScheduleDate { get; set; }
        public string AuditType { get; set; }
        public string AuditLevel { get; set; }
        public string Status { get; set; }
        public string AuditMomIntro { get; set; }
        public string AuditMomFinal { get; set; }
    }

}
