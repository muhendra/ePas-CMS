namespace e_Pas_CMS.Models
{
    public partial class trx_claim_detail
    {
        public string id { get; set; }

        public string trx_claim_id { get; set; }

        public string claim_item_type { get; set; }
        public string? description { get; set; }

        public decimal amount { get; set; }

        public string created_by { get; set; }
        public DateTime created_date { get; set; }

        public string updated_by { get; set; }
        public DateTime updated_date { get; set; }
    }
}
