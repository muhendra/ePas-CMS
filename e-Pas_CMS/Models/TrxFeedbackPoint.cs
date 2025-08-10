using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxFeedbackPoint
{
    public string Id { get; set; } = null!;

    public string TrxFeedbackId { get; set; } = null!;

    public string? Description { get; set; }

    public string? ElementMasterQuestionerDetailId { get; set; }

    public string? SubElementMasterQuestionerDetailId { get; set; }

    public string? DetailElementMasterQuestionerDetailId { get; set; }

    public int? MediaUpload { get; set; }

    public int? MediaTotal { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual master_questioner_detail? ElementMasterQuestionerDetail { get; set; }

    public virtual TrxFeedback TrxFeedback { get; set; } = null!;

    public virtual ICollection<TrxFeedbackPointElement> TrxFeedbackPointElements { get; set; } = new List<TrxFeedbackPointElement>();

    public virtual ICollection<TrxFeedbackPointMedium> TrxFeedbackPointMedia { get; set; } = new List<TrxFeedbackPointMedium>();
}
