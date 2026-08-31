using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace WaHybrid.Infrastructure.Data;

/// <summary>
/// 🗂️ تطبيق عروض المتابعة (Views) على SQL Server.
///
/// ═══ ليه العروض مش جوّه الـ migrations؟ ═══
/// EF Core مابيولّدش views من الموديل — هو بيعرف الجداول بس. فأمامنا
/// اختيارين:
///   ١) نكتب الـ SQL جوّه ملف migration بـ migrationBuilder.Sql(...)
///   ٢) نسيبها في ملف .sql مستقل ونطبّقها عند الإقلاع
///
/// اخترنا (٢) لسببين عمليين:
///   • الـ DBA بيقدر يفتح الملف ويقراه ويراجعه بدون ما يعرف C#
///   • العروض بتتعدّل كتير (تقرير جديد، عمود جديد)، ولو كانت في
///     migrations هنولّد migration جديد كل شهر على تعديل تقرير
///
/// وبما إن الملف مكتوب بـ CREATE OR ALTER VIEW، تشغيله مية مرة =
/// تشغيله مرة. مافيش خطر ولا حاجة محتاجة حراسة.
///
/// ⚠️ SQLite مابيدعمش CREATE OR ALTER ولا SUM(CASE) بنفس الصياغة،
/// وكذلك مافيهوش GO. فالدالة دي بتشتغل على SQL Server بس، والداشبورد
/// في التطوير بيحسب نفس الأرقام في C# (DashboardEndpoints).
/// </summary>
public static class DbViews
{
    /// <summary>اسم ملف العروض — نسخة واحدة في الريبو</summary>
    public const string ViewsFileName = "002_views_sqlserver.sql";

    /// <summary>
    /// بيقرا ملف العروض ويطبّقه على قاعدة البيانات.
    /// بيقسّم على <c>GO</c> لأن SQL Server مابيقبلش أكتر من CREATE VIEW
    /// في نفس الـ batch — و<c>GO</c> ده فاصل batch بيفهمه sqlcmd بس،
    /// مش أمر T-SQL، فلازم إحنا نقسّم بإيدينا.
    /// </summary>
    /// <param name="contentRoot">جذر المشروع — بندور من عنده على db/migrations</param>
    public static async Task ApplyAsync(
        HybridDbContext db, string contentRoot, ILogger log,
        CancellationToken ct = default)
    {
        if (db.IsSqlite)
        {
            log.LogInformation("⏭️ العروض اتخطّت — SQLite مش بيدعم الصياغة دي");
            return;
        }

        var path = FindViewsFile(contentRoot);
        if (path is null)
        {
            // مش خطأ قاتل: النظام بيشتغل تمام بدون العروض، هي للتقارير بس.
            log.LogWarning("⚠️ ملف العروض {File} ملقيتوش — النظام هيشتغل بدونه", ViewsFileName);
            return;
        }

        var sql = await File.ReadAllTextAsync(path, ct);

        // التقسيم على GO كسطر لوحده (case-insensitive) — مش على كلمة "go"
        // اللي ممكن تكون جوّه تعليق أو اسم عمود.
        var batches = System.Text.RegularExpressions.Regex
            .Split(sql, @"^\s*GO\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline
                | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            // بنشيل الـ batches اللي كلها تعليقات — مافيش داعي نبعتها للسيرفر
            .Where(b => !IsOnlyComments(b))
            .ToList();

        var applied = 0;
        foreach (var batch in batches)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(batch, ct);
                applied++;
            }
            catch (Exception ex)
            {
                // بنسجّل ونكمّل: عرض واحد باظ مايوقّفش النظام كله.
                log.LogError(ex, "❌ فشل تطبيق batch من ملف العروض");
            }
        }

        log.LogInformation("🗂️ العروض اتطبّقت: {Applied}/{Total} batch من {File}",
            applied, batches.Count, Path.GetFileName(path));
    }

    /// <summary>
    /// بيدوّر على db/migrations/002_views_sqlserver.sql.
    /// بنطلع لفوق شوية لأن الـ ContentRoot وقت التشغيل بيبقى
    /// src/WaHybrid.Api مش جذر الريبو.
    /// </summary>
    private static string? FindViewsFile(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);

        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "db", "migrations", ViewsFileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>batch كله تعليقات؟ (سطور -- أو بلوك /* */)</summary>
    private static bool IsOnlyComments(string batch)
    {
        var stripped = System.Text.RegularExpressions.Regex
            .Replace(batch, @"/\*.*?\*/", "",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        return stripped
            .Split('\n')
            .Select(l => l.Trim())
            .All(l => l.Length == 0 || l.StartsWith("--"));
    }
}
