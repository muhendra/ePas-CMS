using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using e_Pas_CMS.ViewModels;

public class InvoiceController : Controller
{
    private readonly EpasDbContext _context;
    private const int DefaultPageSize = 10;

    private const string InvoiceNotClaimed = "NOT_CLAIMED";
    private const string InvoiceInProgress = "IN_PROGRESS";
    private const string InvoiceCompleted = "COMPLETED";
    private const string InvoiceRejected = "REJECTED";

    private const string InvoiceDetailNotClaimed = "NOT_CLAIMED";
    private const string InvoiceDetailClaimed = "CLAIMED";

    private const string ClaimInProgressSubmit = "IN_PROGRESS_SUBMIT";
    private const string ClaimUnderReview = "UNDER_REVIEW";
    private const string ClaimPendingApproval = "PENDING_APPROVAL";
    private const string ClaimApproved = "APPROVED";
    private const string ClaimRejected = "REJECTED";

    public InvoiceController(EpasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(
        int pageNumber = 1,
        int pageSize = DefaultPageSize,
        string searchTerm = "",
        string sortColumn = "InvoiceDate",
        string sortDirection = "desc")
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = DefaultPageSize;

            var currentUser = User.Identity?.Name ?? "";

            var userRegion = await (
                from aur in _context.app_user_roles
                join au in _context.app_users on aur.app_user_id equals au.id
                where au.username == currentUser
                select aur.region
            )
            .Where(r => r != null && r != "")
            .Distinct()
            .ToListAsync();

            var userSbm = await (
                from aur in _context.app_user_roles
                join au in _context.app_users on aur.app_user_id equals au.id
                where au.username == currentUser
                select aur.sbm
            )
            .Where(s => s != null && s != "")
            .Distinct()
            .ToListAsync();

            var query =
                from inv in _context.TrxInvoices.AsNoTracking()
                join claim in _context.TrxClaims.AsNoTracking()
                    on inv.Id equals claim.trx_invoice_id
                join det in _context.TrxInvoiceDetails.AsNoTracking()
                    on inv.Id equals det.TrxInvoiceId
                join aud in _context.trx_audits.AsNoTracking()
                    on det.TrxAuditId equals aud.id
                join sp in _context.spbus.AsNoTracking()
                    on aud.spbu_id equals sp.id
                join usr in _context.app_users.AsNoTracking()
                    on aud.app_user_id equals usr.id into audUser
                from usr in audUser.DefaultIfEmpty()
                where claim.status == ClaimUnderReview
                   || claim.status == ClaimPendingApproval
                   || claim.status == ClaimApproved
                   || claim.status == ClaimRejected
                select new
                {
                    Invoice = inv,
                    Claim = claim,
                    Detail = det,
                    Audit = aud,
                    Spbu = sp,
                    SurveyorName = usr != null ? usr.name : "-"
                };

            if (userRegion.Any() || userSbm.Any())
            {
                query = query.Where(x =>
                    (x.Spbu.region != null && userRegion.Contains(x.Spbu.region)) ||
                    (x.Spbu.sbm != null && userSbm.Contains(x.Spbu.sbm))
                );
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var keyword = searchTerm.Trim().ToLower();

                query = query.Where(x =>
                    (x.Invoice.InvoiceNo ?? "").ToLower().Contains(keyword) ||
                    (x.Invoice.AppUserId ?? "").ToLower().Contains(keyword) ||
                    (x.Spbu.spbu_no ?? "").ToLower().Contains(keyword) ||
                    (x.SurveyorName ?? "").ToLower().Contains(keyword)
                );
            }

            var grouped = query
                .GroupBy(x => new
                {
                    x.Invoice.Id,
                    x.Invoice.InvoiceNo,
                    x.Invoice.IssuedDate,
                    x.Invoice.AppUserId,
                    x.Invoice.DueDate,
                    ClaimId = x.Claim.id,
                    ClaimStatus = x.Claim.status,
                    x.SurveyorName
                })
                .Select(g => new InvoiceVM
                {
                    Id = g.Key.Id,
                    InvoiceNo = g.Key.InvoiceNo,
                    InvoiceDate = g.Key.IssuedDate,
                    EmployeeId = g.Key.AppUserId,
                    ExpectedDate = g.Key.DueDate,
                    Status = g.Key.ClaimStatus,
                    SurveyorName = g.Key.SurveyorName,

                    TotalAmount =
                        (
                            _context.TrxClaimDetails
                                .Where(cd => cd.trx_claim_id == g.Key.ClaimId)
                                .Sum(cd => (decimal?)cd.amount) ?? 0
                        )
                        + g.Sum(x => x.Detail.AuditFee)
                        + g.Sum(x => x.Detail.LumpsumFee ?? 0)
                });

            grouped = sortColumn switch
            {
                "InvoiceNo" => sortDirection == "asc"
                    ? grouped.OrderBy(x => x.InvoiceNo)
                    : grouped.OrderByDescending(x => x.InvoiceNo),

                "InvoiceDate" => sortDirection == "asc"
                    ? grouped.OrderBy(x => x.InvoiceDate)
                    : grouped.OrderByDescending(x => x.InvoiceDate),

                "ExpectedDate" => sortDirection == "asc"
                    ? grouped.OrderBy(x => x.ExpectedDate)
                    : grouped.OrderByDescending(x => x.ExpectedDate),

                "TotalAmount" => sortDirection == "asc"
                    ? grouped.OrderBy(x => x.TotalAmount)
                    : grouped.OrderByDescending(x => x.TotalAmount),

                "Status" => sortDirection == "asc"
                    ? grouped.OrderBy(x => x.Status)
                    : grouped.OrderByDescending(x => x.Status),

                _ => grouped.OrderByDescending(x => x.InvoiceDate)
            };

            var totalItems = await grouped.CountAsync();

            var items = await grouped
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var model = new PaginationModel<InvoiceVM>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.SearchTerm = searchTerm;
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;

            return View(model);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Gagal load invoice: " + ex.Message;

            return View(new PaginationModel<InvoiceVM>
            {
                Items = new List<InvoiceVM>(),
                PageNumber = 1,
                PageSize = DefaultPageSize,
                TotalItems = 0
            });
        }
    }

    public async Task<IActionResult> Detail(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var invoice = await _context.TrxInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (invoice == null)
            return NotFound();

        var claim = await _context.TrxClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.trx_invoice_id == id);

        if (claim == null)
            return NotFound();

        var claimExpenseAmount = await _context.TrxClaimDetails
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .SumAsync(x => (decimal?)x.amount) ?? 0;

        var firstClaimDescription = await _context.TrxClaimDetails
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .OrderBy(x => x.created_date)
            .Select(x => x.description)
            .FirstOrDefaultAsync();

        var details = await (
            from det in _context.TrxInvoiceDetails.AsNoTracking()
            join aud in _context.trx_audits.AsNoTracking()
                on det.TrxAuditId equals aud.id
            join sp in _context.spbus.AsNoTracking()
                on aud.spbu_id equals sp.id
            where det.TrxInvoiceId == id
            orderby aud.audit_execution_time descending,
                    aud.audit_schedule_date descending,
                    det.CreatedDate descending
            select new InvoiceDetailItemVM
            {
                TrxInvoiceDetailId = det.Id,
                TrxAuditId = det.TrxAuditId,

                Date = DateTime.SpecifyKind(
                    aud.audit_execution_time
                        ?? (
                            aud.audit_schedule_date.HasValue
                                ? aud.audit_schedule_date.Value.ToDateTime(TimeOnly.MinValue)
                                : aud.created_date
                        ),
                    DateTimeKind.Utc
                ),

                Description = firstClaimDescription ?? ("Audit SPBU " + sp.spbu_no),

                ClaimAmount = 0,
                AuditFee = det.AuditFee,
                Lumpsum = det.LumpsumFee ?? 0,
                Amount = det.AuditFee + (det.LumpsumFee ?? 0)
            }
        ).ToListAsync();

        if (details.Any())
        {
            details[0].ClaimAmount = claimExpenseAmount;
            details[0].Amount = details[0].Amount + claimExpenseAmount;
        }

        var totalAuditFee = details.Sum(x => x.AuditFee);
        var totalLumpsum = details.Sum(x => x.Lumpsum);
        var totalExpense = claimExpenseAmount + totalAuditFee + totalLumpsum;

        var attachments = await _context.TrxClaimMedias
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .ToListAsync();

        var attachmentGroups = attachments
            .Where(x => !string.IsNullOrWhiteSpace(x.claim_item_type))
            .GroupBy(x => x.claim_item_type)
            .ToDictionary(g => g.Key, g => g.First());

        var latestApproval = await _context.TrxInvoiceApprovals
            .AsNoTracking()
            .Where(x => x.TrxInvoiceId == invoice.Id)
            .OrderByDescending(x => x.ApprovedDate)
            .FirstOrDefaultAsync();

        var isProcessOrDone =
            claim.status == ClaimUnderReview ||
            claim.status == ClaimPendingApproval ||
            claim.status == ClaimApproved ||
            claim.status == ClaimRejected;

        var vm = new InvoiceDetailViewModel
        {
            Id = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Status = invoice.Status,
            ClaimDate = claim.claim_date,

            EmployeeName = await GetEmployeeName(invoice.AppUserId, claim.app_user_id),
            Period = $"{invoice.InvoicePeriodStart:dd MMM yyyy} - {invoice.InvoicePeriodEnd:dd MMM yyyy}",
            Homebase = "-",
            Job = "Audit SPBU",

            Items = details,

            ClaimExpenseAmount = claimExpenseAmount,
            TotalExpense = totalExpense,
            TotalAuditFee = totalAuditFee,
            TotalLumpsum = totalLumpsum,

            LessDirectCharges = totalExpense,
            LessAdvances = 0,

            Attachments = attachments,
            AttachmentGroups = attachmentGroups,

            BankName = isProcessOrDone ? "Bank Mandiri" : "",
            BankAccount = isProcessOrDone ? "129383481" : "",
            BankOwner = "Deswantri Alfariza. H",

            ApprovedBy = latestApproval != null
                ? latestApproval.ApprovedBy
                : claim.status == ClaimApproved
                    ? "Finance / Accounting"
                    : null,

            ApprovedDate = latestApproval?.ApprovedDate,

            RejectionReason = latestApproval?.RejectionReason
        };

        ViewBag.InvoiceStatus = invoice.Status;
        ViewBag.InvoiceStatusLabel = GetInvoiceStatusLabel(invoice.Status);

        ViewBag.ClaimStatus = claim.status;
        ViewBag.ClaimStatusLabel = GetClaimStatusLabel(claim.status);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(string id)
    {
        return await StartProcess(id);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartProcess(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var invoice = await _context.TrxInvoices
            .FirstOrDefaultAsync(x => x.Id == id);

        if (invoice == null)
            return NotFound();

        var claim = await _context.TrxClaims
            .FirstOrDefaultAsync(x => x.trx_invoice_id == id);

        if (claim == null)
        {
            TempData["Error"] = "Data claim tidak ditemukan.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (claim.status != ClaimUnderReview)
        {
            TempData["Error"] = "Claim belum masuk tahap under review.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var invoiceDetails = await _context.TrxInvoiceDetails
            .Where(x => x.TrxInvoiceId == id)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var currentUser = GetCurrentUser();

        invoice.Status = InvoiceInProgress;
        invoice.UpdatedBy = currentUser;
        invoice.UpdatedDate = now;

        claim.status = ClaimPendingApproval;
        claim.updated_by = currentUser;
        claim.updated_date = now;

        foreach (var detail in invoiceDetails)
        {
            detail.Status = InvoiceDetailNotClaimed;
            detail.UpdatedBy = currentUser;
            detail.UpdatedDate = now;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Invoice berhasil diproses dan masuk ke pending approval.";

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id, InvoiceApprovalPostVM model)
    {
        var invoiceId = ResolveInvoiceId(id, model);

        if (string.IsNullOrWhiteSpace(invoiceId))
            return NotFound();

        var invoice = await _context.TrxInvoices
            .FirstOrDefaultAsync(x => x.Id == invoiceId);

        if (invoice == null)
            return NotFound();

        var claim = await _context.TrxClaims
            .FirstOrDefaultAsync(x => x.trx_invoice_id == invoiceId);

        if (claim == null)
        {
            TempData["Error"] = "Data claim tidak ditemukan.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        if (claim.status != ClaimPendingApproval)
        {
            TempData["Error"] = "Claim belum masuk tahap pending approval.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        var invoiceDetails = await _context.TrxInvoiceDetails
            .Where(x => x.TrxInvoiceId == invoiceId)
            .ToListAsync();

        if (!invoiceDetails.Any())
        {
            TempData["Error"] = "Detail invoice tidak ditemukan.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        ApplyFinanceAdjustment(invoiceDetails, model?.Items);

        var approval = await BuildApprovalSnapshot(
            invoice,
            claim,
            invoiceDetails,
            "APPROVED",
            null
        );

        var now = DateTime.UtcNow;
        var currentUser = GetCurrentUser();

        invoice.Status = InvoiceCompleted;
        invoice.CompletedDate = now;
        invoice.UpdatedBy = currentUser;
        invoice.UpdatedDate = now;

        claim.status = ClaimApproved;
        claim.completed_date = now;
        claim.updated_by = currentUser;
        claim.updated_date = now;

        foreach (var detail in invoiceDetails)
        {
            detail.Status = InvoiceDetailClaimed;
            detail.UpdatedBy = currentUser;
            detail.UpdatedDate = now;
        }

        _context.TrxInvoiceApprovals.Add(approval);

        await _context.SaveChangesAsync();

        TempData["Success"] = "Invoice berhasil disetujui dan data approval finance berhasil disimpan.";

        return RedirectToAction(nameof(Detail), new { id = invoiceId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(string id, InvoiceApprovalPostVM model)
    {
        var invoiceId = ResolveInvoiceId(id, model);

        if (string.IsNullOrWhiteSpace(invoiceId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(model?.RejectionReason))
        {
            TempData["Error"] = "Alasan ditolak wajib diisi.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        var invoice = await _context.TrxInvoices
            .FirstOrDefaultAsync(x => x.Id == invoiceId);

        if (invoice == null)
            return NotFound();

        var claim = await _context.TrxClaims
            .FirstOrDefaultAsync(x => x.trx_invoice_id == invoiceId);

        if (claim == null)
        {
            TempData["Error"] = "Data claim tidak ditemukan.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        if (claim.status != ClaimPendingApproval)
        {
            TempData["Error"] = "Claim belum masuk tahap pending approval.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        var invoiceDetails = await _context.TrxInvoiceDetails
            .Where(x => x.TrxInvoiceId == invoiceId)
            .ToListAsync();

        if (!invoiceDetails.Any())
        {
            TempData["Error"] = "Detail invoice tidak ditemukan.";
            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }

        ApplyFinanceAdjustment(invoiceDetails, model?.Items);

        var approval = await BuildApprovalSnapshot(
            invoice,
            claim,
            invoiceDetails,
            "REJECTED",
            model.RejectionReason.Trim()
        );

        var now = DateTime.UtcNow;
        var currentUser = GetCurrentUser();

        invoice.Status = InvoiceRejected;
        invoice.UpdatedBy = currentUser;
        invoice.UpdatedDate = now;

        claim.status = ClaimRejected;
        claim.completed_date = now;
        claim.updated_by = currentUser;
        claim.updated_date = now;

        foreach (var detail in invoiceDetails)
        {
            detail.Status = InvoiceDetailNotClaimed;
            detail.UpdatedBy = currentUser;
            detail.UpdatedDate = now;
        }

        _context.TrxInvoiceApprovals.Add(approval);

        await _context.SaveChangesAsync();

        TempData["Success"] = "Invoice berhasil ditolak dan alasan penolakan berhasil disimpan.";

        return RedirectToAction(nameof(Detail), new { id = invoiceId });
    }

    public IActionResult DownloadPdf(string id)
    {
        TempData["Error"] = "Fitur download PDF belum tersedia.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    public string GetInvoiceStatusLabel(string status)
    {
        return status switch
        {
            InvoiceNotClaimed => "Belum Diklaim",
            InvoiceInProgress => "Diproses",
            InvoiceCompleted => "Selesai",
            InvoiceRejected => "Ditolak",
            _ => status ?? "-"
        };
    }

    public string GetStatusLabel(string status)
    {
        return GetInvoiceStatusLabel(status);
    }

    public string GetClaimStatusLabel(string status)
    {
        return status switch
        {
            ClaimInProgressSubmit => "Submit Diproses",
            ClaimUnderReview => "Sedang Direview",
            ClaimPendingApproval => "Menunggu Persetujuan",
            ClaimApproved => "Disetujui",
            ClaimRejected => "Ditolak",
            _ => status ?? "-"
        };
    }

    private static string ResolveInvoiceId(string id, InvoiceApprovalPostVM model)
    {
        if (!string.IsNullOrWhiteSpace(model?.Id))
            return model.Id;

        return id;
    }

    private void ApplyFinanceAdjustment(
        List<TrxInvoiceDetail> invoiceDetails,
        List<InvoiceApprovalDetailPostVM> postedItems)
    {
        if (postedItems == null || !postedItems.Any())
            return;

        var postedMap = postedItems
            .Where(x => !string.IsNullOrWhiteSpace(x.TrxInvoiceDetailId))
            .GroupBy(x => x.TrxInvoiceDetailId)
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var detail in invoiceDetails)
        {
            if (!postedMap.TryGetValue(detail.Id, out var posted))
                continue;

            detail.AuditFee = posted.AuditFee < 0 ? 0 : posted.AuditFee;
            detail.LumpsumFee = posted.LumpsumFee < 0 ? 0 : posted.LumpsumFee;
        }
    }

    private async Task<TrxInvoiceApproval> BuildApprovalSnapshot(
    TrxInvoice invoice,
    trx_claim claim,
    List<TrxInvoiceDetail> invoiceDetails,
    string action,
    string rejectionReason)
    {
        var claimExpenseAmount = await _context.TrxClaimDetails
            .Where(x => x.trx_claim_id == claim.id)
            .SumAsync(x => (decimal?)x.amount) ?? 0;

        var totalAuditFee = invoiceDetails.Sum(x => x.AuditFee);
        var totalLumpsumFee = invoiceDetails.Sum(x => x.LumpsumFee ?? 0);
        var totalExpense = claimExpenseAmount + totalAuditFee + totalLumpsumFee;

        var currentUser = GetCurrentUser();
        var now = DateTime.UtcNow;

        return new TrxInvoiceApproval
        {
            Id = Guid.NewGuid().ToString(),
            TrxInvoiceId = invoice.Id,
            TrxClaimId = claim.id,
            ApprovalAction = action,
            ClaimExpenseAmount = claimExpenseAmount,
            TotalAuditFee = totalAuditFee,
            TotalLumpsumFee = totalLumpsumFee,
            TotalExpense = totalExpense,
            RejectionReason = rejectionReason,
            ApprovedBy = currentUser,
            ApprovedDate = now,
            CreatedBy = currentUser,
            CreatedDate = now,
            TrxInvoiceApprovalDetails = invoiceDetails.Select(x => new TrxInvoiceApprovalDetail
            {
                Id = Guid.NewGuid().ToString(),
                TrxInvoiceDetailId = x.Id,
                TrxAuditId = x.TrxAuditId,
                AuditFee = x.AuditFee,
                LumpsumFee = x.LumpsumFee ?? 0,
                LineTotal = x.AuditFee + (x.LumpsumFee ?? 0),
                CreatedBy = currentUser,
                CreatedDate = now
            }).ToList()
        };
    }
    private string GetCurrentUser()
    {
        return User.Identity?.Name ?? "SYSTEM";
    }

    private async Task<string> GetEmployeeName(string invoiceAppUserId, string claimAppUserId)
    {
        var userId = !string.IsNullOrWhiteSpace(invoiceAppUserId)
            ? invoiceAppUserId
            : claimAppUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return "-";

        var name = await _context.app_users
            .AsNoTracking()
            .Where(x => x.id == userId)
            .Select(x => x.name)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(name) ? "-" : name;
    }
}

public class InvoiceApprovalPostVM
{
    public string Id { get; set; }
    public string RejectionReason { get; set; }
    public List<InvoiceApprovalDetailPostVM> Items { get; set; } = new List<InvoiceApprovalDetailPostVM>();
}

public class InvoiceApprovalDetailPostVM
{
    public string TrxInvoiceDetailId { get; set; }
    public decimal AuditFee { get; set; }
    public decimal LumpsumFee { get; set; }
}