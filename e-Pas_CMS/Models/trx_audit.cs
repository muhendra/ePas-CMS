using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class trx_audit
{
    public string id { get; set; } = null!;

    public string? report_prefix { get; set; }

    public string? report_no { get; set; }

    public string spbu_id { get; set; } = null!;

    public string? app_user_id { get; set; }

    public string? master_questioner_intro_id { get; set; }

    public string? master_questioner_checklist_id { get; set; }

    public string audit_level { get; set; } = null!;

    public string audit_type { get; set; } = null!;
    public decimal? score { get; set; } = 0;

    public DateOnly? audit_schedule_date { get; set; }

    public DateTime? audit_execution_time { get; set; }

    public int? audit_media_upload { get; set; }

    public int? audit_media_total { get; set; }

    public string? audit_mom_intro { get; set; }

    public string? audit_mom_final { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public DateTime? approval_date { get; set; }
    public string? approval_by { get; set; }

    public string? report_file_good { get; set; }

    public string? report_file_excellent { get; set; }

    public string? report_file_boa { get; set; }

    public virtual app_user? app_user { get; set; }

    public virtual master_questioner? master_questioner_checklist { get; set; }

    public virtual master_questioner? master_questioner_intro { get; set; }

    public virtual spbu spbu { get; set; } = null!;

    public virtual ICollection<trx_audit_checklist> trx_audit_checklists { get; set; } = new List<trx_audit_checklist>();

    public virtual ICollection<trx_audit_medium> trx_audit_media { get; set; } = new List<trx_audit_medium>();

    public virtual ICollection<trx_audit_qq> trx_audit_qqs { get; set; } = new List<trx_audit_qq>();

    public virtual ICollection<TrxFeedback> TrxFeedbacks { get; set; } = new List<TrxFeedback>();
}
