using System.Data;
using System.Data.Common;
using Dapper;
using e_Pas_CMS.Data;
using Microsoft.EntityFrameworkCore;

public class FixingStatusAutoSchedulerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FixingStatusAutoSchedulerService> _logger;

    private const string ExistsSql = @"
        SELECT EXISTS (
            SELECT 1
            FROM trx_audit
            WHERE status != form_status_auditor1
              AND form_status_auditor2 IS NULL
        );
    ";

    private const string UpdateSql = @"
        UPDATE trx_audit 
        SET form_status_auditor1 = status
        WHERE status != form_status_auditor1 
          AND form_status_auditor2 IS NULL;
    ";

    public FixingStatusAutoSchedulerService(IServiceProvider services,
                                            ILogger<FixingStatusAutoSchedulerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FixingStatusAutoSchedulerService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EpasDbContext>();

                await ExecuteProcessAsync(db, stoppingToken);

                _logger.LogInformation(
                    "FixingStatusAutoSchedulerService finished check at {Time}",
                    DateTime.Now
                );

                // interval 15 menit
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // normal stop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FixingStatusAutoSchedulerService");

                // avoid infinite crash loop
                await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
            }
        }
    }

    private static async Task ExecuteProcessAsync(EpasDbContext db, CancellationToken ct)
    {
        DbConnection conn = db.Database.GetDbConnection();

        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // 1️⃣ Cek dulu apakah ada data yang perlu di-update
        bool hasData = await conn.ExecuteScalarAsync<bool>(ExistsSql);

        if (!hasData)
        {
            return; // tidak ada data, skip update
        }

        // 2️⃣ Jika ada, jalankan update dalam transaksi
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            int affected = await conn.ExecuteAsync(UpdateSql, transaction: tx);

            await tx.CommitAsync(ct);

            Console.WriteLine($"Rows updated: {affected}");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
