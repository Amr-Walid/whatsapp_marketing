IF OBJECT_ID(N'[__ef_migrations_history]') IS NULL
BEGIN
    CREATE TABLE [__ef_migrations_history] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___ef_migrations_history] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [campaigns] (
        [id] bigint NOT NULL IDENTITY,
        [name] nvarchar(160) NOT NULL,
        [segment] nvarchar(40) NULL,
        [intent_name] nvarchar(40) NOT NULL,
        [body_template] nvarchar(max) NULL,
        [status] nvarchar(20) NOT NULL,
        [estimated_cost_usd] decimal(12,4) NOT NULL,
        [planned_official] int NOT NULL,
        [planned_unofficial] int NOT NULL,
        [planned_skipped] int NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [PK_campaigns] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [cost_ledger] (
        [id] bigint NOT NULL IDENTITY,
        [day] date NOT NULL,
        [channel] int NOT NULL,
        [meta_category] int NOT NULL,
        [country_code] nvarchar(4) NOT NULL,
        [msg_count] int NOT NULL,
        [delivered] int NOT NULL,
        [cost_usd] decimal(14,6) NOT NULL,
        [bsp_fee_usd] decimal(14,6) NOT NULL,
        CONSTRAINT [PK_cost_ledger] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [customers] (
        [id] bigint NOT NULL IDENTITY,
        [phone] nvarchar(20) NOT NULL,
        [name] nvarchar(120) NULL,
        [segment] nvarchar(40) NULL,
        [opted_in] bit NOT NULL,
        [opt_in_source] nvarchar(60) NULL,
        [opted_in_at] datetimeoffset NULL,
        [opted_out] bit NOT NULL,
        [opted_out_at] datetimeoffset NULL,
        [preferred_channel] int NULL,
        [official_opt_in] bit NOT NULL,
        [official_opt_in_at] datetimeoffset NULL,
        [ctwa_clid] nvarchar(200) NULL,
        [acquisition_source] int NOT NULL,
        [last_channel_used] int NULL,
        [monetary] decimal(14,2) NOT NULL,
        [frequency] int NOT NULL,
        [recency_days] int NOT NULL,
        [priority] int NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [PK_customers] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [message_log] (
        [id] bigint NOT NULL IDENTITY,
        [campaign_id] bigint NULL,
        [customer_id] bigint NOT NULL,
        [phone] nvarchar(20) NOT NULL,
        [direction] int NOT NULL,
        [channel] int NOT NULL,
        [intent] nvarchar(40) NOT NULL,
        [window_state] int NOT NULL,
        [send_mode] int NOT NULL,
        [template_name] nvarchar(120) NULL,
        [meta_category] int NOT NULL,
        [idempotency_key] nvarchar(40) NULL,
        [content] nvarchar(max) NULL,
        [cost_estimated] decimal(12,6) NOT NULL,
        [cost_billed] decimal(12,6) NULL,
        [route_reason] nvarchar(max) NULL,
        [fallback_from] int NULL,
        [status] int NOT NULL,
        [wa_message_id] nvarchar(120) NULL,
        [error_code] nvarchar(20) NULL,
        [error_message] nvarchar(max) NULL,
        [session_id] nvarchar(60) NULL,
        [delay_used_ms] int NULL,
        [created_at] datetimeoffset NOT NULL,
        [sent_at] datetimeoffset NULL,
        [delivered_at] datetimeoffset NULL,
        [read_at] datetimeoffset NULL,
        CONSTRAINT [PK_message_log] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [official_status] (
        [id] smallint NOT NULL IDENTITY,
        [phone_number_id] nvarchar(40) NULL,
        [tier] nvarchar(20) NOT NULL,
        [daily_limit] int NOT NULL,
        [used_today] int NOT NULL,
        [quality_rating] int NOT NULL,
        [reset_at] datetimeoffset NULL,
        [last_checked_at] datetimeoffset NULL,
        [notes] nvarchar(200) NULL,
        CONSTRAINT [PK_official_status] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [suppression_list] (
        [id] bigint NOT NULL IDENTITY,
        [phone] nvarchar(20) NOT NULL,
        [reason] nvarchar(30) NOT NULL,
        [seen_on_channel] int NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [PK_suppression_list] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [wa_sessions] (
        [id] bigint NOT NULL IDENTITY,
        [session_id] nvarchar(60) NOT NULL,
        [phone] nvarchar(20) NULL,
        [status] nvarchar(20) NOT NULL,
        [warmup_day] int NOT NULL,
        [daily_quota] int NOT NULL,
        [sent_today] int NOT NULL,
        [risk_score] int NOT NULL,
        [proxy_label] nvarchar(60) NULL,
        [last_seen_at] datetimeoffset NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [PK_wa_sessions] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [wa_templates] (
        [id] bigint NOT NULL IDENTITY,
        [name] nvarchar(120) NOT NULL,
        [language] nvarchar(10) NOT NULL,
        [category] int NOT NULL,
        [status] int NOT NULL,
        [quality] int NULL,
        [paused_until] datetimeoffset NULL,
        [body_text] nvarchar(max) NOT NULL,
        [header_kind] nvarchar(12) NULL,
        [footer_text] nvarchar(max) NULL,
        [required_params_json] nvarchar(max) NOT NULL,
        [intent] nvarchar(40) NULL,
        [meta_id] nvarchar(60) NULL,
        [rejected_reason] nvarchar(max) NULL,
        [submitted_at] datetimeoffset NULL,
        [approved_at] datetimeoffset NULL,
        [last_synced_at] datetimeoffset NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [PK_wa_templates] PRIMARY KEY ([id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE TABLE [customer_windows] (
        [id] bigint NOT NULL IDENTITY,
        [customer_id] bigint NOT NULL,
        [phone] nvarchar(20) NOT NULL,
        [kind] int NOT NULL,
        [opened_at] datetimeoffset NOT NULL,
        [expires_at] datetimeoffset NOT NULL,
        [opened_by] nvarchar(30) NOT NULL,
        [source_ref] nvarchar(120) NULL,
        [channel_seen] int NULL,
        [renew_count] int NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [PK_customer_windows] PRIMARY KEY ([id]),
        CONSTRAINT [FK_customer_windows_customers_customer_id] FOREIGN KEY ([customer_id]) REFERENCES [customers] ([id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_cost_ledger_day_channel_meta_category_country_code] ON [cost_ledger] ([day], [channel], [meta_category], [country_code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_customer_windows_customer_id_kind] ON [customer_windows] ([customer_id], [kind]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_customer_windows_phone_expires_at] ON [customer_windows] ([phone], [expires_at]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_customers_opted_in_opted_out] ON [customers] ([opted_in], [opted_out]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_customers_phone] ON [customers] ([phone]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_customers_segment] ON [customers] ([segment]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_message_log_campaign_id] ON [message_log] ([campaign_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_message_log_channel_created_at] ON [message_log] ([channel], [created_at]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_message_log_idempotency_key] ON [message_log] ([idempotency_key]) WHERE [idempotency_key] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_message_log_phone] ON [message_log] ([phone]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_suppression_list_phone] ON [suppression_list] ([phone]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_wa_sessions_session_id] ON [wa_sessions] ([session_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE INDEX [IX_wa_templates_intent_status] ON [wa_templates] ([intent], [status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_wa_templates_name] ON [wa_templates] ([name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__ef_migrations_history]
    WHERE [MigrationId] = N'20260831084013_InitialHybridSchema'
)
BEGIN
    INSERT INTO [__ef_migrations_history] ([MigrationId], [ProductVersion])
    VALUES (N'20260831084013_InitialHybridSchema', N'8.0.10');
END;
GO

COMMIT;
GO

