using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxAuditQq
{
    public string Id { get; set; } = null!;

    public string TrxAuditId { get; set; } = null!;

    public string NozzleNumber { get; set; } = null!;

    public string? DuMake { get; set; }

    public string? DuSerialNo { get; set; }

    public string? Product { get; set; }

    public string? Mode { get; set; }

    public decimal? QuantityVariationWithMeasure { get; set; }

    public decimal? QuantityVariationInPercentage { get; set; }

    public decimal? ObservedDensity { get; set; }

    public decimal? ObservedTemp { get; set; }

    public decimal? ObservedDensity15Degree { get; set; }

    public decimal? ReferenceDensity15Degree { get; set; }

    public int? TankNumber { get; set; }

    public decimal? DensityVariation { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual TrxAudit TrxAudit { get; set; } = null!;
}
