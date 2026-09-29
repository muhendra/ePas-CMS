using System;

namespace e_Pas_CMS.Models;

public class trx_audit_import_summary
{
    public string id { get; set; } = null!;
    public string trx_audit_id { get; set; } = null!;

    public DateTime? send_date { get; set; }
    public DateTime? audit_date { get; set; }

    public decimal? total_score { get; set; }

    public decimal? sss { get; set; }
    public decimal? eqnq { get; set; }
    public decimal? rfs { get; set; }
    public decimal? vfc { get; set; }
    public decimal? epo { get; set; }

    // Optional fields. If the uploaded template does not contain them, keep NULL.
    public decimal? wtms { get; set; }
    public decimal? qq { get; set; }
    public decimal? wmef { get; set; }
    public decimal? format_fisik { get; set; }
    public decimal? cpo { get; set; }

    public string? kelas_spbu { get; set; }
    public string? audit_next { get; set; }

    public string? penalty_good_alerts { get; set; }
    public string? penalty_excellent_alerts { get; set; }

    public string? source_file { get; set; }

    public string created_by { get; set; } = null!;
    public DateTime created_date { get; set; }
}
