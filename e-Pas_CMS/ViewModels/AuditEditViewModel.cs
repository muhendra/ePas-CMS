using e_Pas_CMS.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace e_Pas_CMS.ViewModels
{
    public class AuditEditViewModel
    {
        public string Id { get; set; }
        public string SpbuId { get; set; }
        public string AppUserId { get; set; }
        public string AuditLevel { get; set; }
        public string AuditType { get; set; }
        public DateOnly? AuditScheduleDate { get; set; }
        public string AuditMomIntro { get; set; }
        public string AuditMomFinal { get; set; }
        public string Status { get; set; }

        public IEnumerable<SelectListItem> SpbuList { get; set; }
        public IEnumerable<SelectListItem> UserList { get; set; }
        public IEnumerable<SelectListItem> StatusList { get; set; }
    }



}
