IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLog] (
        [Id] bigint NOT NULL IDENTITY,
        [ActorEmail] VARCHAR(320) NULL,
        [Action] VARCHAR(100) NOT NULL,
        [EntityType] VARCHAR(64) NOT NULL,
        [EntityId] VARCHAR(100) NULL,
        [DetailsJson] NVARCHAR(MAX) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Audit] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [AuthToken] (
        [Id] uniqueidentifier NOT NULL,
        [Email] VARCHAR(320) NOT NULL,
        [TokenHash] CHAR(64) NOT NULL,
        [Purpose] TINYINT NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RedeemedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuthToken] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [BlobRef] (
        [Id] uniqueidentifier NOT NULL,
        [BlobPath] VARCHAR(400) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [RefCount] int NOT NULL CONSTRAINT [DF_BlobRef_C] DEFAULT 1,
        [CreatedAtUtc] datetime2 NOT NULL,
        [PhysicallyDeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_BlobRef] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [DownloadEvent] (
        [Id] bigint NOT NULL IDENTITY,
        [TransferId] uniqueidentifier NOT NULL,
        [FileId] uniqueidentifier NULL,
        [IpHash] VARCHAR(64) NULL,
        [Country] CHAR(2) NULL,
        [UaHash] VARCHAR(64) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_DownEvent] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [EmailSuppression] (
        [Id] uniqueidentifier NOT NULL,
        [Address] VARCHAR(320) NOT NULL,
        [SenderEmail] VARCHAR(320) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ES] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [FeatureFlag] (
        [Key] VARCHAR(100) NOT NULL,
        [Value] NVARCHAR(MAX) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Flag] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [IdempotencyKey] (
        [Key] VARCHAR(128) NOT NULL,
        [ResultJson] NVARCHAR(MAX) NULL,
        [TransferId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Idem] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [JobRun] (
        [JobKey] VARCHAR(64) NOT NULL,
        [RunAtUtc] datetime2 NOT NULL,
        [LockUntilUtc] datetime2 NOT NULL,
        [FinishedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_JobRun] PRIMARY KEY ([JobKey], [RunAtUtc])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [Plan] (
        [Id] uniqueidentifier NOT NULL,
        [Code] VARCHAR(32) NOT NULL,
        [Name] VARCHAR(64) NOT NULL,
        [LimitsJson] NVARCHAR(MAX) NOT NULL,
        [FeaturesJson] NVARCHAR(MAX) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_Plan_Sort] DEFAULT 0,
        CONSTRAINT [PK_Plan] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [StripeEvent] (
        [StripeEventId] VARCHAR(100) NOT NULL,
        [ProcessedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_StripeEvent] PRIMARY KEY ([StripeEventId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [AppUser] (
        [Id] uniqueidentifier NOT NULL,
        [Email] VARCHAR(320) NOT NULL,
        [EmailConfirmed] bit NOT NULL CONSTRAINT [DF_AppUser_Conf] DEFAULT CAST(0 AS bit),
        [DisplayName] NVARCHAR(100) NOT NULL,
        [PasswordHash] VARCHAR(256) NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Theme] VARCHAR(16) NOT NULL CONSTRAINT [DF_AppUser_Theme] DEFAULT 'system',
        [CreatedAtUtc] datetime2 NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_AppUser] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppUser_Plan] FOREIGN KEY ([PlanId]) REFERENCES [Plan] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [Subscription] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [StripeSubId] VARCHAR(100) NOT NULL,
        [StripeCustomerId] VARCHAR(100) NULL,
        [PlanCode] VARCHAR(32) NOT NULL,
        [Status] VARCHAR(32) NOT NULL,
        [CurrentPeriodEndUtc] datetime2 NULL,
        [GraceEndsAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Sub] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sub_U] FOREIGN KEY ([AppUserId]) REFERENCES [AppUser] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [Transfer] (
        [Id] uniqueidentifier NOT NULL,
        [LinkId] CHAR(8) NOT NULL,
        [OwnerAppUserId] uniqueidentifier NULL,
        [Status] TINYINT NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [MaxDownloads] int NOT NULL,
        [DownloadsCount] int NOT NULL CONSTRAINT [DF_T_Downloads] DEFAULT 0,
        [PasswordHash] VARCHAR(256) NULL,
        [SenderName] NVARCHAR(100) NOT NULL,
        [SenderEmail] VARCHAR(320) NULL,
        [Note] NVARCHAR(500) NULL,
        [ScheduledSendAtUtc] datetime2 NULL,
        [TotalBytes] bigint NOT NULL CONSTRAINT [DF_T_Bytes] DEFAULT CAST(0 AS bigint),
        [FileCount] int NOT NULL CONSTRAINT [DF_T_FileCount] DEFAULT 0,
        [SupersededBy] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiredAtUtc] datetime2 NULL,
        [DeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Transfer] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_T_Super] FOREIGN KEY ([SupersededBy]) REFERENCES [Transfer] ([Id]),
        CONSTRAINT [FK_Transfer_Owner] FOREIGN KEY ([OwnerAppUserId]) REFERENCES [AppUser] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [EmailRecipient] (
        [Id] uniqueidentifier NOT NULL,
        [TransferId] uniqueidentifier NOT NULL,
        [Address] VARCHAR(320) NOT NULL,
        [NotifiedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ER] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ER_T] FOREIGN KEY ([TransferId]) REFERENCES [Transfer] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE TABLE [FileItem] (
        [Id] uniqueidentifier NOT NULL,
        [TransferId] uniqueidentifier NOT NULL,
        [BlobRefId] uniqueidentifier NOT NULL,
        [OriginalName] NVARCHAR(500) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [ContentType] VARCHAR(200) NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_FileItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FileItem_B] FOREIGN KEY ([BlobRefId]) REFERENCES [BlobRef] ([Id]),
        CONSTRAINT [FK_FileItem_T] FOREIGN KEY ([TransferId]) REFERENCES [Transfer] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_AppUser_Email] ON [AppUser] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_AuthToken_Hash] ON [AuthToken] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_BlobRef_Cleanup] ON [BlobRef] ([PhysicallyDeletedAtUtc]) WHERE RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_BlobRef_Path] ON [BlobRef] ([BlobPath]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DownEvent_T] ON [DownloadEvent] ([TransferId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_ER] ON [EmailRecipient] ([TransferId], [Address]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UQ_ES] ON [EmailSuppression] ([Address], [SenderEmail]) WHERE [SenderEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FileItem_T] ON [FileItem] ([TransferId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_FileItem_T_B] ON [FileItem] ([TransferId], [BlobRefId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Plan_Code] ON [Plan] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Sub_Stripe] ON [Subscription] ([StripeSubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Transfer_Expiry] ON [Transfer] ([Status], [ExpiresAtUtc]) INCLUDE ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Transfer_Owner] ON [Transfer] ([OwnerAppUserId], [CreatedAtUtc] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Transfer_Sched] ON [Transfer] ([Status], [ScheduledSendAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Transfer_LinkId] ON [Transfer] ([LinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011213139_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261011213139_InitialCreate', N'10.0.12');
END;

COMMIT;
GO


