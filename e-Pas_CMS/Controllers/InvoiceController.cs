using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using e_Pas_CMS.Data;
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
            .Where(r => r != null)
            .Distinct()
            .ToListAsync();

            var userSbm = await (
                from aur in _context.app_user_roles
                join au in _context.app_users on aur.app_user_id equals au.id
                where au.username == currentUser
                select aur.sbm
            )
            .Where(s => s != null)
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
                select new
                {
                    Invoice = inv,
                    Claim = claim,
                    Detail = det,
                    Audit = aud,
                    Spbu = sp,
                    SurveyorName = usr != null ? usr.name : "-"
                };

            query = query.Where(x =>
                x.Claim.status == ClaimUnderReview ||
                x.Claim.status == ClaimPendingApproval ||
                x.Claim.status == ClaimApproved ||
                x.Claim.status == ClaimRejected
            );

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
                .GroupBy(x => x.Invoice.Id)
                .Select(g => new InvoiceVM
                {
                    Id = g.First().Invoice.Id,
                    InvoiceNo = g.First().Invoice.InvoiceNo,
                    InvoiceDate = g.First().Invoice.IssuedDate,
                    EmployeeId = g.First().Invoice.AppUserId,
                    ExpectedDate = g.First().Invoice.DueDate,

                    // Status di list sekarang pakai trx_claim.status
                    Status = g.First().Claim.status,

                    TotalAmount = g.Sum(x => x.Detail.Amount),
                    SurveyorName = g.First().SurveyorName
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

        var details = await (
            from d in _context.TrxClaimDetails.AsNoTracking()
            where d.trx_claim_id == claim.id
            select new InvoiceDetailItemVM
            {
                Date = DateTime.SpecifyKind(claim.claim_date, DateTimeKind.Utc),
                Description = d.description,
                Amount = d.amount,
                AuditFee = d.amount,
                Lumpsum = 0
            }
        ).ToListAsync();

        var attachments = await _context.TrxClaimMedias
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .ToListAsync();

        var attachmentGroups = attachments
            .Where(x => !string.IsNullOrWhiteSpace(x.claim_item_type))
            .GroupBy(x => x.claim_item_type)
            .ToDictionary(
                g => g.Key,
                g => g.First()
            );

        var totalExpense = details.Sum(x => x.Amount);
        var totalAuditFee = details.Sum(x => x.AuditFee);
        var totalLumpsum = details.Sum(x => x.Lumpsum);

        var isProcessOrDone =
            claim.status == ClaimUnderReview ||
            claim.status == ClaimPendingApproval ||
            claim.status == ClaimApproved ||
            claim.status == ClaimRejected;

        var vm = new InvoiceDetailViewModel
        {
            Id = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,

            // Tetap simpan status invoice di Model.Status
            // Claim status dikirim lewat ViewBag.ClaimStatus
            Status = invoice.Status,

            ClaimDate = claim.claim_date,

            EmployeeName = "Muhammad Ramdan",
            Period = isProcessOrDone ? "Januari 2026" : "6 Januari - 15 Februari",
            Homebase = "Depok",
            Job = "Audit SPBU",

            Items = details,

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

            ApprovedBy = claim.status == ClaimApproved
                ? "Finance / Accounting"
                : null,

            ApprovedDate = claim.status == ClaimApproved
                ? DateTime.Now
                : null
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

        invoice.Status = InvoiceInProgress;
        claim.status = ClaimPendingApproval;

        foreach (var detail in invoiceDetails)
        {
            detail.Status = InvoiceDetailNotClaimed;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Invoice berhasil diproses dan masuk ke pending approval.";

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id)
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

        if (claim.status != ClaimPendingApproval)
        {
            TempData["Error"] = "Claim belum masuk tahap pending approval.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var invoiceDetails = await _context.TrxInvoiceDetails
            .Where(x => x.TrxInvoiceId == id)
            .ToListAsync();

        invoice.Status = InvoiceCompleted;
        claim.status = ClaimApproved;

        foreach (var detail in invoiceDetails)
        {
            detail.Status = InvoiceDetailClaimed;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Invoice berhasil disetujui.";

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(string id)
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

        if (claim.status != ClaimPendingApproval)
        {
            TempData["Error"] = "Claim belum masuk tahap pending approval.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var invoiceDetails = await _context.TrxInvoiceDetails
            .Where(x => x.TrxInvoiceId == id)
            .ToListAsync();

        invoice.Status = InvoiceRejected;
        claim.status = ClaimRejected;

        foreach (var detail in invoiceDetails)
        {
            detail.Status = InvoiceDetailNotClaimed;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Invoice berhasil ditolak.";

        return RedirectToAction(nameof(Detail), new { id });
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
}