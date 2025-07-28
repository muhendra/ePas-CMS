using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace e_Pas_CMS.Models
{
    [Keyless]
    [Table("vw_audit_report_final")]
    public class VwAuditReportFinal
    {
        public DateTime? SendDate { get; set; }
        public DateTime? AuditDate { get; set; }
        public string SpbuNo { get; set; }
        public string Region { get; set; }
        public int? Year { get; set; }
        public string Address { get; set; }
        public string City_Name { get; set; }
        public string Tipe_Spbu { get; set; }
        public string Rayon { get; set; }
        public string Audit_Level { get; set; }
        public string Audit_Next { get; set; }
        public string Good_Status { get; set; }
        public string Excellent_Status { get; set; }
        public decimal? Total_Score { get; set; }
        public decimal? Sss { get; set; }
        public decimal? Eqnq { get; set; }
        public decimal? Rfs { get; set; }
        public decimal? Vfc { get; set; }
        public decimal? Epo { get; set; }
        public decimal? Wtms { get; set; }
        public decimal? Qq { get; set; }
        public decimal? Wmef { get; set; }
        public decimal? Format_Fisik { get; set; }
        public decimal? Cpo { get; set; }
        public string Kelas_Spbu { get; set; }
        public string Penalty_Good_Alerts { get; set; }
        public string Penalty_Excellent_Alerts { get; set; }
    }

}
