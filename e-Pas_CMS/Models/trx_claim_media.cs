namespace e_Pas_CMS.Models
{
    public partial class trx_claim_media
    {
        public string id { get; set; }

        public string trx_claim_id { get; set; }

        public string claim_item_type { get; set; }
        public string media_type { get; set; }
        public string media_path { get; set; }

        public string created_by { get; set; }
        public DateTime created_date { get; set; }
    }
}
