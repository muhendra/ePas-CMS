using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class CityBoundaryController : Controller
    {
        private readonly EpasDbContext _context;

        public CityBoundaryController(EpasDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
    int pageNumber = 1,
    int pageSize = 10,
    string searchTerm = "")
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var keyword = searchTerm ?? "";

            var whereSql = @"
        WHERE 1=1
          AND EXISTS (
              SELECT 1
              FROM app_user_city_boundary b
              WHERE b.app_user_id = au.id
          )
          AND (
                @searchTerm = ''
                OR LOWER(COALESCE(au.name, '')) LIKE LOWER('%' || @searchTerm || '%')
                OR LOWER(COALESCE(au.username, '')) LIKE LOWER('%' || @searchTerm || '%')
                OR EXISTS (
                    SELECT 1
                    FROM app_user_city_boundary bx
                    WHERE bx.app_user_id = au.id
                      AND LOWER(COALESCE(bx.address, '')) LIKE LOWER('%' || @searchTerm || '%')
                )
          )
    ";

            var totalItems = await conn.ExecuteScalarAsync<int>($@"
        SELECT COUNT(1)
        FROM app_user au
        {whereSql};
    ", new
            {
                searchTerm = keyword
            });

            var dataSql = $@"
        WITH boundary_ranked AS (
            SELECT
                b.app_user_id,
                b.address,
                ROW_NUMBER() OVER (
                    PARTITION BY b.app_user_id
                    ORDER BY b.created_date DESC, b.id DESC
                ) AS rn
            FROM app_user_city_boundary b
        ),
        boundary_pivot AS (
            SELECT
                app_user_id,
                MAX(CASE WHEN rn = 1 THEN address END) AS BoundaryCity1,
                MAX(CASE WHEN rn = 2 THEN address END) AS BoundaryCity2,
                MAX(CASE WHEN rn = 3 THEN address END) AS BoundaryCity3,
                MAX(CASE WHEN rn = 4 THEN address END) AS BoundaryCity4
            FROM boundary_ranked
            WHERE rn <= 4
            GROUP BY app_user_id
        ),
        last_audit AS (
            SELECT
                ta.app_user_id,
                MAX(COALESCE(
                    ta.audit_execution_time,
                    ta.audit_schedule_date::timestamp,
                    ta.created_date
                )) AS LastAuditDate
            FROM trx_audit ta
            GROUP BY ta.app_user_id
        )
        SELECT
            au.id AS Id,
            au.id AS AppUserId,
            au.name AS AuditorName,
            au.username AS AuditorUsername,
            la.LastAuditDate,
            bp.BoundaryCity1,
            bp.BoundaryCity2,
            bp.BoundaryCity3,
            bp.BoundaryCity4,
            au.status AS Status
        FROM app_user au
        INNER JOIN boundary_pivot bp ON bp.app_user_id = au.id
        LEFT JOIN last_audit la ON la.app_user_id = au.id
        {whereSql}
        ORDER BY COALESCE(la.LastAuditDate, au.created_date) DESC NULLS LAST
        OFFSET @offset
        LIMIT @pageSize;
    ";

            var items = (await conn.QueryAsync<CityBoundaryListVm>(dataSql, new
            {
                searchTerm = keyword,
                offset = (pageNumber - 1) * pageSize,
                pageSize
            })).ToList();

            var model = new PaginationModel<CityBoundaryListVm>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.SearchTerm = searchTerm;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Approval(
            int pageNumber = 1,
            int pageSize = 10,
            string searchTerm = "",
            string status = "")
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var whereSql = @"
                WHERE 1=1
                  AND (@status = '' OR t.status = @status)
                  AND (
                        @searchTerm = ''
                        OR LOWER(au.name) LIKE LOWER('%' || @searchTerm || '%')
                        OR LOWER(au.username) LIKE LOWER('%' || @searchTerm || '%')
                        OR LOWER(COALESCE(t.status, '')) LIKE LOWER('%' || @searchTerm || '%')
                  )
            ";

            var totalItems = await conn.ExecuteScalarAsync<int>($@"
                SELECT COUNT(1)
                FROM trx_city_boundary_request t
                INNER JOIN app_user au ON au.id = t.app_user_id
                {whereSql};
            ", new
            {
                searchTerm = searchTerm ?? "",
                status = status ?? ""
            });

            var items = (await conn.QueryAsync<CityBoundaryListVm>($@"
                SELECT
                    t.id,
                    t.app_user_id AS AppUserId,
                    au.name AS AuditorName,
                    au.username AS AuditorUsername,
                    t.last_audit_date AS LastAuditDate,
                    t.boundary_city_1 AS BoundaryCity1,
                    t.boundary_city_2 AS BoundaryCity2,
                    t.boundary_city_3 AS BoundaryCity3,
                    t.boundary_city_4 AS BoundaryCity4,
                    t.status
                FROM trx_city_boundary_request t
                INNER JOIN app_user au ON au.id = t.app_user_id
                {whereSql}
                ORDER BY 
                    CASE WHEN t.status = 'WAITING_APPROVAL' THEN 0 ELSE 1 END,
                    t.created_date DESC
                OFFSET @offset
                LIMIT @pageSize;
            ", new
            {
                searchTerm = searchTerm ?? "",
                status = status ?? "",
                offset = (pageNumber - 1) * pageSize,
                pageSize
            })).ToList();

            ViewBag.SearchTerm = searchTerm;
            ViewBag.Status = status;

            return View(new PaginationModel<CityBoundaryListVm>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            });
        }

        [HttpGet]
        public async Task<IActionResult> Detail(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction(nameof(Index));

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var model = await conn.QueryFirstOrDefaultAsync<CityBoundaryDetailVm>(@"
        WITH boundary_ranked AS (
            SELECT
                b.app_user_id,
                b.address,
                ROW_NUMBER() OVER (
                    PARTITION BY b.app_user_id
                    ORDER BY b.created_date DESC, b.id DESC
                ) AS rn
            FROM app_user_city_boundary b
            WHERE b.app_user_id = @appUserId
        ),
        boundary_pivot AS (
            SELECT
                app_user_id,
                MAX(CASE WHEN rn = 1 THEN address END) AS BoundaryCity1,
                MAX(CASE WHEN rn = 2 THEN address END) AS BoundaryCity2,
                MAX(CASE WHEN rn = 3 THEN address END) AS BoundaryCity3,
                MAX(CASE WHEN rn = 4 THEN address END) AS BoundaryCity4
            FROM boundary_ranked
            WHERE rn <= 4
            GROUP BY app_user_id
        ),
        last_audit AS (
            SELECT
                ta.app_user_id,
                MAX(COALESCE(
                    ta.audit_execution_time,
                    ta.audit_schedule_date::timestamp,
                    ta.created_date
                )) AS LastAuditDate
            FROM trx_audit ta
            WHERE ta.app_user_id = @appUserId
            GROUP BY ta.app_user_id
        )
        SELECT
            au.id AS Id,
            au.id AS AppUserId,
            au.name AS AuditorName,
            au.username AS AuditorUsername,
            au.phone_number AS AuditorPhone,
            au.email AS AuditorEmail,
            au.status AS AuditorStatus,
            la.LastAuditDate,
            bp.BoundaryCity1,
            bp.BoundaryCity2,
            bp.BoundaryCity3,
            bp.BoundaryCity4,
            au.status AS Status,
            NULL AS Notes,
            NULL AS ApprovalNotes
        FROM app_user au
        INNER JOIN boundary_pivot bp ON bp.app_user_id = au.id
        LEFT JOIN last_audit la ON la.app_user_id = au.id
        WHERE au.id = @appUserId
        LIMIT 1;
    ", new { appUserId = id });

            if (model == null)
                return NotFound();

            model.AuditHistories = (await conn.QueryAsync<CityBoundaryAuditHistoryVm>(@"
        SELECT
            ta.id AS TrxAuditId,
            ta.report_no AS ReportNo,
            COALESCE(
                ta.audit_execution_time,
                ta.audit_schedule_date::timestamp,
                ta.created_date
            ) AS AuditDate,
            ta.audit_type AS AuditType,
            ta.audit_level AS AuditLevel,
            s.spbu_no AS SpbuNo,
            s.address AS SpbuAddress,
            COALESCE(tid.amount, 0) AS Amount
        FROM trx_audit ta
        INNER JOIN spbu s ON s.id = ta.spbu_id
        LEFT JOIN trx_invoice_detail tid ON tid.trx_audit_id = ta.id
        WHERE ta.app_user_id = @appUserId
        ORDER BY COALESCE(
            ta.audit_execution_time,
            ta.audit_schedule_date::timestamp,
            ta.created_date
        ) DESC
        LIMIT 30;
    ", new { appUserId = model.AppUserId })).ToList();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(CityBoundaryApprovalVm model)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
                return RedirectToAction(nameof(Approval));

            var currentUser = User.Identity?.Name ?? "SYSTEM";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE trx_city_boundary_request
                SET 
                    status = 'APPROVED',
                    approval_notes = @notes,
                    approved_by = @user,
                    approved_date = CURRENT_TIMESTAMP,
                    updated_by = @user,
                    updated_date = CURRENT_TIMESTAMP
                WHERE id = @id;
            ", new
            {
                id = model.Id,
                notes = model.ApprovalNotes,
                user = currentUser
            });

            TempData["Success"] = "Batas kota berhasil disetujui.";
            return RedirectToAction(nameof(Approval));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(CityBoundaryApprovalVm model)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
                return RedirectToAction(nameof(Approval));

            var currentUser = User.Identity?.Name ?? "SYSTEM";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE trx_city_boundary_request
                SET 
                    status = 'REJECTED',
                    approval_notes = @notes,
                    approved_by = @user,
                    approved_date = CURRENT_TIMESTAMP,
                    updated_by = @user,
                    updated_date = CURRENT_TIMESTAMP
                WHERE id = @id;
            ", new
            {
                id = model.Id,
                notes = model.ApprovalNotes,
                user = currentUser
            });

            TempData["Success"] = "Batas kota berhasil ditolak.";
            return RedirectToAction(nameof(Approval));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeedFromAuditor()
        {
            var currentUser = User.Identity?.Name ?? "SYSTEM";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                INSERT INTO trx_city_boundary_request
                (
                    id,
                    app_user_id,
                    last_audit_date,
                    boundary_city_1,
                    boundary_city_2,
                    boundary_city_3,
                    boundary_city_4,
                    status,
                    notes,
                    created_by,
                    created_date,
                    updated_by,
                    updated_date
                )
                SELECT
                    uuid_generate_v4(),
                    au.id,
                    MAX(COALESCE(ta.audit_execution_time, ta.audit_schedule_date::timestamp, ta.created_date)) AS last_audit_date,
                    au.city_name,
                    NULL,
                    NULL,
                    NULL,
                    'WAITING_APPROVAL',
                    'Auto generated from auditor profile',
                    @user,
                    CURRENT_TIMESTAMP,
                    @user,
                    CURRENT_TIMESTAMP
                FROM app_user au
                LEFT JOIN trx_audit ta ON ta.app_user_id = au.id
                WHERE au.status = 'ACTIVE'
                  AND NOT EXISTS (
                        SELECT 1 
                        FROM trx_city_boundary_request x 
                        WHERE x.app_user_id = au.id
                  )
                GROUP BY au.id, au.city_name;
            ", new { user = currentUser });

            TempData["Success"] = "Data batas kota berhasil digenerate dari auditor aktif.";
            return RedirectToAction(nameof(Index));
        }
    }
}