using e_Pas_CMS.Models;

namespace e_Pas_CMS.ViewModels
{
    public class InvoiceVM
    {
        public string Id { get; set; }
        public string InvoiceNo { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public string EmployeeId { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public string SurveyorName { get; set; }
    }

    public class InvoiceDetailVM
    {
        public string AuditId { get; set; }
        public string ReportNo { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
    }

    public class InvoiceDetailPageVM
    {
        public TrxInvoice Invoice { get; set; }
        public List<InvoiceDetailVM> Details { get; set; }
    }

    public class InvoiceDetailViewModel
    {
        // =========================
        // BASIC INFO
        // =========================
        public string Id { get; set; } // 🔥 penting buat approve/reject
        public string InvoiceNo { get; set; }
        public string Status { get; set; }

        public DateTime ClaimDate { get; set; }

        // =========================
        // HEADER INFO (NEW)
        // =========================
        public string EmployeeName { get; set; }
        public string Period { get; set; }
        public string Homebase { get; set; }
        public string Job { get; set; } = "Audit SPBU";

        // =========================
        // DETAIL ITEMS
        // =========================
        public List<InvoiceDetailItemVM> Items { get; set; } = new();

        // =========================
        // TOTALS
        // =========================
        public decimal TotalExpense { get; set; }
        public decimal TotalAuditFee { get; set; }
        public decimal TotalLumpsum { get; set; }

        // =========================
        // SUMMARY EXTRA (OPTIONAL FUTURE)
        // =========================
        public decimal LessDirectCharges { get; set; } = 0;
        public decimal LessAdvances { get; set; } = 0;

        public decimal AmountDue =>
            TotalExpense - LessDirectCharges - LessAdvances;

        // =========================
        // ATTACHMENTS
        // =========================
        public List<trx_claim_media> Attachments { get; set; } = new();

        // =========================
        // BANK INFO (NEW)
        // =========================
        public string BankName { get; set; }
        public string BankAccount { get; set; }
        public string BankOwner { get; set; }

        // =========================
        // APPROVAL (NEW)
        // =========================
        public string ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }

        public string RejectReason { get; set; }

        // =========================
        // HELPER DISPLAY
        // =========================
        public string StatusLabel =>
            Status switch
            {
                "NOT_CLAIMED" => "Menunggu Aksi",
                "PROCESS" => "Sedang Diproses",
                "REVIEW" => "Menunggu Persetujuan",
                "APPROVED" => "Disetujui",
                "REJECTED" => "Ditolak",
                _ => Status
            };

        public Dictionary<string, trx_claim_media> AttachmentGroups { get; set; } = new();
    }

    public class InvoiceDetailItemVM
    {
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public decimal AuditFee { get; set; }
        public decimal Lumpsum { get; set; }
    }
}
