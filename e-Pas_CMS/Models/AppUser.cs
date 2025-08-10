using System;
using System.Collections.Generic;

namespace e_Pas_CMS.Models;

public partial class AppUser
{
    public string Id { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? NotificationToken { get; set; }

    public DateTime? LastChangePasswdDt { get; set; }

    public DateTime? LastLoginDt { get; set; }

    public string? SuffixRefreshToken { get; set; }

    public string Status { get; set; } = null!;

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string UpdatedBy { get; set; } = null!;

    public DateTime? UpdatedDate { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<TrxAudit> TrxAudits { get; set; } = new List<TrxAudit>();

    public virtual ICollection<TrxFeedback> TrxFeedbacks { get; set; } = new List<TrxFeedback>();

    public virtual ICollection<TrxSurvey> TrxSurveys { get; set; } = new List<TrxSurvey>();
}
