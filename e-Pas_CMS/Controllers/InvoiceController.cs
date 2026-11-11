using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Authorize]
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
    private const string InvoiceDetailInProgress = "IN_PROGRESS";

    private const string ApprovalFlowPending = "PENDING";
    private const string ApprovalFlowApproved = "APPROVED";
    private const string ApprovalFlowRejected = "REJECTED";
    private const string ApprovalFlowCancelled = "CANCELLED";

    // Approval invoice is strictly user-based, never role-based.
    private static readonly string[] FixedApprovalUsernames =
    {
        "tri.handayani",
        "rommy.irawan",
        "sabar.kembaren"
    };

    public InvoiceController(EpasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(
        int pageNumber = 1,
        int pageSize = DefaultPageSize,
        string searchTerm = "",
        string auditorId = "",
        string periodClaim = "",
        string sortColumn = "InvoiceDate",
        string sortDirection = "desc")
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = DefaultPageSize;

            searchTerm = (searchTerm ?? "").Trim();
            auditorId = (auditorId ?? "").Trim();
            periodClaim = (periodClaim ?? "").Trim();

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
                    on inv.AppUserId equals usr.id into audUser
                from usr in audUser.DefaultIfEmpty()
                where
                    (
                        // Mobile Submit
                        // trx_invoice        = IN_PROGRESS
                        // trx_invoice_detail = IN_PROGRESS
                        // trx_claim          = UNDER_REVIEW
                        claim.status == ClaimUnderReview
                        && inv.Status == InvoiceInProgress
                        && det.Status == InvoiceDetailInProgress
                    )
                    ||
                    (
                        // Approval berjalan (setelah Level 1 claim menjadi PENDING_APPROVAL)
                        // trx_invoice        = IN_PROGRESS
                        // trx_invoice_detail = IN_PROGRESS
                        // trx_claim          = PENDING_APPROVAL
                        claim.status == ClaimPendingApproval
                        && inv.Status == InvoiceInProgress
                        && det.Status == InvoiceDetailInProgress
                    )
                    ||
                    (
                        // CMS Setuju
                        // trx_invoice        = COMPLETED
                        // trx_invoice_detail = CLAIMED
                        // trx_claim          = APPROVED
                        claim.status == ClaimApproved
                        && inv.Status == InvoiceCompleted
                        && det.Status == InvoiceDetailClaimed
                    )
                    ||
                    (
                        // CMS Tidak Setuju
                        // trx_invoice        = REJECTED
                        // trx_invoice_detail = NOT_CLAIMED
                        // trx_claim          = REJECTED
                        claim.status == ClaimRejected
                        && inv.Status == InvoiceRejected
                        && det.Status == InvoiceDetailNotClaimed
                    )
                select new
                {
                    Invoice = inv,
                    Claim = claim,
                    Detail = det,
                    Audit = aud,
                    Spbu = sp,
                    SurveyorName = usr.name != null ? usr.name : "-"
                };

            var isFixedApprover = FixedApprovalUsernames.Any(username =>
                string.Equals(username, currentUser, StringComparison.OrdinalIgnoreCase));

            if (!isFixedApprover && (userRegion.Any() || userSbm.Any()))
            {
                query = query.Where(x =>
                    (x.Spbu.region != null && userRegion.Contains(x.Spbu.region)) ||
                    (x.Spbu.sbm != null && userSbm.Contains(x.Spbu.sbm))
                );
            }

            var auditorOptions = await query
                .Where(x => x.Invoice.AppUserId != null && x.Invoice.AppUserId != "")
                .GroupBy(x => new
                {
                    Id = x.Invoice.AppUserId,
                    Name = x.SurveyorName
                })
                .Select(g => new InvoiceAuditorOptionVM
                {
                    Id = g.Key.Id,
                    Name = string.IsNullOrWhiteSpace(g.Key.Name) ? g.Key.Id : g.Key.Name
                })
                .OrderBy(x => x.Name)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(auditorId))
            {
                query = query.Where(x => x.Invoice.AppUserId == auditorId);
            }

            if (!string.IsNullOrWhiteSpace(periodClaim))
            {
                if (DateTime.TryParseExact(
                    periodClaim + "-01",
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var monthDate))
                {
                    // claim_date di PostgreSQL kamu terbaca sebagai timestamptz.
                    // Npgsql wajib menerima parameter DateTime dengan Kind=Utc untuk timestamptz.
                    // Jangan pakai DateTimeKind.Unspecified di filter, karena akan error:
                    // Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'.
                    var monthStart = DateTime.SpecifyKind(monthDate.Date, DateTimeKind.Utc);
                    var monthEnd = monthStart.AddMonths(1);

                    query = query.Where(x =>
                        x.Claim.claim_date >= monthStart &&
                        x.Claim.claim_date < monthEnd
                    );
                }
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
                    ClaimCreatedDate = x.Claim.created_date,
                    ClaimDate = x.Claim.claim_date,
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
                    ClaimSubmittedDate = g.Key.ClaimCreatedDate,
                    SurveyorName = g.Key.SurveyorName,
                    TotalAmount =
                        (
                            _context.TrxClaimDetails
                                .Where(cd => cd.trx_claim_id == g.Key.ClaimId)
                                .Sum(cd => (decimal?)cd.amount) ?? 0
                        )
                        + g.Sum(x => x.Detail.AuditFee)
                        + g.Sum(x => x.Detail.LumpsumFee ?? 0m)
                });

            ViewBag.SummaryTotalInvoice = await grouped.CountAsync();
            ViewBag.SummaryTotalExpense = await grouped.SumAsync(x => (decimal?)x.TotalAmount) ?? 0m;
            ViewBag.SummaryApproved = await grouped.CountAsync(x => x.Status == ClaimApproved);
            ViewBag.SummaryPending = await grouped.CountAsync(x => x.Status == ClaimUnderReview || x.Status == ClaimPendingApproval);
            ViewBag.SummaryRejected = await grouped.CountAsync(x => x.Status == ClaimRejected);

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
            ViewBag.AuditorId = auditorId;
            ViewBag.PeriodClaim = periodClaim;
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;
            ViewBag.AuditorOptions = auditorOptions;

            return View(model);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Gagal load invoice: " + ex.Message;

            ViewBag.SearchTerm = searchTerm;
            ViewBag.AuditorId = auditorId;
            ViewBag.PeriodClaim = periodClaim;
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;
            ViewBag.AuditorOptions = new List<InvoiceAuditorOptionVM>();
            ViewBag.SummaryTotalInvoice = 0;
            ViewBag.SummaryTotalExpense = 0m;
            ViewBag.SummaryApproved = 0;
            ViewBag.SummaryPending = 0;
            ViewBag.SummaryRejected = 0;

            return View(new PaginationModel<InvoiceVM>
            {
                Items = new List<InvoiceVM>(),
                PageNumber = 1,
                PageSize = DefaultPageSize,
                TotalItems = 0
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Summary(string period = "")
    {
        if (!await HasFinanceReportingRoleAsync()) return Forbid();
        var result = await BuildMonthlySummaryAsync(period);
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> ExportSummary(string period = "")
    {
        if (!await HasFinanceReportingRoleAsync()) return Forbid();
        var result = await BuildMonthlySummaryAsync(period);
        var sb = new StringBuilder();
        sb.AppendLine("No Invoice,Nama Auditor,ID Auditor,Total Pendapatan (Rp)");
        foreach (var row in result.Items)
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(row.InvoiceNo), Csv(row.AuditorName), Csv(row.AuditorCode),
                Csv(row.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture))
            }));
        sb.AppendLine($"{Csv("TOTAL")},,,{Csv(result.Total.ToString("0.##", CultureInfo.InvariantCulture))}");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"invoice-summary-{result.Period}.csv");
    }

    private async Task<InvoiceMonthlySummaryVM> BuildMonthlySummaryAsync(string period)
    {
        // Finance cut-off 16 previous month - 15 current month.
        var jakarta = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, jakarta);
        var currentMonth = new DateTime(localNow.Year, localNow.Month, 1);
        if (!DateTime.TryParseExact(period + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodMonth))
            periodMonth = currentMonth;

        var startDay = periodMonth.AddMonths(-1).AddDays(15);
        var endExclusiveDay = periodMonth.AddDays(15);
        // Claim dates are stored as timestamptz, so Jakarta cut-off is converted to UTC.
        var start = TimeZoneInfo.ConvertTimeToUtc(startDay, jakarta);
        var end = TimeZoneInfo.ConvertTimeToUtc(endExclusiveDay, jakarta);

        var invoices = await (
            from claim in _context.TrxClaims.AsNoTracking()
            join inv in _context.TrxInvoices.AsNoTracking() on claim.trx_invoice_id equals inv.Id
            join user in _context.app_users.AsNoTracking() on inv.AppUserId equals user.id into users
            from user in users.DefaultIfEmpty()
            where claim.status == ClaimApproved && inv.Status == InvoiceCompleted
                && claim.claim_date >= start && claim.claim_date < end
            select new
            {
                inv.Id,
                inv.InvoiceNo,
                ClaimId = claim.id,
                AuditorName = user == null ? "-" : user.name,
                AuditorCode = user == null ? "" : user.auditor_code
            }).ToListAsync();

        var invoiceIds = invoices.Select(x => x.Id).ToList();
        var claimIds = invoices.Select(x => x.ClaimId).ToList();
        var audits = await _context.TrxInvoiceDetails.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.TrxInvoiceId) && x.Status == InvoiceDetailClaimed)
            .Select(x => new { x.TrxInvoiceId, x.AuditFee, x.LumpsumFee })
            .ToListAsync();
        var expenses = await _context.TrxClaimDetails.AsNoTracking()
            .Where(x => claimIds.Contains(x.trx_claim_id))
            .Select(x => new { x.trx_claim_id, x.amount })
            .ToListAsync();

        var vm = new InvoiceMonthlySummaryVM
        {
            Period = periodMonth.ToString("yyyy-MM"),
            Start = startDay,
            EndExclusive = endExclusiveDay,
            Items = invoices.Select(inv => new InvoiceMonthlySummaryLineVM
            {
                InvoiceId = inv.Id,
                InvoiceNo = inv.InvoiceNo,
                AuditorName = inv.AuditorName ?? "-",
                AuditorCode = inv.AuditorCode ?? "",
                TotalAmount = audits.Where(x => x.TrxInvoiceId == inv.Id)
                    .Sum(x => x.AuditFee + (x.LumpsumFee ?? 0m))
                    + expenses.Where(x => x.trx_claim_id == inv.ClaimId).Sum(x => x.amount)
            }).OrderBy(x => x.AuditorName).ThenBy(x => x.InvoiceNo).ToList()
        };
        return vm;
    }

    public async Task<IActionResult> Detail(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var activeClaimStatus = await _context.TrxClaims
            .AsNoTracking()
            .Where(x => x.trx_invoice_id == id && x.status != "DELETED")
            .OrderByDescending(x => x.created_date)
            .Select(x => x.status)
            .FirstOrDefaultAsync();

        if (activeClaimStatus == ClaimUnderReview || activeClaimStatus == ClaimPendingApproval)
        {
            // New imports already have this flow. This also self-heals old
            // unprocessed invoices that do not yet have the fixed-user flow.
            await EnsureFixedApprovalFlowAsync(id);
        }

        var vm = await BuildInvoiceDetailViewModel(id, setViewBag: true);

        if (vm == null)
            return NotFound();

        return View(vm);
    }

    public async Task<IActionResult> Preview(string id, bool print = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var previewClaimStatus = await _context.TrxClaims
            .AsNoTracking()
            .Where(x => x.trx_invoice_id == id && x.status != "DELETED")
            .OrderByDescending(x => x.created_date)
            .Select(x => x.status)
            .FirstOrDefaultAsync();

        if (previewClaimStatus == ClaimUnderReview || previewClaimStatus == ClaimPendingApproval)
            await EnsureFixedApprovalFlowAsync(id);

        var vm = await BuildInvoiceDetailViewModel(id, setViewBag: true);

        if (vm == null)
            return NotFound();

        ViewBag.AutoPrint = print;

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(string id, InvoiceStartProcessVM model)
    {
        return await StartProcess(id, model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartProcess(string id, InvoiceStartProcessVM model)
    {
        // Flow lama Finance -> Proses sudah dinonaktifkan.
        // Approval flow fixed user dibuat otomatis saat import invoice.
        // Setelah final submit mobile menghasilkan claim UNDER_REVIEW,
        // invoice langsung menunggu approval Level 1.
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        await Task.CompletedTask;
        TempData["Error"] =
            "Flow Proses Finance sudah dinonaktifkan. Invoice dari mobile langsung masuk approval Level 1.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    // An append-only audit record is stored in the same transaction as each Finance edit.
    private Task LogFinanceEditAsync(string invoiceId, string claimId, string itemType,
        string itemId, object before, object? after)
    {
        var previousJson = JsonSerializer.Serialize(before);
        var newJson = JsonSerializer.Serialize(after);
        var user = GetCurrentUser();
        return _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO public.trx_invoice_finance_change_log
                (id, invoice_id, claim_id, item_type, item_id,
                 before_value, after_value, changed_by, changed_date)
            VALUES ({Guid.NewGuid().ToString()}, {invoiceId}, {claimId},
                {itemType}, {itemId}, CAST({previousJson} AS jsonb),
                CAST({newJson} AS jsonb), {user}, CURRENT_TIMESTAMP);");
    }

    // Finance-only adjustments are permitted before Level 1 has approved.
    // The auditor's submitted claim remains UNDER_REVIEW; subsequent approval reads the revised figures.
    private async Task<(trx_claim? claim, string? error)> GetEditableClaimAsync(string invoiceId)
    {
        if (!await HasRoleAsync("Finance"))
            return (null, "Hanya Finance yang dapat mengubah rincian klaim.");
        var invoice = await _context.TrxInvoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == invoiceId);
        if (invoice == null || invoice.Status != InvoiceInProgress)
            return (null, "Invoice tidak dalam status proses Finance.");
        var claim = await _context.TrxClaims
            .Where(x => x.trx_invoice_id == invoiceId && x.status != "DELETED")
            .OrderByDescending(x => x.created_date).FirstOrDefaultAsync();
        if (claim?.status != ClaimUnderReview)
            return (null, "Klaim hanya dapat diubah selama UNDER_REVIEW sebelum approval.");
        var steps = await _context.TrxInvoiceApprovalFlows.AsNoTracking()
            .Where(x => x.TrxInvoiceId == invoiceId).ToListAsync();
        if (steps.Count == 0 || steps.Any(x => x.Status != ApprovalFlowPending || x.ActionDate != null))
            return (null, "Approval sudah dimulai. Edit ditutup.");
        if (await _context.TrxInvoiceApprovals.AsNoTracking().AnyAsync(x => x.TrxInvoiceId == invoiceId))
            return (null, "Invoice memiliki riwayat approval. Tidak dapat diedit.");
        return (claim, null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateClaimExpense(string id, string expenseId, DateOnly expenseDate, decimal amount)
    {
        await using var financeTransaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var (claim, error) = await GetEditableClaimAsync(id);
        if (error != null) { TempData["Error"] = error; return RedirectToAction(nameof(Detail), new { id }); }
        if (expenseDate == default || expenseDate.Year < 2000 || amount < 0 || amount > 100000000m)
        {
            TempData["Error"] = "Tanggal atau nominal expense tidak valid.";
            return RedirectToAction(nameof(Detail), new { id });
        }
        var item = await _context.TrxClaimDetails
            .FirstOrDefaultAsync(x => x.id == expenseId && x.trx_claim_id == claim!.id);
        if (item == null) return NotFound();
        await LogFinanceEditAsync(id, claim!.id, "EXPENSE_UPDATE", item.id,
            new { item.expense_date, item.amount }, new { expense_date = expenseDate, amount });
        item.expense_date = expenseDate;
        item.amount = amount;
        item.updated_by = GetCurrentUser();
        item.updated_date = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        NormalizeDateTimesForPostgres();
        await _context.SaveChangesAsync();
        await financeTransaction.CommitAsync();
        TempData["Success"] = "Tanggal dan nominal expense diperbarui. Total invoice dihitung ulang.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteClaimExpense(string id, string expenseId)
    {
        await using var financeTransaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var (claim, error) = await GetEditableClaimAsync(id);
        if (error != null) { TempData["Error"] = error; return RedirectToAction(nameof(Detail), new { id }); }
        var item = await _context.TrxClaimDetails
            .FirstOrDefaultAsync(x => x.id == expenseId && x.trx_claim_id == claim!.id);
        if (item == null) return NotFound();
        // Keep uploaded media for forensic/audit investigation; deleting one expense
        // must never delete another expense's evidence from the same category.
        await LogFinanceEditAsync(id, claim!.id, "EXPENSE_DELETE", item.id,
            new { item.expense_date, item.amount, item.description, item.claim_item_type }, (object?)null);
        _context.TrxClaimDetails.Remove(item);
        await _context.SaveChangesAsync();
        await financeTransaction.CommitAsync();
        TempData["Success"] = "Expense dihapus dari klaim. Evidence tetap tersimpan.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInvoiceItem(string id, string detailId, decimal auditFee, decimal lumpsumFee)
    {
        await using var financeTransaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var (_, error) = await GetEditableClaimAsync(id);
        if (error != null) { TempData["Error"] = error; return RedirectToAction(nameof(Detail), new { id }); }
        if (auditFee < 0 || lumpsumFee < 0 || auditFee > 100000000m || lumpsumFee > 100000000m)
        {
            TempData["Error"] = "Nominal audit fee / lumpsum tidak valid.";
            return RedirectToAction(nameof(Detail), new { id });
        }
        var item = await _context.TrxInvoiceDetails
            .FirstOrDefaultAsync(x => x.Id == detailId && x.TrxInvoiceId == id && x.Status == InvoiceDetailInProgress);
        if (item == null) return NotFound();
        await LogFinanceEditAsync(id, "", "INVOICE_ITEM_UPDATE", item.Id,
            new { item.AuditFee, item.LumpsumFee, item.IsDeleted },
            new { AuditFee = auditFee, LumpsumFee = lumpsumFee, IsDeleted = false });
        item.AuditFee = auditFee;
        item.LumpsumFee = lumpsumFee;
        item.IsDeleted = false;
        item.UpdatedBy = GetCurrentUser();
        item.UpdatedDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        NormalizeDateTimesForPostgres();
        await _context.SaveChangesAsync();
        await financeTransaction.CommitAsync();
        TempData["Success"] = "Audit fee dan lumpsum diperbarui.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInvoiceItem(string id, string detailId)
    {
        await using var financeTransaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var (_, error) = await GetEditableClaimAsync(id);
        if (error != null) { TempData["Error"] = error; return RedirectToAction(nameof(Detail), new { id }); }
        var item = await _context.TrxInvoiceDetails
            .FirstOrDefaultAsync(x => x.Id == detailId && x.TrxInvoiceId == id && x.Status == InvoiceDetailInProgress);
        if (item == null) return NotFound();
        await LogFinanceEditAsync(id, "", "INVOICE_ITEM_DELETE", item.Id,
            new { item.AuditFee, item.LumpsumFee, item.IsDeleted },
            new { AuditFee = 0m, LumpsumFee = 0m, IsDeleted = true });
        item.IsDeleted = true;
        item.AuditFee = 0m;
        item.LumpsumFee = 0m;
        item.UpdatedBy = GetCurrentUser();
        item.UpdatedDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        NormalizeDateTimesForPostgres();
        await _context.SaveChangesAsync();
        await financeTransaction.CommitAsync();
        TempData["Success"] = "Item SPBU dinonaktifkan dari tagihan (audit tetap terlacak).";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id, InvoiceApprovalPostVM model)
    {
        var invoiceId = ResolveInvoiceId(id, model);

        try
        {
            if (string.IsNullOrWhiteSpace(invoiceId))
                return NotFound();

            var invoice = await _context.TrxInvoices
                .FirstOrDefaultAsync(x => x.Id == invoiceId);

            if (invoice == null)
                return NotFound();

            var claim = await _context.TrxClaims
                .Where(x => x.trx_invoice_id == invoiceId && x.status != "DELETED")
                .OrderByDescending(x => x.created_date)
                .FirstOrDefaultAsync();

            if (claim == null)
            {
                TempData["Error"] = "Data claim tidak ditemukan.";
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

            if (invoice.Status != InvoiceInProgress ||
                invoiceDetails.Any(x => x.Status != InvoiceDetailInProgress) ||
                (claim.status != ClaimUnderReview && claim.status != ClaimPendingApproval))
            {
                TempData["Error"] = "Invoice belum berada pada tahap approval.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            await EnsureFixedApprovalFlowAsync(invoiceId);

            var flowSteps = await _context.TrxInvoiceApprovalFlows
                .Where(x => x.TrxInvoiceId == invoiceId)
                .OrderBy(x => x.ApprovalLevel)
                .ToListAsync();

            if (flowSteps.Count == 0)
            {
                TempData["Error"] = "Invoice belum memiliki approval flow fixed user.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            var pendingStep = flowSteps
                .FirstOrDefault(x => x.Status == ApprovalFlowPending);

            string? currentUserId = null;

            if (flowSteps.Any())
            {
                if (pendingStep == null)
                {
                    TempData["Error"] = "Tidak ada level approval yang sedang menunggu.";
                    return RedirectToAction(nameof(Detail), new { id = invoiceId });
                }

                currentUserId = await GetCurrentAppUserIdAsync();
                if (string.IsNullOrWhiteSpace(currentUserId) ||
                    !string.Equals(currentUserId, pendingStep.ApproverUserId, StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] =
                        $"Invoice saat ini menunggu approval level {pendingStep.ApprovalLevel} oleh approver yang telah dipilih.";
                    return RedirectToAction(nameof(Detail), new { id = invoiceId });
                }
            }

            // Mobile submit langsung ke approval. Nilai claim divalidasi dan
            // fee audit/lumpsum diseed otomatis sebelum approver pertama bertindak.
            var expenseValidationError = await ValidateExpenseClaimsAsync(claim.id);
            if (expenseValidationError != null)
            {
                TempData["Error"] = expenseValidationError;
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            await SeedInvoiceFeesFromAuditsAsync(invoiceDetails);

            var now = DateTime.Now;
            var currentUser = GetCurrentUser();

            if (pendingStep != null)
            {
                pendingStep.Status = ApprovalFlowApproved;
                pendingStep.ActionBy = currentUser;
                pendingStep.ActionDate = now;
                pendingStep.SignaturePath = await GetUserSignaturePathAsync(currentUserId);
                pendingStep.UpdatedBy = currentUser;
                pendingStep.UpdatedDate = now;
            }

            var approvalAction = pendingStep == null
                ? "APPROVED"
                : $"APPROVED_LEVEL_{pendingStep.ApprovalLevel}";

            var approvalSnapshot = await BuildApprovalSnapshot(
                invoice,
                claim,
                invoiceDetails,
                approvalAction,
                null
            );

            _context.TrxInvoiceApprovals.Add(approvalSnapshot);

            var hasNextPending = flowSteps.Any(x =>
                x != pendingStep &&
                x.Status == ApprovalFlowPending);

            if (flowSteps.Any() && hasNextPending)
            {
                // Intermediate approval: claim moves/stays PENDING_APPROVAL until final level.
                invoice.Status = InvoiceInProgress;
                invoice.UpdatedBy = currentUser;
                invoice.UpdatedDate = now;

                claim.status = ClaimPendingApproval;
                claim.updated_by = currentUser;
                claim.updated_date = now;

                foreach (var detail in invoiceDetails)
                {
                    detail.Status = InvoiceDetailInProgress;
                    detail.UpdatedBy = currentUser;
                    detail.UpdatedDate = now;
                }

                NormalizeDateTimesForPostgres();
                await _context.SaveChangesAsync();

                var nextStep = flowSteps
                    .Where(x => x.Status == ApprovalFlowPending)
                    .OrderBy(x => x.ApprovalLevel)
                    .First();

                TempData["Success"] =
                    $"Approval level {pendingStep!.ApprovalLevel} berhasil. Lanjut ke level {nextStep.ApprovalLevel}.";

                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            // Final approval Level 3.
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
                // Final status is derived from the auto-seeded invoice amounts.
                var isDeleted = detail.IsDeleted || (detail.AuditFee == 0m && (detail.LumpsumFee ?? 0m) == 0m);

                detail.Status = isDeleted
                    ? InvoiceDetailNotClaimed
                    : InvoiceDetailClaimed;

                detail.UpdatedBy = currentUser;
                detail.UpdatedDate = now;
            }

            NormalizeDateTimesForPostgres();
            await _context.SaveChangesAsync();

            TempData["Success"] =
                "Seluruh level approval selesai. Invoice berhasil disetujui.";

            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Gagal approve invoice: " + ex.Message;

            if (ex.InnerException != null)
                TempData["Error"] += " | Inner: " + ex.InnerException.Message;

            return RedirectToAction(nameof(Detail), new { id = invoiceId ?? id ?? model?.Id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(string id, InvoiceApprovalPostVM model)
    {
        var invoiceId = ResolveInvoiceId(id, model);

        try
        {
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
                .Where(x => x.trx_invoice_id == invoiceId && x.status != "DELETED")
                .OrderByDescending(x => x.created_date)
                .FirstOrDefaultAsync();

            if (claim == null)
            {
                TempData["Error"] = "Data claim tidak ditemukan.";
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

            if (invoice.Status != InvoiceInProgress ||
                invoiceDetails.Any(x => x.Status != InvoiceDetailInProgress) ||
                (claim.status != ClaimUnderReview && claim.status != ClaimPendingApproval))
            {
                TempData["Error"] = "Invoice belum berada pada tahap approval.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            await EnsureFixedApprovalFlowAsync(invoiceId);

            var flowSteps = await _context.TrxInvoiceApprovalFlows
                .Where(x => x.TrxInvoiceId == invoiceId)
                .OrderBy(x => x.ApprovalLevel)
                .ToListAsync();

            if (flowSteps.Count == 0)
            {
                TempData["Error"] = "Invoice belum memiliki approval flow fixed user.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            var pendingStep = flowSteps
                .FirstOrDefault(x => x.Status == ApprovalFlowPending);

            string? currentUserId = null;

            if (flowSteps.Any())
            {
                if (pendingStep == null)
                {
                    TempData["Error"] = "Tidak ada level approval yang sedang menunggu.";
                    return RedirectToAction(nameof(Detail), new { id = invoiceId });
                }

                currentUserId = await GetCurrentAppUserIdAsync();
                if (string.IsNullOrWhiteSpace(currentUserId) ||
                    !string.Equals(currentUserId, pendingStep.ApproverUserId, StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] =
                        $"Invoice saat ini menunggu approval level {pendingStep.ApprovalLevel} oleh approver yang telah dipilih.";
                    return RedirectToAction(nameof(Detail), new { id = invoiceId });
                }
            }

            // Reject dapat dilakukan langsung sejak claim UNDER_REVIEW oleh
            // user fixed yang sedang mendapat giliran approval.

            var now = DateTime.Now;
            var currentUser = GetCurrentUser();
            var rejectionReason = model.RejectionReason.Trim();

            if (pendingStep != null)
            {
                pendingStep.Status = ApprovalFlowRejected;
                pendingStep.ActionBy = currentUser;
                pendingStep.ActionDate = now;
                pendingStep.RejectionReason = rejectionReason;
                pendingStep.SignaturePath = await GetUserSignaturePathAsync(currentUserId);
                pendingStep.UpdatedBy = currentUser;
                pendingStep.UpdatedDate = now;

                foreach (var future in flowSteps.Where(x =>
                    x.Status == ApprovalFlowPending &&
                    x.ApprovalLevel > pendingStep.ApprovalLevel))
                {
                    future.Status = ApprovalFlowCancelled;
                    future.UpdatedBy = currentUser;
                    future.UpdatedDate = now;
                }
            }

            var approval = await BuildApprovalSnapshot(
                invoice,
                claim,
                invoiceDetails,
                pendingStep == null
                    ? "REJECTED"
                    : $"REJECTED_LEVEL_{pendingStep.ApprovalLevel}",
                rejectionReason
            );

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

            NormalizeDateTimesForPostgres();
            await _context.SaveChangesAsync();

            TempData["Success"] = pendingStep == null
                ? "Invoice berhasil ditolak."
                : $"Invoice ditolak pada approval level {pendingStep.ApprovalLevel}.";

            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Gagal reject invoice: " + ex.Message;

            if (ex.InnerException != null)
                TempData["Error"] += " | Inner: " + ex.InnerException.Message;

            return RedirectToAction(nameof(Detail), new { id = invoiceId ?? id ?? model?.Id });
        }
    }

    public async Task<IActionResult> Export(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var exportClaimStatus = await _context.TrxClaims
            .AsNoTracking()
            .Where(x => x.trx_invoice_id == id && x.status != "DELETED")
            .OrderByDescending(x => x.created_date)
            .Select(x => x.status)
            .FirstOrDefaultAsync();

        if (exportClaimStatus == ClaimUnderReview || exportClaimStatus == ClaimPendingApproval)
            await EnsureFixedApprovalFlowAsync(id);

        var vm = await BuildInvoiceDetailViewModel(id, setViewBag: true);

        if (vm == null)
            return NotFound();

        var claimExpenseAmounts = ViewBag.ClaimExpenseAmounts as Dictionary<string, decimal>
            ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        decimal ExpenseAmount(string key) =>
            claimExpenseAmounts.TryGetValue(key, out var amount) ? amount : 0m;

        string ApprovalName(int level, string fallback)
        {
            var step = vm.ApprovalSteps?.FirstOrDefault(x => x.ApprovalLevel == level);
            if (step == null)
                return fallback;

            return string.IsNullOrWhiteSpace(step.ApproverName)
                ? step.ApproverUsername
                : step.ApproverName;
        }

        string ApprovalDate(int level)
        {
            var step = vm.ApprovalSteps?.FirstOrDefault(x => x.ApprovalLevel == level);
            return step?.ActionDate?.ToString("dd/MM/yyyy") ?? "";
        }

        string ApprovalStatus(int level)
        {
            var step = vm.ApprovalSteps?.FirstOrDefault(x => x.ApprovalLevel == level);
            return step?.Status ?? "PENDING";
        }

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Invoice");

        const int lastColumn = 17; // A:Q - sama dengan tabel Preview.
        var yellow = XLColor.FromHtml("#FFF200");
        var lightGray = XLColor.FromHtml("#F3F4F6");

        ws.Style.Font.FontName = "Arial";
        ws.Style.Font.FontSize = 9;
        ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // ===== Header =====
        ws.Range(1, 1, 2, 6).Merge();
        ws.Cell(1, 1).Value = "PT Citrabuana Indoloka";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        ws.Range(3, 1, 3, 6).Merge();
        ws.Cell(3, 1).Value = "In association with Intertek";
        ws.Cell(3, 1).Style.Font.FontSize = 8;

        ws.Range(1, 7, 3, 12).Merge();
        ws.Cell(1, 7).Value = "INVOICE";
        ws.Cell(1, 7).Style.Font.Bold = true;
        ws.Cell(1, 7).Style.Font.FontSize = 20;
        ws.Cell(1, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        ws.Range(1, 13, 3, 17).Merge();
        ws.Cell(1, 13).Value = $"INVOICE NO. : {vm.InvoiceNo}";
        ws.Cell(1, 13).Style.Fill.BackgroundColor = yellow;
        ws.Cell(1, 13).Style.Font.Bold = true;
        ws.Cell(1, 13).Style.Font.FontSize = 9;
        ws.Cell(1, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 13).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var info = new[]
        {
            ("Name (Auditor)", $"{vm.EmployeeName} (ID: {vm.AuditorCode})"),
            ("Period", vm.Period),
            ("Kind of Job", vm.Job),
            ("Homebase", vm.Homebase)
        };

        for (var i = 0; i < info.Length; i++)
        {
            var row = 5 + i;
            ws.Range(row, 1, row, 2).Merge();
            ws.Cell(row, 1).Value = info[i].Item1;
            ws.Cell(row, 1).Style.Font.Bold = true;

            ws.Range(row, 3, row, 7).Merge();
            ws.Cell(row, 3).Value = info[i].Item2 ?? "";
            ws.Range(row, 3, row, 7).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        ws.Range(5, 13, 5, 14).Merge();
        ws.Cell(5, 13).Value = "Status";
        ws.Cell(5, 13).Style.Font.Bold = true;
        ws.Range(5, 15, 5, 17).Merge();
        ws.Cell(5, 15).Value = ViewBag.ClaimStatusLabel?.ToString() ?? vm.Status;
        ws.Cell(5, 15).Style.Font.Bold = true;

        // ===== Main invoice table =====
        const int header1 = 10;
        const int header2 = 11;
        ws.Range(header1, 1, header2, 1).Merge().Value = "Nomor";
        ws.Range(header1, 2, header2, 2).Merge().Value = "Date";
        ws.Range(header1, 3, header2, 5).Merge().Value = "Details of Expense (Purpose, staff or others present)";
        ws.Range(header1, 6, header2, 6).Merge().Value = "Contract / Cost Centre";
        ws.Range(header1, 7, header2, 8).Merge().Value = "TOTAL EXPENSE";
        ws.Range(header1, 9, header1, 17).Merge().Value = "Descriptions";

        ws.Cell(header2, 9).Value = "Audit Fee";
        ws.Cell(header2, 10).Value = "Lumpsum";
        ws.Range(header2, 11, header2, 12).Merge().Value = "BPJS Kesehatan";
        ws.Range(header2, 13, header2, 14).Merge().Value = "BPJS Ketenagakerjaan";
        ws.Cell(header2, 15).Value = "Vehicle Maintenance";
        ws.Cell(header2, 16).Value = "Internet Data";
        ws.Cell(header2, 17).Value = "Others";

        var headerRange = ws.Range(header1, 1, header2, lastColumn);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Fill.BackgroundColor = lightGray;
        SetAllBorders(headerRange);

        var lines = vm.PrintLines ?? new List<InvoicePrintLineVM>();
        var displayRows = Math.Max(lines.Count, 12);
        var firstDataRow = 12;

        for (var i = 0; i < displayRows; i++)
        {
            var row = firstDataRow + i;
            var line = i < lines.Count ? lines[i] : null;

            if (line != null)
            {
                ws.Cell(row, 1).Value = i + 1;
                ws.Cell(row, 2).Value = line.Date;
                ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy";
                ws.Range(row, 3, row, 5).Merge().Value = line.Description ?? "";
                ws.Cell(row, 6).Value = "102";
                ws.Range(row, 7, row, 8).Merge().Value = line.Total;
                ws.Cell(row, 9).Value = line.AuditFee;
                ws.Cell(row, 10).Value = line.Lumpsum;
                ws.Range(row, 11, row, 12).Merge().Value = line.BpjsKesehatan;
                ws.Range(row, 13, row, 14).Merge().Value = line.BpjsKetenagakerjaan;
                ws.Cell(row, 15).Value = line.VehicleMaintenance;
                ws.Cell(row, 16).Value = line.Internet;
                ws.Cell(row, 17).Value = line.Other;

                foreach (var col in new[] { 7, 9, 10, 11, 13, 15, 16, 17 })
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
            }
            else
            {
                ws.Range(row, 3, row, 5).Merge();
                ws.Range(row, 7, row, 8).Merge();
                ws.Range(row, 11, row, 12).Merge();
                ws.Range(row, 13, row, 14).Merge();
            }

            ws.Row(row).Height = 16;
            var dataRange = ws.Range(row, 1, row, lastColumn);
            dataRange.Style.Alignment.WrapText = true;
            SetAllBorders(dataRange);
        }

        var summaryRow = firstDataRow + displayRows;

        // Brought forward
        ws.Range(summaryRow, 3, summaryRow, 6).Merge().Value = "Brought forward from continuation sheet";
        SetAllBorders(ws.Range(summaryRow, 1, summaryRow, lastColumn));

        // Totals
        summaryRow++;
        ws.Range(summaryRow, 3, summaryRow, 6).Merge().Value = "TOTAL / CARRIED FORWARD";
        ws.Range(summaryRow, 7, summaryRow, 8).Merge().Value = vm.TotalExpense;
        ws.Cell(summaryRow, 9).Value = vm.TotalAuditFee;
        ws.Cell(summaryRow, 10).Value = vm.TotalLumpsum;
        ws.Range(summaryRow, 11, summaryRow, 12).Merge().Value = ExpenseAmount("BPJS_KESEHATAN");
        ws.Range(summaryRow, 13, summaryRow, 14).Merge().Value = ExpenseAmount("BPJS_KETENAGAKERJAAN");
        ws.Cell(summaryRow, 15).Value = ExpenseAmount("VEHICLE_MAINTENANCE");
        ws.Cell(summaryRow, 16).Value = ExpenseAmount("INTERNET");
        ws.Cell(summaryRow, 17).Value = ExpenseAmount("OTHER");
        ws.Range(summaryRow, 1, summaryRow, lastColumn).Style.Font.Bold = true;
        foreach (var col in new[] { 7, 9, 10, 11, 13, 15, 16, 17 })
            ws.Cell(summaryRow, col).Style.NumberFormat.Format = "#,##0";
        SetAllBorders(ws.Range(summaryRow, 1, summaryRow, lastColumn));

        var amountDue = vm.TotalExpense - vm.LessDirectCharges - vm.LessAdvances;
        foreach (var summary in new[]
        {
            ("LESS DIRECT CHARGES", vm.LessDirectCharges),
            ("LESS ADVANCES", vm.LessAdvances),
            ("AMOUNT DUE TO/FROM EMPLOYEE", amountDue)
        })
        {
            summaryRow++;
            ws.Range(summaryRow, 3, summaryRow, 6).Merge().Value = summary.Item1;
            ws.Range(summaryRow, 7, summaryRow, 8).Merge().Value = summary.Item2;
            ws.Range(summaryRow, 3, summaryRow, 8).Style.Font.Bold = true;
            ws.Cell(summaryRow, 7).Style.NumberFormat.Format = "#,##0";
            SetAllBorders(ws.Range(summaryRow, 1, summaryRow, lastColumn));
        }

        // ===== Statement / notes =====
        var statementRow = summaryRow + 2;
        ws.Range(statementRow, 10, statementRow, 17).Merge().Value = "Report Statement";
        ws.Range(statementRow, 10, statementRow, 17).Style.Font.Bold = true;
        ws.Range(statementRow + 1, 10, statementRow + 3, 17).Merge().Value =
            "The expenses submitted herein are a true and accurate record of prudent costs necessarily incurred on behalf of Intertek. " +
            "These costs are supported by original receipts wherever possible and have not previously been reimbursed.";
        ws.Range(statementRow + 1, 10, statementRow + 3, 17).Style.Alignment.WrapText = true;
        ws.Range(statementRow + 1, 10, statementRow + 3, 17).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var notesRow = statementRow + 5;
        ws.Range(notesRow, 1, notesRow, 8).Merge().Value = "Claimant";
        ws.Range(notesRow, 1, notesRow, 8).Style.Font.Bold = true;
        ws.Range(notesRow + 1, 1, notesRow + 2, 8).Merge().Value =
            "Receipts/invoices for the above are properly documented, attached and cross-referenced.";
        ws.Range(notesRow + 1, 1, notesRow + 2, 8).Style.Alignment.WrapText = true;

        ws.Range(notesRow + 3, 1, notesRow + 3, 8).Merge().Value = "Accountant / Authoriser";
        ws.Range(notesRow + 3, 1, notesRow + 3, 8).Style.Font.Bold = true;
        ws.Range(notesRow + 4, 1, notesRow + 5, 8).Merge().Value =
            "Checked calculations, reasonableness, receipts, dates and supporting documents. Approver may approve or reject the claim.";
        ws.Range(notesRow + 4, 1, notesRow + 5, 8).Style.Alignment.WrapText = true;

        // ===== Bank detail =====
        var bottomRow = notesRow + 7;
        ws.Range(bottomRow, 1, bottomRow, 6).Merge().Value = "Please transfer to:";
        ws.Range(bottomRow, 1, bottomRow, 6).Style.Font.Bold = true;
        ws.Range(bottomRow, 1, bottomRow, 6).Style.Font.Underline = XLFontUnderlineValues.Single;

        var bankRows = new[]
        {
            ("NAME", vm.BankOwner ?? ""),
            ("Bank", vm.BankName ?? ""),
            ("A/C No.", vm.BankAccount ?? "")
        };
        for (var i = 0; i < bankRows.Length; i++)
        {
            var row = bottomRow + 1 + i;
            ws.Range(row, 1, row, 2).Merge().Value = bankRows[i].Item1;
            ws.Range(row, 3, row, 6).Merge().Value = bankRows[i].Item2;
            ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = yellow;
            ws.Range(row, 1, row, 2).Style.Font.Bold = true;
            SetAllBorders(ws.Range(row, 1, row, 6));
        }

        // ===== Signatures / approval =====
        var signatureStart = bottomRow;
        var signatureBlocks = new[]
        {
            (Name: vm.EmployeeName ?? "-", Role: "Requestor", Date: vm.ClaimDate.ToString("dd/MM/yyyy"), Status: "SUBMITTED"),
            (Name: ApprovalName(1, "tri.handayani"), Role: "Approval Level 1", Date: ApprovalDate(1), Status: ApprovalStatus(1)),
            (Name: ApprovalName(2, "rommy.irawan"), Role: "Approval Level 2", Date: ApprovalDate(2), Status: ApprovalStatus(2)),
            (Name: ApprovalName(3, "sabar.kembaren"), Role: "Approval Level 3", Date: ApprovalDate(3), Status: ApprovalStatus(3))
        };

        for (var i = 0; i < signatureBlocks.Length; i++)
        {
            var block = signatureBlocks[i];
            var isLeft = i % 2 == 0;
            var pairRow = signatureStart + (i / 2) * 4;
            var startCol = isLeft ? 9 : 14;
            var endCol = isLeft ? 13 : 17;

            ws.Range(pairRow, startCol, pairRow, endCol).Merge().Value = block.Name;
            ws.Range(pairRow, startCol, pairRow, endCol).Style.Font.Bold = true;
            ws.Range(pairRow, startCol, pairRow, endCol).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(pairRow + 1, startCol, pairRow + 1, endCol).Merge().Value = block.Role;
            ws.Range(pairRow + 2, startCol, pairRow + 2, endCol).Merge().Value =
                string.IsNullOrWhiteSpace(block.Date) ? block.Status : $"{block.Status} · {block.Date}";
            ws.Range(pairRow + 2, startCol, pairRow + 2, endCol).Style.Font.FontSize = 8;
        }

        // ===== Widths / print =====
        ws.Column(1).Width = 6;
        ws.Column(2).Width = 11;
        ws.Column(3).Width = 15;
        ws.Column(4).Width = 15;
        ws.Column(5).Width = 15;
        ws.Column(6).Width = 10;
        ws.Column(7).Width = 5;
        ws.Column(8).Width = 12;
        ws.Column(9).Width = 12;
        ws.Column(10).Width = 12;
        ws.Column(11).Width = 8;
        ws.Column(12).Width = 10;
        ws.Column(13).Width = 8;
        ws.Column(14).Width = 10;
        ws.Column(15).Width = 14;
        ws.Column(16).Width = 12;
        ws.Column(17).Width = 12;

        ws.Rows(1, 3).Height = 20;
        ws.Row(1).Height = 26;
        ws.Row(header1).Height = 24;
        ws.Row(header2).Height = 30;

        ws.ShowGridLines = false;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        // Keep each printed Excel page readable instead of shrinking all rows to one A4.
        // The final page reserves space for brought-forward, bank and signatures.
        ws.PageSetup.FitToPages(1, 0);
        if (lines.Count > 18)
        {
            var printed = 0;
            while (lines.Count - printed > 18)
            {
                printed += Math.Min(30, lines.Count - printed - 18);
                ws.PageSetup.AddHorizontalPageBreak(firstDataRow + printed);
            }
        }
        ws.PageSetup.Margins.Left = 0.25;
        ws.PageSetup.Margins.Right = 0.25;
        ws.PageSetup.Margins.Top = 0.30;
        ws.PageSetup.Margins.Bottom = 0.30;
        ws.PageSetup.CenterHorizontally = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"invoice-{SanitizeFileName(vm.InvoiceNo)}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void SetAllBorders(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    public IActionResult DownloadPdf(string id)
    {
        return RedirectToAction(nameof(Preview), new { id, print = true });
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
            ClaimUnderReview => "Menunggu Approval",
            ClaimPendingApproval => "Menunggu Approval",
            ClaimApproved => "Disetujui",
            ClaimRejected => "Ditolak",
            _ => status ?? "-"
        };
    }

    private async Task<InvoiceDetailViewModel> BuildInvoiceDetailViewModel(string id, bool setViewBag)
    {
        var invoice = await _context.TrxInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (invoice == null)
            return null;

        var claim = await _context.TrxClaims
            .AsNoTracking()
            .Where(x => x.trx_invoice_id == id && x.status != "DELETED")
            .OrderByDescending(x => x.created_date)
            .FirstOrDefaultAsync();

        if (claim == null)
            return null;

        var claimExpenseDetails = await _context.TrxClaimDetails
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .OrderBy(x => x.created_date)
            .Select(x => new
            {
                x.id,
                x.claim_item_type,
                x.description,
                x.expense_date,
                x.created_date,
                amount = (decimal?)x.amount ?? 0m
            })
            .ToListAsync();

        var claimExpenseAmount = claimExpenseDetails.Sum(x => x.amount);

        var claimExpenseAmounts = claimExpenseDetails
            .GroupBy(x => GetClaimExpenseCategory(
                string.IsNullOrWhiteSpace(x.claim_item_type) ? x.description : x.claim_item_type))
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.amount),
                StringComparer.OrdinalIgnoreCase
            );

        var rawDetails = await (
            from det in _context.TrxInvoiceDetails.AsNoTracking()
            join aud in _context.trx_audits.AsNoTracking()
                on det.TrxAuditId equals aud.id
            join sp in _context.spbus.AsNoTracking()
                on aud.spbu_id equals sp.id
            where det.TrxInvoiceId == id
            orderby aud.audit_execution_time ascending,
                    aud.audit_schedule_date ascending,
                    det.CreatedDate ascending
            select new
            {
                TrxInvoiceDetailId = det.Id,
                TrxAuditId = det.TrxAuditId,
                AuditDate = aud.audit_execution_time
                    ?? (
                        aud.audit_schedule_date.HasValue
                            ? aud.audit_schedule_date.Value.ToDateTime(TimeOnly.MinValue)
                            : aud.created_date
                    ),
                Description = "Audit SPBU " + sp.spbu_no + (sp.address != null && sp.address != "" ? ", " + sp.address : ""),
                IsDeleted = det.IsDeleted,
                AuditFee = det.AuditFee,
                LumpsumFee = det.LumpsumFee,
                DistanceKm = aud.km_range
            }
        ).ToListAsync();

        // When legacy lines have NULL lumpsum, only one auto-lumpsum per calendar date
        // *within this invoice*. Existing explicit finance adjustments remain untouched.
        var autoLumpsumDays = rawDetails
            .Where(x => !x.IsDeleted && (x.LumpsumFee ?? 0m) > 0m)
            .Select(x => DateOnly.FromDateTime(x.AuditDate))
            .ToHashSet();
        var details = rawDetails.Select(x =>
        {
            var distanceKm = x.DistanceKm;
            var day = DateOnly.FromDateTime(x.AuditDate);
            var lumpsum = x.IsDeleted ? 0m : x.LumpsumFee ??
                (GetLumpsumFeeByDistance(distanceKm) > 0m && autoLumpsumDays.Add(day)
                    ? 400000m : 0m);

            return new InvoiceDetailItemVM
            {
                TrxInvoiceDetailId = x.TrxInvoiceDetailId,
                TrxAuditId = x.TrxAuditId,
                Date = DateTime.SpecifyKind(x.AuditDate, DateTimeKind.Utc),
                Description = x.Description,
                DistanceKm = distanceKm,
                LumpsumEligible = distanceKm > 80m,
                IsDeleted = x.IsDeleted,
                ClaimAmount = 0,
                AuditFee = x.IsDeleted ? 0m : x.AuditFee,
                Lumpsum = lumpsum,
                Amount = (x.IsDeleted ? 0m : x.AuditFee) + lumpsum
            };
        }).ToList();

        var totalKmRange = details.Sum(x => x.DistanceKm);
        var totalAuditFee = details.Sum(x => x.AuditFee);
        var totalLumpsum = details.Sum(x => x.Lumpsum);
        var totalExpense = claimExpenseAmount + totalAuditFee + totalLumpsum;

        var attachments = await _context.TrxClaimMedias
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .ToListAsync();

        var attachmentGroups = attachments
            .Where(x => !string.IsNullOrWhiteSpace(x.claim_item_type))
            .GroupBy(x => GetClaimExpenseCategory(x.claim_item_type), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var latestApproval = await _context.TrxInvoiceApprovals
            .AsNoTracking()
            .Where(x => x.TrxInvoiceId == invoice.Id)
            .OrderByDescending(x => x.ApprovedDate)
            .FirstOrDefaultAsync();

        var approvalSteps = await (
            from flow in _context.TrxInvoiceApprovalFlows.AsNoTracking()
            join user in _context.app_users.AsNoTracking()
                on flow.ApproverUserId equals user.id
            where flow.TrxInvoiceId == invoice.Id
            orderby flow.ApprovalLevel
            select new InvoiceApprovalStepVM
            {
                Id = flow.Id,
                ApprovalLevel = flow.ApprovalLevel,
                ApproverUserId = flow.ApproverUserId,
                ApproverUsername = user.username,
                ApproverName = user.name,
                Status = flow.Status,
                ActionBy = flow.ActionBy,
                ActionDate = flow.ActionDate,
                RejectionReason = flow.RejectionReason,
                SignaturePath = flow.SignaturePath ?? user.signature_path
            }
        ).ToListAsync();

        var approvalUserOptions = await _context.app_users
            .AsNoTracking()
            .Where(x =>
                x.status == "ACTIVE" &&
                FixedApprovalUsernames.Contains(x.username))
            .OrderBy(x => x.username)
            .Select(x => new InvoiceApprovalUserOptionVM
            {
                Id = x.id,
                Username = x.username,
                Name = x.name
            })
            .ToListAsync();

        // Backward ViewModel compatibility only; no role selection/filtering.
        var approvalLevel1UserOptions = approvalUserOptions
            .Where(x => string.Equals(x.Username, FixedApprovalUsernames[0], StringComparison.OrdinalIgnoreCase))
            .ToList();

        var approvalLevel2UserOptions = approvalUserOptions
            .Where(x => string.Equals(x.Username, FixedApprovalUsernames[1], StringComparison.OrdinalIgnoreCase))
            .ToList();

        var currentPendingApproval = approvalSteps
            .FirstOrDefault(x => x.Status == ApprovalFlowPending);

        var currentAppUserId = await GetCurrentAppUserIdAsync();

        var canCurrentUserApprove =
            (claim.status == ClaimUnderReview || claim.status == ClaimPendingApproval) &&
            currentPendingApproval != null &&
            !string.IsNullOrWhiteSpace(currentAppUserId) &&
            string.Equals(
                currentPendingApproval.ApproverUserId,
                currentAppUserId,
                StringComparison.OrdinalIgnoreCase);

        var isProcessOrDone =
            claim.status == ClaimUnderReview ||
            claim.status == ClaimPendingApproval ||
            claim.status == ClaimApproved ||
            claim.status == ClaimRejected;

        var requestorUser = await GetRequestorUser(invoice.AppUserId, claim.app_user_id);

        var claimExpenses = claimExpenseDetails
            .Select(x => new InvoiceClaimExpenseVM
            {
                Id = x.id,
                Date = x.expense_date ?? DateOnly.FromDateTime(x.created_date),
                Type = x.claim_item_type ?? "",
                Description = x.description ?? x.claim_item_type ?? "Biaya lainnya",
                Amount = x.amount,
                EvidencePath = attachments
                    .Where(m => GetClaimExpenseCategory(m.claim_item_type) == GetClaimExpenseCategory(x.claim_item_type))
                    .Select(m => m.media_path)
                    .FirstOrDefault()
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Type)
            .ToList();

        var printLines = details.Where(x => !x.IsDeleted).Select(x => new InvoicePrintLineVM
        {
            Date = x.Date,
            Description = x.Description,
            AuditFee = x.AuditFee,
            Lumpsum = x.Lumpsum
        }).Concat(claimExpenses.Select(x =>
        {
            var category = GetClaimExpenseCategory(
                string.IsNullOrWhiteSpace(x.Type) ? x.Description : x.Type);
            return new InvoicePrintLineVM
            {
                Date = x.Date.ToDateTime(TimeOnly.MinValue),
                Description = x.Description,
                BpjsKesehatan = category == "BPJS_KESEHATAN" ? x.Amount : 0m,
                BpjsKetenagakerjaan = category == "BPJS_KETENAGAKERJAAN" ? x.Amount : 0m,
                VehicleMaintenance = category == "VEHICLE_MAINTENANCE" ? x.Amount : 0m,
                Internet = category == "INTERNET" ? x.Amount : 0m,
                Other = category == "OTHER" ? x.Amount : 0m
            };
        })).OrderBy(x => x.Date).ThenBy(x => x.Description).ToList();

        var vm = new InvoiceDetailViewModel
        {
            Id = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Status = invoice.Status,
            ClaimDate = claim.claim_date,

            EmployeeName = requestorUser.Name,
            RequestorSignaturePath = requestorUser.SignaturePath,

            Period = $"{invoice.InvoicePeriodStart:dd MMM yyyy} - {invoice.InvoicePeriodEnd:dd MMM yyyy}",
            Homebase = requestorUser.Homebase,
            AuditorCode = requestorUser.AuditorCode,
            ClaimExpenses = claimExpenses,
            ExpenseWarnings = GetExpenseWarnings(claimExpenseDetails.Select(x => (x.claim_item_type, x.description, x.amount, x.expense_date)).ToList()),
            CanFinanceEdit = claim.status == ClaimUnderReview && invoice.Status == InvoiceInProgress
                && approvalSteps.Count > 0
                && approvalSteps.All(x => x.Status == ApprovalFlowPending && x.ActionDate == null)
                && latestApproval == null && await HasRoleAsync("Finance"),
            PrintLines = printLines,
            TotalKmRange = totalKmRange,
            Job = "Audit SPBU",

            Items = details,

            ClaimExpenseAmount = claimExpenseAmount,
            TotalExpense = totalExpense,
            TotalAuditFee = totalAuditFee,
            TotalLumpsum = totalLumpsum,

            LessDirectCharges = 0,
            LessAdvances = 0,

            Attachments = attachments,
            AttachmentGroups = attachmentGroups,

            BankName = isProcessOrDone ? "Bank Mandiri" : "",
            BankAccount = isProcessOrDone ? "129383481" : "",
            BankOwner = string.IsNullOrWhiteSpace(requestorUser.Name) ? "-" : requestorUser.Name,

            ApprovedBy = latestApproval != null
                ? latestApproval.ApprovedBy
                : claim.status == ClaimApproved
                    ? "Finance / Accounting"
                    : null,

            ApprovedDate = latestApproval?.ApprovedDate,
            RejectionReason = latestApproval?.RejectionReason,

            ApprovalSteps = approvalSteps,
            ApprovalUserOptions = approvalUserOptions,
            ApprovalLevel1UserOptions = approvalLevel1UserOptions,
            ApprovalLevel2UserOptions = approvalLevel2UserOptions,
            CanCurrentUserApprove = canCurrentUserApprove,
            CurrentApprovalLevel = currentPendingApproval?.ApprovalLevel,
            CurrentApproverName = currentPendingApproval == null
                ? null
                : string.IsNullOrWhiteSpace(currentPendingApproval.ApproverName)
                    ? currentPendingApproval.ApproverUsername
                    : currentPendingApproval.ApproverName
        };

        if (setViewBag)
        {
            ViewBag.ClaimExpenseAmounts = claimExpenseAmounts;
            ViewBag.InvoiceStatus = invoice.Status;
            ViewBag.InvoiceStatusLabel = GetInvoiceStatusLabel(invoice.Status);
            ViewBag.ClaimStatus = claim.status;
            ViewBag.ClaimStatusLabel = GetClaimStatusLabel(claim.status);
        }

        return vm;
    }

    private static decimal GetLumpsumFeeByDistance(decimal kmRange)
    {
        return kmRange > 80 ? 400000m : 0m;
    }

    private static string GetClaimExpenseCategory(string description)
    {
        var value = (description ?? "").Trim().ToUpperInvariant();

        if (value.Contains("BPJS") &&
            (value.Contains("KESEHATAN") || value.Contains("BPJS KES") || value.Contains("BPJS_KES")))
            return "BPJS_KESEHATAN";

        if (value.Contains("BPJS") &&
            (value.Contains("KETENAGAKERJAAN") || value.Contains("TK")))
            return "BPJS_KETENAGAKERJAAN";

        if (value.Contains("VEHICLE") ||
            value.Contains("MAINTENANCE") ||
            value.Contains("KENDARAAN") ||
            value.Contains("SERVICE") ||
            value.Contains("SERVIS"))
            return "VEHICLE_MAINTENANCE";

        if (value.Contains("INTERNET") || value.Contains("KUOTA") || value.Contains("PULSA") || value.Contains("DATA"))
            return "INTERNET";

        return "OTHER";
    }

    private async Task SeedInvoiceFeesFromAuditsAsync(List<TrxInvoiceDetail> details)
    {
        var auditIds = details.Select(x => x.TrxAuditId).ToList();
        var auditData = await _context.trx_audits.AsNoTracking()
            .Where(x => auditIds.Contains(x.id))
            .Select(x => new
            {
                x.id,
                x.audit_execution_time,
                x.audit_schedule_date,
                x.created_date,
                x.km_range
            }).ToListAsync();
        var auditsById = auditData.ToDictionary(x => x.id);

        // One invoice can have multiple audited SPBU on the same day.
        // Existing explicit finance values are honored; NULL means legacy unset.
        var grouped = details.Where(x => auditsById.ContainsKey(x.TrxAuditId))
            .GroupBy(x =>
            {
                var a = auditsById[x.TrxAuditId];
                return DateOnly.FromDateTime(a.audit_execution_time ??
                    (a.audit_schedule_date.HasValue
                        ? a.audit_schedule_date.Value.ToDateTime(TimeOnly.MinValue)
                        : a.created_date));
            });
        foreach (var day in grouped)
        {
            var daily = day.Where(x => !x.IsDeleted).OrderBy(x => x.TrxAuditId).ToList();
            // Do not reseed zero fees here: Finance may explicitly reduce an audit fee to zero.
            // Imported invoice details already receive the default audit fee at creation.

            var hasLumpsum = daily.Any(x => (x.LumpsumFee ?? 0m) > 0m);
            if (!hasLumpsum)
            {
                var firstEligible = daily.FirstOrDefault(x =>
                    auditsById[x.TrxAuditId].km_range > 80m && !x.LumpsumFee.HasValue);
                if (firstEligible != null)
                    firstEligible.LumpsumFee = 400000m;
            }
            foreach (var d in daily)
                if (!d.LumpsumFee.HasValue) d.LumpsumFee = 0m;
        }
    }

    private async Task<bool> HasRoleAsync(params string[] roles)
    {
        var username = GetCurrentUser();
        var userRoles = await (
            from user in _context.app_users.AsNoTracking()
            join assignment in _context.app_user_roles.AsNoTracking() on user.id equals assignment.app_user_id
            join role in _context.app_roles.AsNoTracking() on assignment.app_role_id equals role.id
            where user.username == username && user.status == "ACTIVE" && role.status == "ACTIVE"
            select role.name
        ).ToListAsync();
        return userRoles.Any(name => roles.Any(r =>
            string.Equals(r, name?.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private Task<bool> HasFinanceReportingRoleAsync() =>
        HasRoleAsync("Finance", "Report", "Legacy", "Admin");

    // One warning per violated rule; never stop after the first category.
    private static List<string> GetExpenseWarnings(
        List<(string claim_item_type, string? description, decimal amount, DateOnly? expense_date)> expenses)
    {
        var warnings = new List<string>();
        var maxima = new Dictionary<string, decimal>
        {
            ["BPJS_KETENAGAKERJAAN"] = 36800m,
            ["BPJS_KESEHATAN"] = 35000m,
            ["VEHICLE_MAINTENANCE"] = 35000m,
            ["INTERNET"] = 100000m
        };
        foreach (var item in expenses)
        {
            var category = GetClaimExpenseCategory(
                string.IsNullOrWhiteSpace(item.claim_item_type) ? item.description ?? "" : item.claim_item_type);
            if (maxima.TryGetValue(category, out var limit) && item.amount > limit)
                warnings.Add($"{item.claim_item_type} Rp{item.amount:N0} melebihi batas Rp{limit:N0}.");
            if (item.amount < 0m)
                warnings.Add($"{item.claim_item_type}: nominal tidak boleh negatif.");
            if ((category == "VEHICLE_MAINTENANCE" || category == "INTERNET" || category == "OTHER")
                && !item.expense_date.HasValue)
                warnings.Add($"{item.claim_item_type}: tanggal pengeluaran wajib diisi.");
        }
        return warnings;
    }

    private async Task<string?> ValidateExpenseClaimsAsync(string claimId)
    {
        var expenses = await _context.TrxClaimDetails.AsNoTracking()
            .Where(x => x.trx_claim_id == claimId)
            .Select(x => new { x.claim_item_type, x.description, x.amount, x.expense_date }).ToListAsync();
        var warnings = GetExpenseWarnings(expenses
            .Select(x => (x.claim_item_type, x.description, x.amount, x.expense_date)).ToList());
        var media = await _context.TrxClaimMedias.AsNoTracking()
            .Where(x => x.trx_claim_id == claimId)
            .Select(x => new { x.claim_item_type, x.media_path }).ToListAsync();
        foreach (var item in expenses.Where(x => x.amount > 0))
        {
            var category = GetClaimExpenseCategory(item.claim_item_type);
            if (!media.Any(x => GetClaimExpenseCategory(x.claim_item_type) == category
                && !string.IsNullOrWhiteSpace(x.media_path)))
                warnings.Add($"Evidence {category.Replace('_', ' ')} belum tersedia.");
        }
        return warnings.Count == 0 ? null : string.Join(" | ", warnings.Distinct());
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

            if (posted.IsDeleted)
            {
                detail.AuditFee = 0;
                detail.LumpsumFee = 0;
                continue;
            }

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
        var now = DateTime.Now;

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

    private async Task EnsureFixedApprovalFlowAsync(string invoiceId)
    {
        var users = await _context.app_users
            .AsNoTracking()
            .Where(x =>
                x.status == "ACTIVE" &&
                FixedApprovalUsernames.Contains(x.username))
            .Select(x => new { x.id, x.username })
            .ToListAsync();

        var userByUsername = users.ToDictionary(
            x => x.username,
            StringComparer.OrdinalIgnoreCase);

        var missing = FixedApprovalUsernames
            .Where(username => !userByUsername.ContainsKey(username))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Approval user ACTIVE tidak ditemukan: " + string.Join(", ", missing));
        }

        var requiredUserIds = FixedApprovalUsernames
            .Select(username => userByUsername[username].id)
            .ToList();

        var existingFlows = await _context.TrxInvoiceApprovalFlows
            .Where(x => x.TrxInvoiceId == invoiceId)
            .OrderBy(x => x.ApprovalLevel)
            .ToListAsync();

        var matches = existingFlows.Count == requiredUserIds.Count &&
                      existingFlows.Select(x => x.ApproverUserId)
                          .SequenceEqual(requiredUserIds, StringComparer.OrdinalIgnoreCase);

        if (matches)
            return;

        if (existingFlows.Any(x => x.Status != ApprovalFlowPending))
        {
            throw new InvalidOperationException(
                "Approval flow invoice sudah memiliki action dan tidak dapat diganti otomatis.");
        }

        if (existingFlows.Count > 0)
            _context.TrxInvoiceApprovalFlows.RemoveRange(existingFlows);

        var currentUser = GetCurrentUser();
        var now = DateTime.Now;

        for (var i = 0; i < requiredUserIds.Count; i++)
        {
            _context.TrxInvoiceApprovalFlows.Add(new TrxInvoiceApprovalFlow
            {
                Id = Guid.NewGuid().ToString(),
                TrxInvoiceId = invoiceId,
                ApprovalLevel = i + 1,
                ApproverUserId = requiredUserIds[i],
                Status = ApprovalFlowPending,
                CreatedBy = currentUser,
                CreatedDate = now,
                UpdatedBy = currentUser,
                UpdatedDate = now
            });
        }

        NormalizeDateTimesForPostgres();
        await _context.SaveChangesAsync();
    }

    private async Task<string?> GetCurrentAppUserIdAsync()
    {
        var username = GetCurrentUser();

        return await _context.app_users
            .AsNoTracking()
            .Where(x => x.username == username)
            .Select(x => x.id)
            .FirstOrDefaultAsync();
    }

    private string GetCurrentUser()
    {
        return User.Identity?.Name ?? "SYSTEM";
    }

    private void NormalizeDateTimesForPostgres()
    {
        foreach (var entry in _context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                continue;

            foreach (var property in entry.Properties)
            {
                var clrType = Nullable.GetUnderlyingType(property.Metadata.ClrType)
                              ?? property.Metadata.ClrType;

                if (clrType != typeof(DateTime))
                    continue;

                if (property.CurrentValue == null)
                    continue;

                var dateValue = (DateTime)property.CurrentValue;
                var columnType = property.Metadata.GetColumnType()?.ToLowerInvariant() ?? "";

                if (columnType.Contains("timestamp with time zone") ||
                    columnType.Contains("timestamptz"))
                {
                    if (dateValue.Kind == DateTimeKind.Utc)
                    {
                        property.CurrentValue = dateValue;
                    }
                    else if (dateValue.Kind == DateTimeKind.Local)
                    {
                        property.CurrentValue = dateValue.ToUniversalTime();
                    }
                    else
                    {
                        property.CurrentValue = DateTime.SpecifyKind(dateValue, DateTimeKind.Local).ToUniversalTime();
                    }
                }
                else if (columnType.Contains("timestamp without time zone") ||
                         columnType.Contains("timestamp"))
                {
                    property.CurrentValue = DateTime.SpecifyKind(dateValue, DateTimeKind.Unspecified);
                }
            }
        }
    }

    private async Task<string?> GetUserSignaturePathAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        return await _context.app_users
            .AsNoTracking()
            .Where(x => x.id == userId)
            .Select(x => x.signature_path)
            .FirstOrDefaultAsync();
    }

    private async Task<(string Name, string SignaturePath, string Homebase, string AuditorCode)> GetRequestorUser(
        string invoiceAppUserId,
        string claimAppUserId)
    {
        var userId = !string.IsNullOrWhiteSpace(invoiceAppUserId)
            ? invoiceAppUserId
            : claimAppUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return ("-", "", "-", "-");

        var user = await _context.app_users
            .AsNoTracking()
            .Where(x => x.id == userId)
            .Select(x => new
            {
                x.name,
                x.signature_path,
                x.homebase,
                x.auditor_code
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return ("-", "", "-", "-");

        return (
            string.IsNullOrWhiteSpace(user.name) ? "-" : user.name,
            user.signature_path ?? "",
            string.IsNullOrWhiteSpace(user.homebase) ? "-" : user.homebase,
            string.IsNullOrWhiteSpace(user.auditor_code) ? "-" : user.auditor_code
        );
    }

    private static string Csv(string value)
    {
        value ??= "";
        // Neutralize spreadsheet formulas in user-supplied text (Excel/LibreOffice CSV).
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");

        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N") : clean;
    }
}
public class InvoiceAuditorOptionVM
{
    public string Id { get; set; }
    public string Name { get; set; }
}