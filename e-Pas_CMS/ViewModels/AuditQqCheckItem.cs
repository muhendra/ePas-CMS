namespace e_Pas_CMS.ViewModels
{
    public class AuditQqCheckItem
    {
        public string NozzleNumber { get; set; }
        public string DuMake { get; set; }
        public string DuSerialNo { get; set; }
        public string Product { get; set; }
        public string Mode { get; set; }
        public decimal? QuantityVariationWithMeasure { get; set; }
        public decimal? QuantityVariationInPercentage { get; set; }
        public decimal? ObservedDensity { get; set; }
        public decimal? ObservedTemp { get; set; }
        public decimal? ObservedDensity15Degree { get; set; }
        public decimal? ReferenceDensity15Degree { get; set; }
        public int? TankNumber { get; set; }
        public decimal? DensityVariation { get; set; }
    }

}
