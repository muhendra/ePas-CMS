using e_Pas_CMS.Data;
using Dapper;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

public class BandingClosingService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BandingClosingService> _logger;

    public BandingClosingService(IServiceProvider services, ILogger<BandingClosingService> logger)
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
                DateTime now = DateTime.Now;
                DateTime today6am = now.Date.AddHours(6);
                DateTime nextRunTime = now <= today6am ? today6am : now.Date.AddDays(1).AddHours(6);
                TimeSpan initialDelay = nextRunTime - now;

                if (initialDelay > TimeSpan.Zero)
                    await Task.Delay(initialDelay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                using (var scope = _services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<EpasDbContext>();
                    await UpdateBandingDeadlinesAsync(db, stoppingToken);
                }

                _logger.LogInformation("BandingClosingService finished at {Time}", DateTime.Now);

                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pada BandingClosingService.");
        }
    }

    // -- SQL: siapkan nilai-nilai hari dari sys_parameter, isi next_audit_before, lalu auto-close yang lewat tenggat
    private static readonly string UpsertNextAuditAndAutocloseSql = @"
WITH sp AS (
    SELECT code, (NULLIF(TRIM(value), '')::int) AS days
    FROM sys_parameter
    WHERE status = 'ACTIVE'
),
days_map AS (
    SELECT
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_SPBU'),  (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_spbu,
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_SBM'),   (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_sbm,
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_PPN'),   (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_ppn,
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_CBI'),   (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_cbi,
        (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') AS d_all
)
-- 1) Update next_audit_before (YYYY-MM-DD)
UPDATE trx_feedback tf
SET next_audit_before = TO_CHAR(
        (tf.created_date::date
         + (
                CASE
                    WHEN tf.status = 'UNDER_REVIEW' THEN (SELECT d_spbu FROM days_map)
                    WHEN tf.status = 'APPROVE_SBM'  THEN (SELECT d_sbm  FROM days_map)
                    WHEN tf.status = 'APPROVE_PPN'  THEN (SELECT d_ppn  FROM days_map)
                    WHEN tf.status = 'APPROVE_CBI'  THEN (SELECT d_cbi  FROM days_map)
                    ELSE COALESCE((SELECT d_all FROM days_map), 0)
                END
            ) * INTERVAL '1 day'
        ), 'YYYY-MM-DD'
    ),
    updated_by = 'SYSTEM',
    updated_date = CURRENT_TIMESTAMP
WHERE tf.feedback_type = 'BANDING'
  AND tf.status IS NOT NULL;

-- 2) Auto-close yang lewat tenggat (opsional, tetap dalam transaksi yang sama)
WITH sp AS (
    SELECT code, (NULLIF(TRIM(value), '')::int) AS days
    FROM sys_parameter
    WHERE status = 'ACTIVE'
),
days_map AS (
    SELECT
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_SPBU'),  (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_spbu,
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_SBM'),   (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_sbm,
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_PPN'),   (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_ppn,
        COALESCE( (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_CBI'),   (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') ) AS d_cbi,
        (SELECT days FROM sp WHERE code = 'BANDING_CLOSING_DATE_ALL') AS d_all
)
UPDATE trx_feedback tf
SET status = 'CLOSED',
    updated_by = 'SYSTEM',
    updated_date = CURRENT_TIMESTAMP
WHERE tf.feedback_type = 'BANDING'
  AND tf.status IN ('UNDER_REVIEW','APPROVE_SBM','APPROVE_PPN','APPROVE_CBI')
  AND (
        tf.created_date::date
        + (
            CASE
                WHEN tf.status = 'UNDER_REVIEW' THEN (SELECT d_spbu FROM days_map)
                WHEN tf.status = 'APPROVE_SBM'  THEN (SELECT d_sbm  FROM days_map)
                WHEN tf.status = 'APPROVE_PPN'  THEN (SELECT d_ppn  FROM days_map)
                WHEN tf.status = 'APPROVE_CBI'  THEN (SELECT d_cbi  FROM days_map)
                ELSE COALESCE((SELECT d_all FROM days_map), 0)
            END
        ) * INTERVAL '1 day'
      ) < CURRENT_DATE
  AND tf.status <> 'CLOSED';
";

    private async Task UpdateBandingDeadlinesAsync(EpasDbContext db, CancellationToken ct = default)
    {
        DbConnection conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // Jalankan 1 batch: update next_audit_before + auto-close (dalam 1 transaksi)
            await conn.ExecuteAsync(UpsertNextAuditAndAutocloseSql, transaction: tx);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
