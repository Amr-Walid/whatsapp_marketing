using Microsoft.EntityFrameworkCore;
using WaHybrid.Domain.Entities;
using WaHybrid.Domain.Enums;

namespace WaHybrid.Infrastructure.Data;

/// <summary>
/// 🔑 قاعدة بيانات **واحدة** للقناتين. docs/09 §0.
///
/// لو عملتها اتنين هتعيش في جحيم: عميلين لنفس الشخص، تاريخ محادثة مقطوع،
/// عميل يستلم رسالة مرتين، و opt-out على قناة مش بيمشي على التانية.
///
/// المزوّد الأساسي = SQL Server. SQLite للتطوير والاختبار بنفس المخطط.
/// </summary>
public class HybridDbContext : DbContext
{
    public HybridDbContext(DbContextOptions<HybridDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerWindow> CustomerWindows => Set<CustomerWindow>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<WaTemplate> WaTemplates => Set<WaTemplate>();
    public DbSet<CostLedgerEntry> CostLedger => Set<CostLedgerEntry>();
    public DbSet<OfficialStatus> OfficialStatuses => Set<OfficialStatus>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<WaSession> WaSessions => Set<WaSession>();
    public DbSet<SuppressionEntry> SuppressionList => Set<SuppressionEntry>();

    /// <summary>true لو المزوّد SQLite — بعض الميزات مش مدعومة</summary>
    public bool IsSqlite => Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ═══════════════ Customers ═══════════════
        b.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Segment).HasMaxLength(40);
            e.Property(x => x.OptInSource).HasMaxLength(60);
            e.Property(x => x.CtwaClid).HasMaxLength(200);
            e.Property(x => x.Monetary).HasPrecision(14, 2);
            e.Property(x => x.PreferredChannel).HasConversion<int?>();
            e.Property(x => x.LastChannelUsed).HasConversion<int?>();
            e.Property(x => x.AcquisitionSource).HasConversion<int>();
            e.HasIndex(x => x.Segment);
            e.HasIndex(x => new { x.OptedIn, x.OptedOut });
        });

        // ═══════════════ CustomerWindows ═══════════════
        b.Entity<CustomerWindow>(e =>
        {
            e.ToTable("customer_windows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.OpenedBy).HasMaxLength(30).IsRequired();
            e.Property(x => x.SourceRef).HasMaxLength(120);
            e.Property(x => x.ChannelSeen).HasConversion<int?>();

            // 🔑 نافذة واحدة لكل نوع لكل عميل — بنحدّثها مش بنضيف صف
            e.HasIndex(x => new { x.CustomerId, x.Kind }).IsUnique();
            e.HasIndex(x => new { x.Phone, x.ExpiresAt });

            e.HasOne(x => x.Customer)
             .WithMany(c => c.Windows)
             .HasForeignKey(x => x.CustomerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ═══════════════ MessageLog ═══════════════
        b.Entity<MessageLog>(e =>
        {
            e.ToTable("message_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Direction).HasConversion<int>();
            e.Property(x => x.Channel).HasConversion<int>();
            e.Property(x => x.Intent).HasMaxLength(40).IsRequired();
            e.Property(x => x.WindowState).HasConversion<int>();
            e.Property(x => x.SendMode).HasConversion<int>();
            e.Property(x => x.MetaCategory).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.TemplateName).HasMaxLength(120);
            e.Property(x => x.IdempotencyKey).HasMaxLength(40);
            e.Property(x => x.WaMessageId).HasMaxLength(120);
            e.Property(x => x.ErrorCode).HasMaxLength(20);
            e.Property(x => x.SessionId).HasMaxLength(60);
            e.Property(x => x.FallbackFrom).HasConversion<int?>();
            e.Property(x => x.CostEstimated).HasPrecision(12, 6);
            e.Property(x => x.CostBilled).HasPrecision(12, 6);

            // 🔒 حزام أمان تاني ضد الإرسال المزدوج على مستوى قاعدة البيانات
            // (صياغة الـ filter بتتحدد حسب المزوّد في آخر الميثود)
            e.HasIndex(x => x.IdempotencyKey).IsUnique();

            e.HasIndex(x => new { x.Channel, x.CreatedAt });
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.CampaignId);
            e.Ignore(x => x.EffectiveCost);
        });

        // ═══════════════ WaTemplates ═══════════════
        b.Entity<WaTemplate>(e =>
        {
            e.ToTable("wa_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Language).HasMaxLength(10).IsRequired();
            e.Property(x => x.Category).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Quality).HasConversion<int?>();
            e.Property(x => x.Intent).HasMaxLength(40);
            e.Property(x => x.MetaId).HasMaxLength(60);
            e.Property(x => x.HeaderKind).HasMaxLength(12);
            e.HasIndex(x => new { x.Intent, x.Status });
        });

        // ═══════════════ CostLedger ═══════════════
        b.Entity<CostLedgerEntry>(e =>
        {
            e.ToTable("cost_ledger");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<int>();
            e.Property(x => x.MetaCategory).HasConversion<int>();
            e.Property(x => x.CountryCode).HasMaxLength(4);
            e.Property(x => x.CostUsd).HasPrecision(14, 6);
            e.Property(x => x.BspFeeUsd).HasPrecision(14, 6);
            e.HasIndex(x => new { x.Day, x.Channel, x.MetaCategory, x.CountryCode }).IsUnique();
        });

        // ═══════════════ OfficialStatus (صف واحد) ═══════════════
        b.Entity<OfficialStatus>(e =>
        {
            e.ToTable("official_status");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tier).HasMaxLength(20);
            e.Property(x => x.PhoneNumberId).HasMaxLength(40);
            e.Property(x => x.QualityRating).HasConversion<int>();
            e.Property(x => x.Notes).HasMaxLength(200);
            e.Ignore(x => x.MarketingPaused);
        });

        // ═══════════════ Campaigns ═══════════════
        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Segment).HasMaxLength(40);
            e.Property(x => x.IntentName).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.EstimatedCostUsd).HasPrecision(12, 4);
        });

        // ═══════════════ WaSessions ═══════════════
        b.Entity<WaSession>(e =>
        {
            e.ToTable("wa_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.SessionId).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.SessionId).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ProxyLabel).HasMaxLength(60);
            e.Ignore(x => x.IsHealthy);
        });

        // ═══════════════ SuppressionList ═══════════════
        b.Entity<SuppressionEntry>(e =>
        {
            e.ToTable("suppression_list");
            e.HasKey(x => x.Id);
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Reason).HasMaxLength(30);
            e.Property(x => x.SeenOnChannel).HasConversion<int?>();
        });

        // ⚠️ صياغة الـ filtered index بتختلف: SQL Server بيستخدم [brackets]
        //    و SQLite بيستخدم الاسم المجرّد. الاتنين بيسمحوا بأكتر من NULL كده.
        b.Entity<MessageLog>()
         .HasIndex(x => x.IdempotencyKey)
         .IsUnique()
         .HasFilter(IsSqlite
             ? "\"idempotency_key\" IS NOT NULL"
             : "[idempotency_key] IS NOT NULL");

        // ══════════════════════════════════════════════════════════════
        //  ⚠️ فرق مزوّد حقيقي لازم تعرفه: DateTimeOffset على SQLite
        // ══════════════════════════════════════════════════════════════
        // SQL Server عنده نوع `datetimeoffset` أصلي، فالترتيب والمقارنة
        // بيشتغلوا طبيعي.
        //
        // SQLite **مش** عنده النوع ده. EF بيخزّنه كنص ISO فيه الـ offset،
        // والنص ده **مش بيترتّب صح** لو فيه offsets مختلفة
        // (مثال: "2026-01-01T00:00:00+02:00" أكبر نصياً من
        //         "2026-01-01T00:00:00+00:00" رغم إن الأول أقدم زمنياً).
        // فـ EF بيرفض الترتيب خالص ويرمي NotSupportedException.
        //
        // 🔑 الحل: نخزّنه كـ long بـ DateTimeOffsetToBinaryConverter —
        //    وده **بيحافظ على الترتيب الزمني** لأنه بيرمّز التيكات بالـ UTC.
        //
        // ⚠️ ملاحظة مهمة للإنتاج: التحويل ده بيتطبّق على SQLite **بس**.
        //    مخطط SQL Server مايتغيّرش ولا حرف — نفس الكود، نفس الكويريز،
        //    وكل الترتيب والمقارنات بيشتغلوا على الاتنين.
        if (IsSqlite)
        {
            var dtoConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion
                .DateTimeOffsetToBinaryConverter();

            foreach (var entity in b.Model.GetEntityTypes())
            {
                foreach (var prop in entity.GetProperties())
                {
                    if (prop.ClrType == typeof(DateTimeOffset)
                     || prop.ClrType == typeof(DateTimeOffset?))
                    {
                        prop.SetValueConverter(dtoConverter);
                    }
                }
            }
        }

        // ── snake_case لكل الأعمدة: بيخلّي المخطط مطابق للـ SQL في docs/09 §6 ──
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                prop.SetColumnName(ToSnakeCase(prop.Name));
            }
        }

        base.OnModelCreating(b);
    }

    /// <summary>IdempotencyKey → idempotency_key</summary>
    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
