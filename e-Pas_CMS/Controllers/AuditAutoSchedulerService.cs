using System.Data;
using System.Data.Common;
using Dapper;
using e_Pas_CMS.Data;
using Microsoft.EntityFrameworkCore;

public class AuditAutoSchedulerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AuditAutoSchedulerService> _logger;

    public static readonly string CreateSchedulerSql = @"
    INSERT INTO trx_audit (
        id,
        report_prefix,
        report_no,
        spbu_id,
        app_user_id,
        master_questioner_intro_id,
        master_questioner_checklist_id,
        audit_level,
        audit_type,
        audit_schedule_date,
        audit_execution_time,
        audit_media_upload,
        audit_media_total,
        audit_mom_intro,
        audit_mom_final,
        status,
        created_by,
        created_date,
        updated_by,
        updated_date,
        approval_by,
        approval_date,
        good_status,
        excellent_status,
        score,
        report_file_good,
        report_file_excellent,
        report_file_boa,
        boa_status,
        master_closing_date_id,
        closing_date,
        app_user_id_auditor2,
        form_type_auditor1,
        form_type_auditor2,
        form_status_auditor1,
        form_status_auditor2
    )
    WITH
    active_closing_date AS (
        SELECT
            id,
            closing_day
        FROM master_closing_date
        WHERE is_active = true
        ORDER BY updated_date DESC NULLS LAST, created_date DESC NULLS LAST
        LIMIT 1
    ),
    latest_verified AS (
        SELECT DISTINCT ON (ta.spbu_id) 
            ta.spbu_id,
            ta.app_user_id,
            ta.audit_level,
            COALESCE(ta.audit_execution_time, ta.audit_schedule_date::timestamp) AS last_verified_date,
            ta.audit_execution_time,
            ta.good_status, 
            ta.excellent_status,
            ta.boa_status,
            ta.app_user_id_auditor2,
            ta.form_type_auditor1,
            ta.form_type_auditor2
        FROM trx_audit ta
        WHERE ta.status IN ('VERIFIED')
        ORDER BY ta.spbu_id, ta.audit_execution_time DESC, ta.audit_schedule_date DESC
    ),
    latest_verified_with_next_audit AS (
        SELECT 
            lv.*,
            CASE 
                WHEN lv.good_status = 'CERTIFIED' OR lv.boa_status = 'CERTIFIED' THEN 
                    COALESCE(
                        maf.passed_audit_level,
                        CASE 
                            WHEN lv.excellent_status = 'CERTIFIED' THEN maf.passed_excellent
                            WHEN lv.good_status = 'CERTIFIED' THEN maf.passed_good
                        END
                    )
                ELSE 
                    maf.failed_audit_level
            END AS audit_next
        FROM latest_verified lv
        JOIN master_audit_flow maf ON maf.audit_level = lv.audit_level
    ),    
    latest_progress AS (
        SELECT DISTINCT ON (ta.spbu_id)
            ta.spbu_id,
            ta.audit_schedule_date AS last_progress_date
        FROM trx_audit ta
        WHERE ta.status IN ('DRAFT','NOT_STARTED','IN_PROGRESS_INPUT','IN_PROGRESS_SUBMIT','UNDER_REVIEW')
        ORDER BY ta.spbu_id, ta.audit_schedule_date DESC, ta.audit_execution_time DESC
    ),
    latest_master_questioner AS (
        SELECT DISTINCT ON (mq.type, mq.category)
               mq.id,
               mq.type,
               mq.category,
               mq.version
        FROM master_questioner mq
        ORDER BY mq.type, mq.category, mq.version DESC
    ),
    data AS (
        SELECT
            s.spbu_no,
            s.id AS spbu_id,
            lvna.app_user_id,
            lvna.app_user_id_auditor2,
            lvna.form_type_auditor1,
            lvna.form_type_auditor2,
            lvna.audit_next,
            lvna.last_verified_date,
            lvna.audit_execution_time,
            lp.last_progress_date,
            maf.range_audit_month,
            maf.audit_type,
            lmqi.id AS master_questioner_intro_id,
            lmqc.id AS master_questioner_checklist_id
        FROM spbu s
        INNER JOIN latest_verified_with_next_audit lvna
            ON lvna.spbu_id = s.id
        INNER JOIN master_audit_flow maf
            ON maf.audit_level = lvna.audit_next
           AND maf.range_audit_month IS NOT NULL
        LEFT JOIN latest_master_questioner lmqi
            ON lmqi.type = maf.audit_type AND lmqi.category = 'INTRO'
        LEFT JOIN latest_master_questioner lmqc
            ON lmqc.type = maf.audit_type AND lmqc.category = 'CHECKLIST'
        LEFT JOIN latest_progress lp
            ON lp.spbu_id = s.id
        WHERE lp.last_progress_date IS NULL
          AND lvna.last_verified_date IS NOT NULL
    ),
    distributed AS (
        SELECT
            d.*,
            ROW_NUMBER() OVER (
                PARTITION BY d.app_user_id, d.range_audit_month
                ORDER BY d.spbu_no
            ) AS rn
        FROM data d
    ),
    scheduled AS (
        SELECT
            d.*,
            (
                date_trunc('month', d.last_verified_date + (d.range_audit_month || ' month')::interval)
                + (
                    (
                        (d.rn - 1) % CAST(
                            EXTRACT(
                                DAY FROM (
                                    date_trunc('month', d.last_verified_date + (d.range_audit_month || ' month')::interval)
                                    + interval '1 month - 1 day'
                                )
                            ) AS int
                        )
                    ) * interval '1 day'
                )
            )::date AS generated_audit_schedule_date
        FROM distributed d
    )
    SELECT
        uuid_generate_v4() AS id,
        '' AS report_prefix,
        '' AS report_no,
        sch.spbu_id,
        sch.app_user_id,
        sch.master_questioner_intro_id,
        sch.master_questioner_checklist_id,
        sch.audit_next AS audit_level,
        sch.audit_type,
        sch.generated_audit_schedule_date AS audit_schedule_date,
        NULL AS audit_execution_time,
        0 AS audit_media_upload,
        0 AS audit_media_total,
        '' AS audit_mom_intro,
        '' AS audit_mom_final,
        'DRAFT' AS status,
        'SYSTEM-AUTO-SCHEDULE' AS created_by,
        current_timestamp AS created_date,
        'SYSTEM-AUTO-SCHEDULE' AS updated_by,
        current_timestamp AS updated_date,
        NULL AS approval_by,
        NULL AS approval_date,
        NULL AS good_status,
        NULL AS excellent_status,
        0 AS score,
        NULL AS report_file_good,
        NULL AS report_file_excellent,
        NULL AS report_file_boa,
        NULL AS boa_status,

        -- Jika master closing date kosong, master_closing_date_id akan NULL.
        acd.id AS master_closing_date_id,

        -- Jika master closing date kosong, default ke tanggal terakhir bulan audit.
        -- Jika master berisi 31 tapi bulan hanya sampai 30/28/29, otomatis pakai tanggal terakhir bulan tersebut.
        (
            date_trunc('month', sch.generated_audit_schedule_date::timestamp)::date
            + (
                LEAST(
                    COALESCE(
                        acd.closing_day,
                        EXTRACT(
                            DAY FROM (
                                date_trunc('month', sch.generated_audit_schedule_date::timestamp)
                                + interval '1 month - 1 day'
                            )
                        )::int
                    ),
                    EXTRACT(
                        DAY FROM (
                            date_trunc('month', sch.generated_audit_schedule_date::timestamp)
                            + interval '1 month - 1 day'
                        )
                    )::int
                ) - 1
            )
        )::date AS closing_date,

        sch.app_user_id_auditor2,
        sch.form_type_auditor1,
        sch.form_type_auditor2,
        'DRAFT' AS form_status_auditor1,
        CASE 
            WHEN sch.app_user_id_auditor2 IS NULL THEN NULL
            ELSE 'DRAFT'
        END AS form_status_auditor2
    FROM scheduled sch
    LEFT JOIN active_closing_date acd ON true
    ORDER BY sch.app_user_id, sch.range_audit_month, sch.rn";

    public AuditAutoSchedulerService(IServiceProvider services,
                                     ILogger<AuditAutoSchedulerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime now = JakartaNow();
                DateTime nextRun = NextRunMonthlyAt0100(now);

                var delay = nextRun - now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EpasDbContext>();
                await CreateSchedulerAsync(db, stoppingToken);

                _logger.LogInformation("CreateScheduler dieksekusi pada {Time}", JakartaNow());

                // hitung jadwal 1 bulan berikutnya jam 01:00 WIB
                var wait = NextRunMonthlyAt0100(JakartaNow()) - JakartaNow();
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, stoppingToken);
            }
        }
        catch (TaskCanceledException)
        {
            // normal saat shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pada AuditAutoSchedulerService.");
        }
    }

    private static DateTime JakartaNow()
    {
        try
        {
            // Windows
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTime(DateTime.Now, tz);
        }
        catch
        {
            // Linux
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta");
            return TimeZoneInfo.ConvertTime(DateTime.Now, tz);
        }
    }

    private static DateTime NextRunMonthlyAt0100(DateTime from)
    {
        var firstThisMonth0100 = new DateTime(from.Year, from.Month, 1, 1, 0, 0);
        return (from <= firstThisMonth0100)
            ? firstThisMonth0100
            : new DateTime(from.Year, from.Month, 1, 1, 0, 0).AddMonths(1);
    }

    private static async Task CreateSchedulerAsync(EpasDbContext db, CancellationToken ct = default)
    {
        DbConnection conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // SOFT DELETE
        await using (var tx1 = await conn.BeginTransactionAsync(ct))
        {
            try
            {
                const string deleteSql = @"
                    UPDATE trx_audit 
                    SET 
                        status = 'DELETED',
                        form_status_auditor1 = 'DELETED',
                        form_status_auditor2 = 'DELETED',
                        updated_by = 'SYSTEM-AUTO-SCHEDULE-DELETE',
                        updated_date = current_timestamp
                    WHERE status = 'DRAFT';
                ";

                await conn.ExecuteAsync(deleteSql, transaction: tx1);
                await tx1.CommitAsync(ct);
            }
            catch
            {
                await tx1.RollbackAsync(ct);
                throw;
            }
        }

        // INSERT
        await using (var tx2 = await conn.BeginTransactionAsync(ct))
        {
            try
            {
                await conn.ExecuteAsync(CreateSchedulerSql, transaction: tx2, commandTimeout: 600);
                await tx2.CommitAsync(ct);
            }
            catch
            {
                await tx2.RollbackAsync(ct);
                throw;
            }
        }
    }
}