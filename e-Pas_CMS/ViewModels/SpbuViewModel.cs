namespace e_Pas_CMS.ViewModels
{
    public class SpbuViewModel
    {
        public string Id { get; set; }
        public string NoSpbu { get; set; }
        public string NamaAuditor { get; set; }
        public string Provinsi { get; set; }
        public string Kota { get; set; }
        public string Report { get; set; }
        public DateTime TanggalSubmit { get; set; }
        public string Status { get; set; }       // PASS / FAIL
        public string Komplain { get; set; }     // ADA / Tidak Ada
        public string Banding { get; set; }      // ADA / Tidak Ada
        public string Type { get; set; }
    }
}
