using e_Pas_CMS.Models;

namespace e_Pas_CMS.ViewModels
{
    public class InvoiceVM
    {
        public string Id { get; set; } = null!;

        public string InvoiceNo { get; set; } = null!;

        public DateTime? InvoiceDate { get; set; }

        public string? EmployeeId { get; set; }

        public DateTime? ExpectedDate { get; set; }

        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = null!;

        public string SurveyorName { get; set; } = "-";
    }

    public class InvoiceDetailVM
    {
        public string AuditId { get; set; } = null!;

        public string? ReportNo { get; set; }

        public decimal Amount { get; set; }

        public string Status { get; set; } = null!;
    }

    public class InvoiceDetailPageVM
    {
        public TrxInvoice Invoice { get; set; } = null!;

        public List<InvoiceDetailVM> Details { get; set; } = new List<InvoiceDetailVM>();
    }

    public class InvoiceDetailViewModel
    {
        // =========================
        // BASIC INFO
        // =========================
        public string Id { get; set; } = null!;

        public string InvoiceNo { get; set; } = null!;

        public string Status { get; set; } = null!;

        public DateTime ClaimDate { get; set; }

        // =========================
        // HEADER INFO
        // =========================
        public string EmployeeName { get; set; } = "-";

        public string Period { get; set; } = "-";

        public string Homebase { get; set; } = "-";

        public string Job { get; set; } = "Audit SPBU";

        // =========================
        // DETAIL ITEMS
        // =========================
        public List<InvoiceDetailItemVM> Items { get; set; } = new List<InvoiceDetailItemVM>();

        // =========================
        // TOTALS
        // ClaimExpenseAmount = SUM(trx_claim_detail.amount)
        // TotalAuditFee      = SUM(trx_invoice_detail.audit_fee)
        // TotalLumpsum       = SUM(trx_invoice_detail.lumpsum_fee)
        // TotalExpense       = ClaimExpenseAmount + TotalAuditFee + TotalLumpsum
        // =========================
        public decimal ClaimExpenseAmount { get; set; }

        public decimal TotalExpense { get; set; }

        public decimal TotalAuditFee { get; set; }

        public decimal TotalLumpsum { get; set; }

        // =========================
        // SUMMARY EXTRA
        // =========================
        public decimal LessDirectCharges { get; set; }

        public decimal LessAdvances { get; set; }

        public decimal AmountDue =>
            TotalExpense - LessDirectCharges - LessAdvances;

        // =========================
        // ATTACHMENTS
        // =========================
        public List<trx_claim_media> Attachments { get; set; } = new List<trx_claim_media>();

        public Dictionary<string, trx_claim_media> AttachmentGroups { get; set; } = new Dictionary<string, trx_claim_media>();

        // =========================
        // BANK INFO
        // =========================
        public string BankName { get; set; } = "";

        public string BankAccount { get; set; } = "";

        public string BankOwner { get; set; } = "";

        // =========================
        // APPROVAL FINANCE
        // =========================
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovedDate { get; set; }

        public string? RejectionReason { get; set; }

        // Backward compatibility kalau view lama masih pakai RejectReason
        public string? RejectReason
        {
            get => RejectionReason;
            set => RejectionReason = value;
        }

        // =========================
        // HELPER DISPLAY
        // =========================
        public string StatusLabel =>
            Status switch
            {
                "NOT_CLAIMED" => "Menunggu Aksi",
                "IN_PROGRESS" => "Sedang Diproses",
                "COMPLETED" => "Selesai",
                "REJECTED" => "Ditolak",

                "IN_PROGRESS_SUBMIT" => "Submit Diproses",
                "UNDER_REVIEW" => "Sedang Direview",
                "PENDING_APPROVAL" => "Menunggu Persetujuan",
                "APPROVED" => "Disetujui",

                "PROCESS" => "Sedang Diproses",
                "REVIEW" => "Menunggu Persetujuan",

                _ => Status ?? "-"
            };
    }

    public class InvoiceDetailItemVM
    {
        public string TrxInvoiceDetailId { get; set; } = null!;

        public string TrxAuditId { get; set; } = null!;

        public DateTime Date { get; set; }

        public string? Description { get; set; }

        // ClaimAmount hanya diisi di row pertama untuk display claim expense
        public decimal ClaimAmount { get; set; }

        // Amount = line total display.
        // Row pertama: claim amount + audit fee + lumpsum.
        // Row lain: audit fee + lumpsum.
        public decimal Amount { get; set; }

        public decimal AuditFee { get; set; }

        public decimal Lumpsum { get; set; }
    }

    public class InvoiceApprovalPostVM
    {
        public string Id { get; set; } = null!;

        public string? RejectionReason { get; set; }

        public List<InvoiceApprovalDetailPostVM> Items { get; set; } = new List<InvoiceApprovalDetailPostVM>();
    }

    public class InvoiceApprovalDetailPostVM
    {
        public string TrxInvoiceDetailId { get; set; } = null!;

        public decimal AuditFee { get; set; }

        public decimal LumpsumFee { get; set; }
    }
}