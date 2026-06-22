using System.Globalization;
using System.Text;
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
                        // CMS Klik Proses
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

            if (userRegion.Any() || userSbm.Any())
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
                        + g.Sum(x => x.Detail.LumpsumFee ?? (x.Audit.km_range > 80 ? 400000m : 0m))
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

    public async Task<IActionResult> Detail(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var vm = await BuildInvoiceDetailViewModel(id, setViewBag: true);

        if (vm == null)
            return NotFound();

        return View(vm);
    }

    public async Task<IActionResult> Preview(string id, bool print = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var vm = await BuildInvoiceDetailViewModel(id, setViewBag: true);

        if (vm == null)
            return NotFound();

        ViewBag.AutoPrint = print;

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

        var invoiceDetails = await _context.TrxInvoiceDetails
            .Where(x => x.TrxInvoiceId == id)
            .ToListAsync();

        if (!invoiceDetails.Any())
        {
            TempData["Error"] = "Detail invoice tidak ditemukan.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        // Flow awal yang boleh klik tombol Proses:
        // trx_invoice        = IN_PROGRESS
        // trx_invoice_detail = IN_PROGRESS
        // trx_claim          = UNDER_REVIEW
        if (invoice.Status != InvoiceInProgress)
        {
            TempData["Error"] = "Invoice belum masuk tahap in progress.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (invoiceDetails.Any(x => x.Status != InvoiceDetailInProgress))
        {
            TempData["Error"] = "Detail invoice belum masuk tahap in progress.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (claim.status != ClaimUnderReview)
        {
            TempData["Error"] = "Claim belum masuk tahap under review.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var now = DateTime.Now;
        var currentUser = GetCurrentUser();

        // Setelah klik Proses:
        // trx_invoice        = IN_PROGRESS
        // trx_invoice_detail = IN_PROGRESS
        // trx_claim          = PENDING_APPROVAL
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

        TempData["Success"] = "Invoice berhasil diproses dan masuk ke pending approval finance.";

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id, InvoiceApprovalPostVM model)
    {
        try
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

            var invoiceDetails = await _context.TrxInvoiceDetails
                .Where(x => x.TrxInvoiceId == invoiceId)
                .ToListAsync();

            if (!invoiceDetails.Any())
            {
                TempData["Error"] = "Detail invoice tidak ditemukan.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            if (invoice.Status != InvoiceInProgress)
            {
                TempData["Error"] = "Invoice belum masuk tahap in progress.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            if (invoiceDetails.Any(x => x.Status != InvoiceDetailInProgress))
            {
                TempData["Error"] = "Detail invoice belum masuk tahap in progress.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            if (claim.status != ClaimPendingApproval)
            {
                TempData["Error"] = "Claim belum masuk tahap pending approval.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            ApplyFinanceAdjustment(invoiceDetails, model?.Items);

            var now = DateTime.Now;
            var currentUser = GetCurrentUser();

            invoice.Status = InvoiceCompleted;
            invoice.CompletedDate = now;
            invoice.UpdatedBy = currentUser;
            invoice.UpdatedDate = now;

            claim.status = ClaimApproved;
            claim.completed_date = now;
            claim.updated_by = currentUser;
            claim.updated_date = now;

            var postedMap = model?.Items?
                .Where(x => !string.IsNullOrWhiteSpace(x.TrxInvoiceDetailId))
                .GroupBy(x => x.TrxInvoiceDetailId)
                .ToDictionary(x => x.Key, x => x.First())
                ?? new Dictionary<string, InvoiceApprovalDetailPostVM>();

            foreach (var detail in invoiceDetails)
            {
                var isDeleted = postedMap.TryGetValue(detail.Id, out var posted) && posted.IsDeleted;

                detail.Status = isDeleted
                    ? InvoiceDetailNotClaimed
                    : InvoiceDetailClaimed;

                detail.UpdatedBy = currentUser;
                detail.UpdatedDate = now;
            }

            var activeInvoiceDetails = invoiceDetails
                .Where(x => x.Status == InvoiceDetailClaimed)
                .ToList();

            var approval = await BuildApprovalSnapshot(
                invoice,
                claim,
                activeInvoiceDetails,
                "APPROVED",
                null
            );

            _context.TrxInvoiceApprovals.Add(approval);

            NormalizeDateTimesForPostgres();

            await _context.SaveChangesAsync();

            TempData["Success"] = "Invoice berhasil disetujui dan data approval finance berhasil disimpan.";

            return RedirectToAction(nameof(Detail), new { id = invoiceId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Gagal approve invoice: " + ex.Message;

            if (ex.InnerException != null)
                TempData["Error"] += " | Inner: " + ex.InnerException.Message;

            return RedirectToAction(nameof(Detail), new { id = id ?? model?.Id });
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
                .FirstOrDefaultAsync(x => x.trx_invoice_id == invoiceId);

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

            // Finance hanya boleh reject saat invoice berada di tahap pending approval:
            // trx_invoice        = IN_PROGRESS
            // trx_invoice_detail = IN_PROGRESS
            // trx_claim          = PENDING_APPROVAL
            if (invoice.Status != InvoiceInProgress)
            {
                TempData["Error"] = "Invoice belum masuk tahap in progress.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            if (invoiceDetails.Any(x => x.Status != InvoiceDetailInProgress))
            {
                TempData["Error"] = "Detail invoice belum masuk tahap in progress.";
                return RedirectToAction(nameof(Detail), new { id = invoiceId });
            }

            if (claim.status != ClaimPendingApproval)
            {
                TempData["Error"] = "Claim belum masuk tahap pending approval.";
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

            var now = DateTime.Now;
            var currentUser = GetCurrentUser();

            // Jika finance reject:
            // trx_invoice        = REJECTED
            // trx_invoice_detail = NOT_CLAIMED
            // trx_claim          = REJECTED
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

            TempData["Success"] = "Invoice berhasil ditolak dan alasan penolakan berhasil disimpan.";

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

        var vm = await BuildInvoiceDetailViewModel(id, setViewBag: true);

        if (vm == null)
            return NotFound();

        var claimExpenseAmounts = ViewBag.ClaimExpenseAmounts as Dictionary<string, decimal>
            ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        decimal ExpenseAmount(string key)
        {
            return claimExpenseAmounts.TryGetValue(key, out var amount) ? amount : 0m;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Invoice No,Name,Period,Kind Of Job,Homebase,Claim Date,Status");
        sb.AppendLine(string.Join(",", new[]
        {
            Csv(vm.InvoiceNo),
            Csv(vm.EmployeeName),
            Csv(vm.Period),
            Csv(vm.Job),
            Csv(vm.Homebase),
            Csv(vm.ClaimDate.ToString("dd/MM/yyyy")),
            Csv(vm.Status)
        }));

        sb.AppendLine();
        sb.AppendLine("No,Date,Details of Expense,Distance KM,Audit Fee,Lumpsum,Line Total");

        if (vm.Items != null)
        {
            for (var i = 0; i < vm.Items.Count; i++)
            {
                var item = vm.Items[i];
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv((i + 1).ToString()),
                    Csv(item.Date.ToString("dd/MM/yyyy")),
                    Csv(item.Description),
                    Csv(item.DistanceKm.ToString("0.##", CultureInfo.InvariantCulture)),
                    Csv(item.AuditFee.ToString("0.##", CultureInfo.InvariantCulture)),
                    Csv(item.Lumpsum.ToString("0.##", CultureInfo.InvariantCulture)),
                    Csv((item.AuditFee + item.Lumpsum).ToString("0.##", CultureInfo.InvariantCulture))
                }));
            }
        }

        sb.AppendLine();
        sb.AppendLine("Summary,Amount");
        sb.AppendLine($"{Csv("Claim Expense")},{vm.ClaimExpenseAmount.ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("Total Audit Fee")},{vm.TotalAuditFee.ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("Total Lumpsum")},{vm.TotalLumpsum.ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("BPJS Kesehatan")},{ExpenseAmount("BPJS_KESEHATAN").ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("BPJS Ketenagakerjaan")},{ExpenseAmount("BPJS_KETENAGAKERJAAN").ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("Vehicle Maintenance")},{ExpenseAmount("VEHICLE_MAINTENANCE").ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("Internet Data")},{ExpenseAmount("INTERNET").ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("Others")},{ExpenseAmount("OTHER").ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Csv("Total Expense")},{vm.TotalExpense.ToString("0.##", CultureInfo.InvariantCulture)}");

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();

        return File(bytes, "text/csv", $"invoice-{SanitizeFileName(vm.InvoiceNo)}.csv");
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
            ClaimUnderReview => "Sedang Direview",
            ClaimPendingApproval => "Menunggu Persetujuan",
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
            .FirstOrDefaultAsync(x => x.trx_invoice_id == id);

        if (claim == null)
            return null;

        var claimExpenseDetails = await _context.TrxClaimDetails
            .AsNoTracking()
            .Where(x => x.trx_claim_id == claim.id)
            .OrderBy(x => x.created_date)
            .Select(x => new
            {
                x.description,
                amount = (decimal?)x.amount ?? 0m
            })
            .ToListAsync();

        var claimExpenseAmount = claimExpenseDetails.Sum(x => x.amount);

        var claimExpenseAmounts = claimExpenseDetails
            .GroupBy(x => GetClaimExpenseCategory(x.description))
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
            orderby aud.audit_execution_time descending,
                    aud.audit_schedule_date descending,
                    det.CreatedDate descending
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
                Description = "Audit SPBU " + sp.spbu_no,
                AuditFee = det.AuditFee,
                LumpsumFee = det.LumpsumFee,
                DistanceKm = aud.km_range
            }
        ).ToListAsync();

        var details = rawDetails.Select(x =>
        {
            var distanceKm = x.DistanceKm;
            var lumpsum = x.LumpsumFee ?? GetLumpsumFeeByDistance(distanceKm);

            return new InvoiceDetailItemVM
            {
                TrxInvoiceDetailId = x.TrxInvoiceDetailId,
                TrxAuditId = x.TrxAuditId,
                Date = DateTime.SpecifyKind(x.AuditDate, DateTimeKind.Utc),
                Description = x.Description,
                DistanceKm = distanceKm,
                ClaimAmount = 0,
                AuditFee = x.AuditFee,
                Lumpsum = lumpsum,
                Amount = x.AuditFee + lumpsum
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

        var requestorUser = await GetRequestorUser(invoice.AppUserId, claim.app_user_id);

        var vm = new InvoiceDetailViewModel
        {
            Id = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            Status = invoice.Status,
            ClaimDate = claim.claim_date,

            EmployeeName = requestorUser.Name,
            RequestorSignaturePath = requestorUser.SignaturePath,

            Period = $"{invoice.InvoicePeriodStart:dd MMM yyyy} - {invoice.InvoicePeriodEnd:dd MMM yyyy}",
            Homebase = "-",
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
            RejectionReason = latestApproval?.RejectionReason
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

        if (value.Contains("BPJS") && value.Contains("KESEHATAN"))
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

    private async Task<(string Name, string SignaturePath)> GetRequestorUser(
        string invoiceAppUserId,
        string claimAppUserId)
    {
        var userId = !string.IsNullOrWhiteSpace(invoiceAppUserId)
            ? invoiceAppUserId
            : claimAppUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return ("-", "");

        var user = await _context.app_users
            .AsNoTracking()
            .Where(x => x.id == userId)
            .Select(x => new
            {
                x.name,
                x.signature_path
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return ("-", "");

        return (
            string.IsNullOrWhiteSpace(user.name) ? "-" : user.name,
            user.signature_path ?? ""
        );
    }

    private static string Csv(string value)
    {
        value ??= "";
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
    public bool IsDeleted { get; set; }
}

public class InvoiceAuditorOptionVM
{
    public string Id { get; set; }
    public string Name { get; set; }
}
