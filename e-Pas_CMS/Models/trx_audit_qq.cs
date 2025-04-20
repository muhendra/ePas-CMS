using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class trx_audit_qq
{
    public string id { get; set; } = null!;

    public string trx_audit_id { get; set; } = null!;

    public string nozzle_number { get; set; } = null!;

    public string? du_make { get; set; }

    public string? du_serial_no { get; set; }

    public string? product { get; set; }

    public string? mode { get; set; }

    public decimal? quantity_variation_with_measure { get; set; }

    public decimal? quantity_variation_in_percentage { get; set; }

    public decimal? observed_density { get; set; }

    public decimal? observed_temp { get; set; }

    public decimal? observed_density_15_degree { get; set; }

    public decimal? reference_density_15_degree { get; set; }

    public int? tank_number { get; set; }

    public decimal? density_variation { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual trx_audit trx_audit { get; set; } = null!;
}
