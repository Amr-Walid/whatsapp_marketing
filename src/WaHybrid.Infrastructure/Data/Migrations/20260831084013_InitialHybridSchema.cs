using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaHybrid.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialHybridSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    segment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    intent_name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    body_template = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    planned_official = table.Column<int>(type: "int", nullable: false),
                    planned_unofficial = table.Column<int>(type: "int", nullable: false),
                    planned_skipped = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    channel = table.Column<int>(type: "int", nullable: false),
                    meta_category = table.Column<int>(type: "int", nullable: false),
                    country_code = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    msg_count = table.Column<int>(type: "int", nullable: false),
                    delivered = table.Column<int>(type: "int", nullable: false),
                    cost_usd = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    bsp_fee_usd = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    segment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    opted_in = table.Column<bool>(type: "bit", nullable: false),
                    opt_in_source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    opted_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    opted_out = table.Column<bool>(type: "bit", nullable: false),
                    opted_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    preferred_channel = table.Column<int>(type: "int", nullable: true),
                    official_opt_in = table.Column<bool>(type: "bit", nullable: false),
                    official_opt_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ctwa_clid = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    acquisition_source = table.Column<int>(type: "int", nullable: false),
                    last_channel_used = table.Column<int>(type: "int", nullable: true),
                    monetary = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    frequency = table.Column<int>(type: "int", nullable: false),
                    recency_days = table.Column<int>(type: "int", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    campaign_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    direction = table.Column<int>(type: "int", nullable: false),
                    channel = table.Column<int>(type: "int", nullable: false),
                    intent = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    window_state = table.Column<int>(type: "int", nullable: false),
                    send_mode = table.Column<int>(type: "int", nullable: false),
                    template_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    meta_category = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    cost_estimated = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    cost_billed = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    route_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    fallback_from = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    wa_message_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    error_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    session_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    delay_used_ms = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "official_status",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    phone_number_id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    tier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    daily_limit = table.Column<int>(type: "int", nullable: false),
                    used_today = table.Column<int>(type: "int", nullable: false),
                    quality_rating = table.Column<int>(type: "int", nullable: false),
                    reset_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_checked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_status", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppression_list",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    seen_on_channel = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppression_list", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wa_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    session_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    warmup_day = table.Column<int>(type: "int", nullable: false),
                    daily_quota = table.Column<int>(type: "int", nullable: false),
                    sent_today = table.Column<int>(type: "int", nullable: false),
                    risk_score = table.Column<int>(type: "int", nullable: false),
                    proxy_label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wa_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wa_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    category = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    quality = table.Column<int>(type: "int", nullable: true),
                    paused_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    body_text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    header_kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    footer_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    required_params_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    intent = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    meta_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    rejected_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wa_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_windows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    opened_by = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    source_ref = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    channel_seen = table.Column<int>(type: "int", nullable: true),
                    renew_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_windows", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_windows_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cost_ledger_day_channel_meta_category_country_code",
                table: "cost_ledger",
                columns: new[] { "day", "channel", "meta_category", "country_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_windows_customer_id_kind",
                table: "customer_windows",
                columns: new[] { "customer_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_windows_phone_expires_at",
                table: "customer_windows",
                columns: new[] { "phone", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_opted_in_opted_out",
                table: "customers",
                columns: new[] { "opted_in", "opted_out" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_phone",
                table: "customers",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_segment",
                table: "customers",
                column: "segment");

            migrationBuilder.CreateIndex(
                name: "IX_message_log_campaign_id",
                table: "message_log",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_log_channel_created_at",
                table: "message_log",
                columns: new[] { "channel", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_message_log_idempotency_key",
                table: "message_log",
                column: "idempotency_key",
                unique: true,
                filter: "[idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_log_phone",
                table: "message_log",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "IX_suppression_list_phone",
                table: "suppression_list",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wa_sessions_session_id",
                table: "wa_sessions",
                column: "session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wa_templates_intent_status",
                table: "wa_templates",
                columns: new[] { "intent", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_wa_templates_name",
                table: "wa_templates",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "cost_ledger");

            migrationBuilder.DropTable(
                name: "customer_windows");

            migrationBuilder.DropTable(
                name: "message_log");

            migrationBuilder.DropTable(
                name: "official_status");

            migrationBuilder.DropTable(
                name: "suppression_list");

            migrationBuilder.DropTable(
                name: "wa_sessions");

            migrationBuilder.DropTable(
                name: "wa_templates");

            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
