using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class master_questioner_detail
{
    public string id { get; set; } = null!;

    public string master_questioner_id { get; set; } = null!;

    public string? parent_id { get; set; }

    public string type { get; set; } = null!;

    public string title { get; set; } = null!;

    public string? description { get; set; }

    public string? score_option { get; set; }

    public decimal? weight { get; set; }

    public int order_no { get; set; }

    public string status { get; set; } = null!;

    public string created_by { get; set; } = null!;

    public DateTime created_date { get; set; }

    public string updated_by { get; set; } = null!;

    public DateTime? updated_date { get; set; }

    public virtual ICollection<master_questioner_detail> Inverseparent { get; set; } = new List<master_questioner_detail>();

    public virtual master_questioner master_questioner { get; set; } = null!;

    public virtual master_questioner_detail? parent { get; set; }

    public virtual ICollection<trx_audit_checklist> trx_audit_checklists { get; set; } = new List<trx_audit_checklist>();

    public virtual ICollection<trx_audit_medium> trx_audit_media { get; set; } = new List<trx_audit_medium>();

    public virtual ICollection<TrxFeedbackPointElement> TrxFeedbackPointElements { get; set; } = new List<TrxFeedbackPointElement>();

    public virtual ICollection<TrxFeedbackPoint> TrxFeedbackPoints { get; set; } = new List<TrxFeedbackPoint>();

    public virtual ICollection<TrxSurveyElement> TrxSurveyElements { get; set; } = new List<TrxSurveyElement>();
}
