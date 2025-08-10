using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class TrxFeedbackPointElement
{
    public string Id { get; set; } = null!;

    public string TrxFeedbackPointId { get; set; } = null!;

    public string MasterQuestionerDetailId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual master_questioner_detail MasterQuestionerDetail { get; set; } = null!;

    public virtual TrxFeedbackPoint TrxFeedbackPoint { get; set; } = null!;
}
