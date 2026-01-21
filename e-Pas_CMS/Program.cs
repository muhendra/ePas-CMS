using e_Pas_CMS.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Kestrel config (port + timeout) + allow sync IO (untuk ZipArchive.Dispose central directory)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(5050);

    // timeout "max" (realistis) untuk request panjang / download zip besar
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromHours(12);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);

    serverOptions.AllowSynchronousIO = true;
});

builder.Services.Configure<IISServerOptions>(o =>
{
    o.AllowSynchronousIO = true;
});

QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container
builder.Services.AddControllersWithViews();

// Register EF DbContext
builder.Services.AddDbContext<EpasDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add session support
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("https://epas-cms.zarata.co.id", "https://localhost:7084")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHostedService<ReminderNotificationService>();
builder.Services.AddHostedService<AuditAutoSchedulerService>();
builder.Services.AddHostedService<FixingStatusAutoSchedulerService>();

var app = builder.Build();

// Middleware
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/privacy-policy", StringComparison.OrdinalIgnoreCase))
        context.Request.Path = "/privacy-policy.html";
    await next();
});

app.UseCors("AllowFrontend");

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
