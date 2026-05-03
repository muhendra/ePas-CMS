using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace e_Pas_CMS.ViewModels
{
    public class MasterChecklistIndexVm
    {
        public string Type { get; set; }
        public string Category { get; set; } = "CHECKLIST";
        public string Search { get; set; }

        public List<SelectListItem> TypeOptions { get; set; } = new();
        public List<SelectListItem> CategoryOptions { get; set; } = new();

        public List<MasterChecklistHeaderVm> Items { get; set; } = new();
    }

    public class MasterChecklistHeaderVm
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public int Version { get; set; }
        public DateTime EffectiveStartDate { get; set; }
        public DateTime EffectiveEndDate { get; set; }
        public string Status { get; set; }
        public int TotalNode { get; set; }
        public int TotalQuestion { get; set; }
    }

    public class MasterChecklistEditVm
    {
        public string MasterQuestionerId { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public DateTime EffectiveStartDate { get; set; }
        public DateTime EffectiveEndDate { get; set; }

        public List<MasterChecklistNodeVm> Nodes { get; set; } = new();
    }

    public class MasterChecklistNodeVm
    {
        public string Id { get; set; }
        public string MasterQuestionerId { get; set; }
        public string ParentId { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ScoreOption { get; set; }
        public decimal? Weight { get; set; }
        public int OrderNo { get; set; }
        public string Status { get; set; }

        public bool? IsPenalty { get; set; }
        public string PenaltyAlert { get; set; }
        public bool? IsRelaksasi { get; set; }
        public string PenaltyExcellentCriteria { get; set; }
        public string ScoreExcellentCriteria { get; set; }
        public string FormType { get; set; }

        public List<MasterChecklistNodeVm> Children { get; set; } = new();
    }

    public class SaveChecklistNodeRequest
    {
        public string Id { get; set; }
        public string MasterQuestionerId { get; set; }
        public string ParentId { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ScoreOption { get; set; }
        public decimal? Weight { get; set; }
        public int OrderNo { get; set; }
        public string Status { get; set; }

        public bool? IsPenalty { get; set; }
        public string PenaltyAlert { get; set; }
        public bool? IsRelaksasi { get; set; }
        public string PenaltyExcellentCriteria { get; set; }
        public string ScoreExcellentCriteria { get; set; }
        public string FormType { get; set; }
    }
}