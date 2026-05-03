using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models
{
    public partial class TrxInvoice
    {
        public string Id { get; set; }

        public string? AppUserId { get; set; }
        public string? InvoicePrefix { get; set; }
        public string InvoiceNo { get; set; }

        public DateTime InvoicePeriodStart { get; set; }
        public DateTime InvoicePeriodEnd { get; set; }

        public DateTime? IssuedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? CompletedDate { get; set; }

        public string Status { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }

        public string UpdatedBy { get; set; }
        public DateTime UpdatedDate { get; set; }

        // Navigation Property
        public virtual ICollection<TrxInvoiceDetail> Details { get; set; } = new List<TrxInvoiceDetail>();
    }
}