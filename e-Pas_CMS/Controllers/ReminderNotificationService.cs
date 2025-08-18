using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using e_Pas_CMS.Data;
using Dapper;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

public class ReminderNotificationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReminderNotificationService> _logger;

    public ReminderNotificationService(IServiceProvider services,
                                       ILogger<ReminderNotificationService> logger)
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
                // 1. Hitung waktu menuju jam 18:00 hari ini
                DateTime now = DateTime.Now;
                DateTime today6pm = now.Date.AddHours(18); // hari ini jam 18:00
                DateTime nextRunTime = now <= today6pm ? today6pm : now.Date.AddDays(1).AddHours(18);
                TimeSpan initialDelay = nextRunTime - now;

                // 2. Delay hingga waktu 18:00 berikutnya
                if (initialDelay > TimeSpan.Zero)
                    await Task.Delay(initialDelay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                // 3. Jalankan tugas reminder (Langkah 3 di bawah)
                using (var scope = _services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<EpasDbContext>();
                    await KirimReminderH1Async(db);  // metode terpisah untuk insert notifikasi
                }

                _logger.LogInformation("Reminder H-1 telah dikirim pada {Time}", DateTime.Now);

                // 4. Tunggu 24 jam hingga jadwal berikutnya
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pada ReminderNotificationService.");
            // Exception akan menghentikan loop; bisa ditambahkan handling restart loop jika diperlukan.
        }
    }

    private static readonly string ReminderH1Sql = @"
    INSERT INTO notification (
        id,
        app_user_id,
        title,
        message,
        status,
        created_by,
        created_date,
        updated_by,
        updated_date
    )
    INSERT INTO notification (
    id,
    app_user_id,
    title,
    message,
    status,
    created_by,
    created_date,
    updated_by,
    updated_date
)
SELECT DISTINCT ON (ta.app_user_id)
    uuid_generate_v4(),
    ta.app_user_id,
    'Audit Segera Dimulai',
    'Audit anda akan segera dimulai ' ||
    EXTRACT(DAY FROM ta.audit_schedule_date) || ' ' ||
    CASE EXTRACT(MONTH FROM ta.audit_schedule_date)
        WHEN 1 THEN 'Januari'
        WHEN 2 THEN 'Februari'
        WHEN 3 THEN 'Maret'
        WHEN 4 THEN 'April'
        WHEN 5 THEN 'Mei'
        WHEN 6 THEN 'Juni'
        WHEN 7 THEN 'Juli'
        WHEN 8 THEN 'Agustus'
        WHEN 9 THEN 'September'
        WHEN 10 THEN 'Oktober'
        WHEN 11 THEN 'November'
        WHEN 12 THEN 'Desember'
    END || ' ' || EXTRACT(YEAR FROM ta.audit_schedule_date) ||
    ', persiapkan perlengkapan audit anda',
    'UNREAD',
    'SYSTEM',
    CURRENT_TIMESTAMP,
    'SYSTEM',
    CURRENT_TIMESTAMP
FROM trx_audit ta
WHERE ta.audit_schedule_date = CURRENT_DATE + INTERVAL '1 day' and ta.status = 'NOT_STARTED';";

    private async Task KirimReminderH1Async(EpasDbContext db, CancellationToken ct = default)
    {
        DbConnection conn = db.Database.GetDbConnection();

        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await conn.ExecuteAsync(ReminderH1Sql, transaction: tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
