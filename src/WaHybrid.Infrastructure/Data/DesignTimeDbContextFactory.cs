using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WaHybrid.Infrastructure.Data;

/// <summary>
/// 🏭 مصنع وقت التصميم — بيستخدمه <c>dotnet ef</c> بس، مش بيشتغل في الإنتاج.
///
/// ═══ ليه الملف ده موجود؟ ═══
/// الـ migrations في EF Core **مرتبطة بالمزوّد**. يعني migration اتولّد
/// من SQLite بيطلّع <c>TEXT</c> و <c>INTEGER</c>، واللي اتولّد من SQL Server
/// بيطلّع <c>nvarchar(20)</c> و <c>datetimeoffset</c> و <c>decimal(14,2)</c>.
/// الاتنين مش قابلين للتبديل.
///
/// وبما إن **SQL Server هو المزوّد الأساسي للإنتاج** (و SQLite للتطوير
/// والاختبار في الساندبوكس بس)، فإحنا:
///   • بنولّد migrations رسمية لـ SQL Server ← الملف ده بيجبر ده
///   • بنستخدم <c>EnsureCreated()</c> لـ SQLite ← مافيش migrations محتاجة
///
/// بكده الـ migrations اللي في الريبو هي نفسها اللي هتشتغل على سيرفر
/// الإنتاج بالظبط، بدون أي تعديل يدوي.
///
/// ⚠️ الـ connection string هنا **مش بيتصل بحاجة**. EF محتاجه بس عشان
/// يعرف يستخدم أنهي مزوّد ويولّد الـ SQL الصح. توليد الـ migration
/// مابيحتاجش قاعدة بيانات شغّالة (وده لطيف، لأن مافيش SQL Server
/// في الساندبوكس أصلاً).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HybridDbContext>
{
    public HybridDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HybridDbContext>()
            // نفس اسم المخطط اللي هيستخدمه الإنتاج — الأسماء مهمة
            // لأنها بتظهر في الـ migration المتولّد.
            .UseSqlServer(
                "Server=localhost;Database=WaHybrid;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__ef_migrations_history"))
            .Options;

        return new HybridDbContext(options);
    }
}
