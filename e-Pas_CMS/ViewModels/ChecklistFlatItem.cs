namespace e_Pas_CMS.ViewModels
{
    public class ChecklistFlatItem
    {
        public string id { get; set; }
        public string parent_id { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string comment { get; set; }
        public bool is_penalty { get; set; }
        public string penalty_alert { get; set; }

        public string type { get; set; }
        public decimal? weight { get; set; }
        public string score_input { get; set; }
        public decimal? score_af { get; set; }
        public decimal? score_x { get; set; }
        public string media_path { get; set; }
        public string tacid { get; set; }
        public string score_option { get; set; }

        public int order_no { get; set; }

    }

}
