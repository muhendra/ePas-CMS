namespace e_Pas_CMS.ViewModels
{
    public class ChecklistFlatSum
    {
        public string RootElementTitle { get; set; }  // "Elemen 1" s.d. "Elemen 5"
        public string ParentId { get; set; }          // Untuk grouping Elemen 2 dan 5
        public decimal? Weight { get; set; }
        public string ScoreInput { get; set; }
        public decimal? ScoreX { get; set; }
        public bool? IsRelaksasi { get; set; }
    }
}
