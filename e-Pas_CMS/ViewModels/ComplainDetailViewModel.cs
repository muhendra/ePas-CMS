namespace e_Pas_CMS.ViewModels
{
    public class ComplainDetailViewModel
    {
        public string Id { get; set; }
        public bool IsBanding { get; set; }
        public string StatusCode { get; set; }   // e.g. MENUNGGU/APPROVED/REJECTED
        public string StatusText { get; set; }

        // SPBU
        public string NoSpbu { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string Address { get; set; }

        // Audit
        public string ReportNo { get; set; }
        public DateTime? TanggalAudit { get; set; }
        public DateTime? SentDate { get; set; }
        public string Verifikator { get; set; }
        public string Auditor1 { get; set; }
        public string Auditor2 { get; set; }
        public string TipeAudit { get; set; }
        public string NextAudit { get; set; }
        public string Koordinator { get; set; }

        // Tambahan untuk galeri Berita Acara (FINAL)
        public string AuditId { get; set; }  // <-- tambahkan
        public List<MediaItem> FinalDocuments { get; set; } = new(); // <-- tambahkan

        // Complain/Banding
        public string TicketNo { get; set; }
        public string NomorBanding { get; set; }
        public DateTime? CreatedDate { get; set; }

        public string BodyText { get; set; }

        public List<PointItem> Points { get; set; } = new();
        public List<AttachmentItem> Attachments { get; set; } = new();

        // Permission flags
        public bool CanApprove { get; set; }
        public bool CanReject { get; set; }

        public string feedback_type { get; set; }
        public string? Klarifikasi { get; set; }
        public string Description { get; set; }
        public string SebelumRevisi { get; set; }
        public string SesudahRevisi { get; set; }
        public List<KlfAttachmentItem> MediaKlarifikasi { get; set; } = new();


        // riwayat klarifikasi
        public List<KlarifikasiLogItem> KlarifikasiHistory { get; set; } = new();
    }

    public class UpdateKlarifikasiRequest
    {
        public string BandingId { get; set; }
        public string Text { get; set; }
    }

    public class PointItem
    {
        public string Element { get; set; }
        public string SubElement { get; set; }
        public string DetailElement { get; set; }
        public string DetailDibantah { get; set; }

        // Tambahan untuk Detail
        public string Description { get; set; }
        public List<AttachmentItem> Attachments { get; set; } = new();

        public string PointId { get; set; } // ← wajib, untuk form approve/reject

        public List<string> mediaElement { get; set; } = new();

        public List<PointApprovalHistory> History { get; set; } = new(); // ← riwayat approval
    }

    public class PointApprovalHistory
    {
        public string Status { get; set; }         // APPROVED / REJECTED
        public string ApprovedBy { get; set; }     // nama user
        public DateTime ApprovedDate { get; set; } // timestamp
        public string StatusCode { get; set; }
    }


    public class mediaElement
    {
        public string FileName { get; set; }
    }

    public class AttachmentItem
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string SizeReadable { get; set; } // opsional

        // Opsional (kalau ingin preview juga untuk attachment per-poin)
        public string Url { get; set; }       // absolute/relative file url
        public string MediaType { get; set; } // "jpg","png","pdf","mp4", dll
    }

    public class KlfAttachmentItem
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string SizeReadable { get; set; } // opsional

        // Opsional (kalau ingin preview juga untuk attachment per-poin)
        public string Url { get; set; }       // absolute/relative file url
        public string MediaType { get; set; } // "jpg","png","pdf","mp4", dll
    }

    public class PointRow
    {
        public string point_id { get; set; } = default!;
        public string description { get; set; } = default!;
        public string element_label { get; set; } = default!;
        public string sub_element_label { get; set; } = default!;
        public string detail_element_label { get; set; } = default!;
        public string compared_elements { get; set; } = default!;
        public string media_elements { get; set; } = default!;
    }

    public class KlarifikasiLogItem
    {
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = "-";
        public string Text { get; set; } = "";
    }

    public class MediaRow
    {
        public string id { get; set; } = default!;
        public string media_type { get; set; } = default!;
        public string media_path { get; set; } = default!;

        public string trx_feedback_point_id { get; set; }
    }

}
