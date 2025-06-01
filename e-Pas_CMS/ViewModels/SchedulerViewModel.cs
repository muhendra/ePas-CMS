using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace e_Pas_CMS.ViewModels
{
    public class SchedulerViewModel
    {
        [Required]
        [Display(Name = "Tanggal Audit")]
        public DateTime? TanggalAudit { get; set; }

        [Required]
        [Display(Name = "Auditor")]
        public string Auditor { get; set; }

        [Required]
        [Display(Name = "Tipe Audit")]
        public string TipeAudit { get; set; }

        [Required]
        [Display(Name = "SPBU yang dipilih")]
        public List<string> SpbuList { get; set; }
    }
}
