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
        app_user_id_auditor2,
        form_type_auditor1,
		form_type_auditor2,
		form_status_auditor1,
		form_status_auditor2
    )
    WITH
    latest_verified AS (
        SELECT DISTINCT ON (ta.spbu_id)
            ta.spbu_id,
            ta.app_user_id,
            COALESCE(ta.audit_execution_time, ta.audit_schedule_date) AS last_verified_date,
            ta.audit_execution_time,
            ta.app_user_id_auditor2,
            ta.form_type_auditor1,
	        ta.form_type_auditor2
        FROM trx_audit ta
        WHERE ta.status IN ('VERIFIED')
        ORDER BY ta.spbu_id, ta.audit_execution_time DESC, ta.audit_schedule_date DESC
    ),
    latest_progress AS (
        SELECT DISTINCT ON (ta.spbu_id)
            ta.spbu_id,
            ta.audit_schedule_date AS last_progress_date
        FROM trx_audit ta
        WHERE ta.status IN ('DRAFT','NOT_STARTED','IN_PROGRESS_INPUT','IN_PROGRESS_SUBMIT','UNDER_REVIEW')
        ORDER BY ta.spbu_id, ta.audit_schedule_date desc, ta.audit_execution_time DESC
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
            s.id as spbu_id,
            lv.app_user_id,
            lv.app_user_id_auditor2,
            lv.form_type_auditor1,
	        lv.form_type_auditor2,
            s.audit_next,
            lv.audit_execution_time,
            lp.last_progress_date,
            maf.range_audit_month,
            maf.audit_type,
            lmqi.id as master_questioner_intro_id,
            lmqc.id as master_questioner_checklist_id
        FROM spbu s
        INNER JOIN master_audit_flow maf
            ON maf.audit_level = s.audit_next
           AND maf.range_audit_month IS NOT null
        LEFT JOIN latest_master_questioner lmqi
            ON lmqi.type = maf.audit_type AND lmqi.category = 'INTRO'
        LEFT JOIN latest_master_questioner lmqc
            ON lmqc.type = maf.audit_type AND lmqc.category = 'CHECKLIST'
        LEFT JOIN latest_verified lv
            ON lv.spbu_id = s.id
        LEFT JOIN latest_progress lp
            ON lp.spbu_id = s.id
        WHERE lp.last_progress_date IS NULL
          AND lv.last_verified_date IS NOT NULL
    ),
    distributed AS (
        SELECT d.*,
               ROW_NUMBER() OVER (PARTITION BY d.app_user_id, d.range_audit_month
                                  ORDER BY d.spbu_no) AS rn
        FROM data d
    )
    SELECT uuid_generate_v4() as id,
           '' as report_prefix,
           '' as report_no,
           spbu_id,
           app_user_id,
           master_questioner_intro_id,
           master_questioner_checklist_id,
           audit_next as audit_level,
           audit_type,
           (
			  date_trunc('month', audit_execution_time + (range_audit_month || ' month')::interval)
			  + (((rn - 1) % CAST(
			        extract(day from date_trunc('month',
			           audit_execution_time + (range_audit_month || ' month')::interval)
			           + interval '1 month - 1 day'
			        ) AS int)
			     ) * interval '1 day')
		   )::date as audit_schedule_date,
           null as audit_execution_time,
           0 as audit_media_upload,
           0 as audit_media_total,
           '' as audit_mom_intro,
           '' as audit_mom_final,
           'DRAFT' as status,
           'SYSTEM-AUTO-SCHEDULE' as created_by,
           current_timestamp as created_date,
           'SYSTEM-AUTO-SCHEDULE' as updated_by,
           current_timestamp as updated_date,
           null as approval_by,
           null as approval_date,
           null as good_status,
           null as excellent_status,
           0 as score,
           null as report_file_good,
           null as report_file_excellent,
           null as report_file_boa,
           null as boa_status,
           app_user_id_auditor2,
           form_type_auditor1,
	       form_type_auditor2,
	       'DRAFT' as form_status_auditor1,
           CASE 
                WHEN app_user_id_auditor2 IS NULL THEN NULL
                ELSE 'DRAFT'
            END as form_status_auditor2
    FROM distributed
    ORDER BY app_user_id, range_audit_month, rn;";

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
                await conn.ExecuteAsync(CreateSchedulerSql, transaction: tx2);
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