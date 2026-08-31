using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WaHybrid.Api.Endpoints;
using WaHybrid.Infrastructure;
using WaHybrid.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════════
//  التركيب
// ═══════════════════════════════════════════════════════════════════════
builder.Services.AddWaHybrid(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "WhatsApp Hybrid API",
        Version = "v1",
        Description =
            "النظام الهجين لواتساب — دماغ واحدة وفمّين.\n\n"
            + "قاعدة بيانات واحدة، منطق واحد، طابور واحد، موجّه واحد، ومزوّدين اتنين.\n"
            + "مفيش كود فوق طبقة المزوّد يعرف إحنا بنستخدم أنهي قناة."
    });
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Encoder =
        System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // عربي مقروء
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ═══════════════════════════════════════════════════════════════════════
//  تهيئة قاعدة البيانات + البذر
// ═══════════════════════════════════════════════════════════════════════
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HybridDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    // ⚠️ في الإنتاج (SQL Server) بنستخدم Migrate() مش EnsureCreated().
    //    هنا SQLite للتطوير فـ EnsureCreated أسرع وأبسط.
    //
    //    الفرق مش تفصيلة: EnsureCreated بيبني المخطط من الموديل مباشرة
    //    ومابيسيبش أثر، فلو الموديل اتغيّر بعدين مافيش طريقة تحدّث قاعدة
    //    فيها بيانات. Migrate بيمشي على الـ migrations المرقّمة بالترتيب
    //    ويسجّلها في __ef_migrations_history — فالترقية بتحافظ على البيانات.
    if (db.IsSqlite)
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
        log.LogInformation("🗄️ الـ migrations اتطبّقت على SQL Server");

        // العروض (Views) مش جزء من الـ migrations لأن EF مابيولّدهاش.
        // بنطبّقها من الملف عشان تبقى نسخة واحدة في الريبو، ومكتوبة
        // بـ CREATE OR ALTER فتشغيلها أكتر من مرة مافيهوش مشكلة.
        await DbViews.ApplyAsync(db, app.Environment.ContentRootPath, log);
    }

    if (app.Configuration.GetValue("Demo:Seed", true))
        await DemoSeeder.SeedAsync(db, log);
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "WhatsApp Hybrid v1");
    c.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles();

// ═══════════════════════════════════════════════════════════════════════
//  المسارات
// ═══════════════════════════════════════════════════════════════════════
app.MapWindowEndpoints();
app.MapRoutingEndpoints();
app.MapSendEndpoints();
app.MapCampaignEndpoints();
app.MapDashboardEndpoints();
app.MapWebhookEndpoints();
app.MapOpsEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "wa-hybrid",
    at = DateTimeOffset.UtcNow,
    // بنرجّع المزوّد عشان الشاشة تقول للمدير إحنا شغّالين على إيه دلوقتي.
    // نفس الكود بالظبط بيشتغل على SQL Server — الفرق سطر في الإعدادات بس.
    stack = ".NET 8 / ASP.NET Core",
    dbProvider = app.Configuration["Database:Provider"] ?? "Sqlite"
})).WithTags("النظام");

app.Run();
