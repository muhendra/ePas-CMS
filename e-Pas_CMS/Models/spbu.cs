using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace e_Pas_CMS.Models;

public partial class spbu
{
    [BindNever] // ⛔ jangan divalidasi dari form
    public string id { get; set; } = null!;

    [Required]
    public string spbu_no { get; set; } = null!;

    [Required]
    public string region { get; set; } = null!;

    [Required]
    public string province_name { get; set; } = null!;

    [Required]
    public string city_name { get; set; } = null!;

    public string? address { get; set; }

    public string? owner_name { get; set; }

    public string? manager_name { get; set; }

    public string? owner_type { get; set; }

    public int? quater { get; set; }

    public int? year { get; set; }

    public string? mor { get; set; }

    public string? sales_area { get; set; }

    public string? sbm { get; set; }

    public string? sam { get; set; }

    public string? type { get; set; }

    public string? phone_number_1 { get; set; }

    public string? phone_number_2 { get; set; }

    public string? level { get; set; }

    public double? latitude { get; set; }

    public double? longitude { get; set; }

    public string? audit_current { get; set; }

    public string? audit_next { get; set; }

    public string? status_good { get; set; }

    public string? status_excellent { get; set; }

    public decimal? audit_current_score { get; set; }

    public DateTime? audit_current_time { get; set; }

    [Required]
    public string status { get; set; } = null!;

    [BindNever] // ⛔ isi manual
    [Required]
    public string created_by { get; set; } = null!;

    [BindNever]
    [Required]
    public DateTime created_date { get; set; }

    [BindNever]
    [Required]
    public string updated_by { get; set; } = null!;

    [BindNever]
    public DateTime? updated_date { get; set; }

    public decimal wtms { get; set; }

    public decimal qq { get; set; }

    public decimal wmef { get; set; }

    public decimal format_fisik { get; set; }

    public decimal cpo { get; set; }

    public virtual ICollection<app_user_role> app_user_roles { get; set; } = new List<app_user_role>();

    public virtual ICollection<spbu_image> spbu_images { get; set; } = new List<spbu_image>();

    public virtual ICollection<trx_audit> trx_audits { get; set; } = new List<trx_audit>();
}
