using System;

namespace e_Pas_CMS.Models
{
    public partial class SysParameter
    {
        public string Id { get; set; } = null!;

        public string Code { get; set; } = null!;

        public string Value { get; set; } = null!;

        public string Status { get; set; } = null!;

        public string CreatedBy { get; set; } = null!;

        public DateTime CreatedDate { get; set; }

        public string UpdatedBy { get; set; } = null!;

        public DateTime? UpdatedDate { get; set; }
    }
}
