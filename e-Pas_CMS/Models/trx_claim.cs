namespace e_Pas_CMS.Models
{
    public partial class trx_claim
    {
        public string id { get; set; }

        public string trx_invoice_id { get; set; }
        public string? app_user_id { get; set; }

        public DateTime claim_date { get; set; }
        public DateTime? completed_date { get; set; }

        public int? claim_media_upload { get; set; }
        public int? claim_media_total { get; set; }

        public string status { get; set; }

        public string created_by { get; set; }
        public DateTime created_date { get; set; }

        public string updated_by { get; set; }
        public DateTime updated_date { get; set; }
    }
}
