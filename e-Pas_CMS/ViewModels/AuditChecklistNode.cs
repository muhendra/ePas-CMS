namespace e_Pas_CMS.ViewModels
{
    public class AuditChecklistNode
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        public string Type { get; set; }
        public string ScoreInput { get; set; }
        public decimal? ScoreAF { get; set; }
        public decimal? ScoreX { get; set; }
        public decimal? Weight { get; set; }
        public string media_path { get; set; }
        public string tacid { get; set; }
        public List<AuditChecklistNode> Children { get; set; } = new();
    }
}
