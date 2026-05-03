using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models
{
    public partial class TrxInvoiceDetail
    {
        public string Id { get; set; }

        public string TrxInvoiceId { get; set; }
        public string TrxAuditId { get; set; }

        public decimal Amount { get; set; }
        public string Status { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }

        public string UpdatedBy { get; set; }
        public DateTime UpdatedDate { get; set; }

        // Navigation Property
        public virtual TrxInvoice Invoice { get; set; }
    }
}