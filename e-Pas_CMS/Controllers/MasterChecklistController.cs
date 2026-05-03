using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class MasterChecklistController : Controller
    {
        private readonly EpasDbContext _context;
        private readonly ILogger<MasterChecklistController> _logger;

        public MasterChecklistController(
            EpasDbContext context,
            ILogger<MasterChecklistController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string searchTerm = "",
            string type = "",
            string sortColumn = "Version",
            string sortDirection = "desc")
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var types = (await conn.QueryAsync<string>(@"
        SELECT DISTINCT type
        FROM master_questioner
        WHERE category = 'CHECKLIST'
          AND type IS NOT NULL 
          AND TRIM(type) <> ''
        ORDER BY type;
    ")).ToList();

            var whereSql = @"
        WHERE mq.category = 'CHECKLIST'
          AND (@type = '' OR mq.type = @type)
          AND (
                @searchTerm = ''
                OR LOWER(mq.type) LIKE LOWER('%' || @searchTerm || '%')
                OR LOWER(mq.status) LIKE LOWER('%' || @searchTerm || '%')
                OR CAST(mq.version AS TEXT) LIKE '%' || @searchTerm || '%'
          )
    ";

            var countSql = $@"
        SELECT COUNT(1)
        FROM master_questioner mq
        {whereSql};
    ";

            var totalItems = await conn.ExecuteScalarAsync<int>(countSql, new
            {
                type = type ?? "",
                searchTerm = searchTerm ?? ""
            });

            var orderBy = sortColumn switch
            {
                "Type" => sortDirection == "asc" ? "mq.type ASC" : "mq.type DESC",
                "Version" => sortDirection == "asc" ? "mq.version ASC" : "mq.version DESC",
                "EffectiveStartDate" => sortDirection == "asc" ? "mq.effective_start_date ASC" : "mq.effective_start_date DESC",
                "EffectiveEndDate" => sortDirection == "asc" ? "mq.effective_end_date ASC" : "mq.effective_end_date DESC",
                "Status" => sortDirection == "asc" ? "mq.status ASC" : "mq.status DESC",
                "TotalNode" => sortDirection == "asc" ? "COUNT(mqd.id) ASC" : "COUNT(mqd.id) DESC",
                "TotalQuestion" => sortDirection == "asc" ? "COUNT(mqd.id) FILTER (WHERE mqd.type = 'QUESTION') ASC" : "COUNT(mqd.id) FILTER (WHERE mqd.type = 'QUESTION') DESC",
                _ => "mq.version DESC"
            };

            var dataSql = $@"
        SELECT 
            mq.id,
            mq.type,
            mq.category,
            mq.version,
            mq.effective_start_date AS EffectiveStartDate,
            mq.effective_end_date AS EffectiveEndDate,
            mq.status,
            COUNT(mqd.id) AS TotalNode,
            COUNT(mqd.id) FILTER (WHERE mqd.type = 'QUESTION') AS TotalQuestion
        FROM master_questioner mq
        LEFT JOIN master_questioner_detail mqd 
            ON mqd.master_questioner_id = mq.id
        {whereSql}
        GROUP BY 
            mq.id,
            mq.type,
            mq.category,
            mq.version,
            mq.effective_start_date,
            mq.effective_end_date,
            mq.status
        ORDER BY {orderBy}
        OFFSET @offset
        LIMIT @pageSize;
    ";

            var items = (await conn.QueryAsync<MasterChecklistHeaderVm>(dataSql, new
            {
                type = type ?? "",
                searchTerm = searchTerm ?? "",
                offset = (pageNumber - 1) * pageSize,
                pageSize
            })).ToList();

            var model = new PaginationModel<MasterChecklistHeaderVm>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.SearchTerm = searchTerm;
            ViewBag.Type = type;
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;
            ViewBag.TypeOptions = types;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction(nameof(Index));

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var header = await conn.QueryFirstOrDefaultAsync<MasterChecklistEditVm>(@"
                SELECT 
                    id AS MasterQuestionerId,
                    type,
                    category,
                    version,
                    status,
                    effective_start_date AS EffectiveStartDate,
                    effective_end_date AS EffectiveEndDate
                FROM master_questioner
                WHERE id = @id
                LIMIT 1;
            ", new { id });

            if (header == null)
                return NotFound();

            var nodes = (await conn.QueryAsync<MasterChecklistNodeVm>(@"
                SELECT
                    id,
                    master_questioner_id AS MasterQuestionerId,
                    parent_id AS ParentId,
                    type,
                    number,
                    title,
                    description,
                    score_option AS ScoreOption,
                    weight,
                    order_no AS OrderNo,
                    status,
                    is_penalty AS IsPenalty,
                    penalty_alert AS PenaltyAlert,
                    is_relaksasi AS IsRelaksasi,
                    penalty_excellent_criteria AS PenaltyExcellentCriteria,
                    score_excellent_criteria AS ScoreExcellentCriteria,
                    form_type AS FormType
                FROM master_questioner_detail
                WHERE master_questioner_id = @id
                ORDER BY 
                    COALESCE(parent_id, ''),
                    order_no ASC,
                    number ASC,
                    title ASC;
            ", new { id })).ToList();

            header.Nodes = BuildTree(nodes);

            return View(header);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveNode([FromBody] SaveChecklistNodeRequest req)
        {
            if (req == null)
                return BadRequest("Payload kosong.");

            if (string.IsNullOrWhiteSpace(req.MasterQuestionerId))
                return BadRequest("Master questioner wajib diisi.");

            if (string.IsNullOrWhiteSpace(req.Title))
                return BadRequest("Title wajib diisi.");

            if (string.IsNullOrWhiteSpace(req.Type))
                req.Type = "QUESTION";

            if (string.IsNullOrWhiteSpace(req.Status))
                req.Status = "ACTIVE";

            var currentUser = User.Identity?.Name ?? "SYSTEM";

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(req.Id))
                {
                    var newId = Guid.NewGuid().ToString();

                    await conn.ExecuteAsync(@"
                        INSERT INTO master_questioner_detail
                        (
                            id,
                            master_questioner_id,
                            parent_id,
                            type,
                            title,
                            description,
                            score_option,
                            weight,
                            order_no,
                            status,
                            created_by,
                            created_date,
                            updated_by,
                            updated_date,
                            is_penalty,
                            penalty_alert,
                            is_relaksasi,
                            penalty_excellent_criteria,
                            number,
                            score_excellent_criteria,
                            form_type
                        )
                        VALUES
                        (
                            @Id,
                            @MasterQuestionerId,
                            NULLIF(@ParentId, ''),
                            @Type,
                            @Title,
                            @Description,
                            @ScoreOption,
                            @Weight,
                            @OrderNo,
                            @Status,
                            @User,
                            CURRENT_TIMESTAMP,
                            @User,
                            CURRENT_TIMESTAMP,
                            @IsPenalty,
                            @PenaltyAlert,
                            @IsRelaksasi,
                            @PenaltyExcellentCriteria,
                            @Number,
                            @ScoreExcellentCriteria,
                            @FormType
                        );
                    ", new
                    {
                        Id = newId,
                        req.MasterQuestionerId,
                        ParentId = req.ParentId ?? "",
                        req.Type,
                        req.Title,
                        req.Description,
                        req.ScoreOption,
                        req.Weight,
                        req.OrderNo,
                        req.Status,
                        User = currentUser,
                        req.IsPenalty,
                        req.PenaltyAlert,
                        req.IsRelaksasi,
                        req.PenaltyExcellentCriteria,
                        req.Number,
                        req.ScoreExcellentCriteria,
                        req.FormType
                    }, tx);

                    await tx.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = "Node berhasil ditambahkan.",
                        id = newId
                    });
                }

                await conn.ExecuteAsync(@"
                    UPDATE master_questioner_detail
                    SET
                        parent_id = NULLIF(@ParentId, ''),
                        type = @Type,
                        title = @Title,
                        description = @Description,
                        score_option = @ScoreOption,
                        weight = @Weight,
                        order_no = @OrderNo,
                        status = @Status,
                        updated_by = @User,
                        updated_date = CURRENT_TIMESTAMP,
                        is_penalty = @IsPenalty,
                        penalty_alert = @PenaltyAlert,
                        is_relaksasi = @IsRelaksasi,
                        penalty_excellent_criteria = @PenaltyExcellentCriteria,
                        number = @Number,
                        score_excellent_criteria = @ScoreExcellentCriteria,
                        form_type = @FormType
                    WHERE id = @Id
                      AND master_questioner_id = @MasterQuestionerId;
                ", new
                {
                    req.Id,
                    req.MasterQuestionerId,
                    ParentId = req.ParentId ?? "",
                    req.Type,
                    req.Title,
                    req.Description,
                    req.ScoreOption,
                    req.Weight,
                    req.OrderNo,
                    req.Status,
                    User = currentUser,
                    req.IsPenalty,
                    req.PenaltyAlert,
                    req.IsRelaksasi,
                    req.PenaltyExcellentCriteria,
                    req.Number,
                    req.ScoreExcellentCriteria,
                    req.FormType
                }, tx);

                await tx.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = "Node berhasil disimpan.",
                    id = req.Id
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Gagal save checklist node");
                return StatusCode(500, "Gagal menyimpan data.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteNode([FromBody] DeleteChecklistNodeRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Id))
                return BadRequest("Id wajib diisi.");

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var hasChildren = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM master_questioner_detail
                WHERE parent_id = @id;
            ", new { id = req.Id });

            if (hasChildren > 0)
                return BadRequest("Node tidak bisa dihapus karena masih punya child.");

            await conn.ExecuteAsync(@"
                DELETE FROM master_questioner_detail
                WHERE id = @id;
            ", new { id = req.Id });

            return Json(new
            {
                success = true,
                message = "Node berhasil dihapus."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrder([FromBody] UpdateOrderRequest req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
                return BadRequest("Data urutan kosong.");

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                foreach (var item in req.Items)
                {
                    await conn.ExecuteAsync(@"
                        UPDATE master_questioner_detail
                        SET 
                            order_no = @OrderNo,
                            updated_by = @User,
                            updated_date = CURRENT_TIMESTAMP
                        WHERE id = @Id;
                    ", new
                    {
                        item.Id,
                        item.OrderNo,
                        User = User.Identity?.Name ?? "SYSTEM"
                    }, tx);
                }

                await tx.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = "Urutan berhasil disimpan."
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Gagal update order checklist");
                return StatusCode(500, "Gagal update urutan.");
            }
        }

        private static List<MasterChecklistNodeVm> BuildTree(List<MasterChecklistNodeVm> flat)
        {
            var lookup = flat.ToLookup(x => x.ParentId);
            foreach (var node in flat)
            {
                node.Children = lookup[node.Id]
                    .OrderBy(x => x.OrderNo)
                    .ThenBy(x => x.Number)
                    .ToList();
            }

            return flat
                .Where(x => string.IsNullOrWhiteSpace(x.ParentId))
                .OrderBy(x => x.OrderNo)
                .ThenBy(x => x.Number)
                .ToList();
        }
    }

    public class DeleteChecklistNodeRequest
    {
        public string Id { get; set; }
    }

    public class UpdateOrderRequest
    {
        public List<UpdateOrderItem> Items { get; set; } = new();
    }

    public class UpdateOrderItem
    {
        public string Id { get; set; }
        public int OrderNo { get; set; }
    }
}