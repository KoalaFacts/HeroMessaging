-- Migration: Create Idempotency Responses Table for SQL Server
-- Version: 001
-- Date: 2025-11-07
-- Description: Creates the table for storing idempotency responses with indexes

-- Check if table exists
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[IdempotencyResponses]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[IdempotencyResponses] (
        -- Primary Key: Idempotency key generated from message
        [IdempotencyKey] NVARCHAR(450) NOT NULL,

        -- Response Status: 0 = Success, 1 = Failure
        [Status] TINYINT NOT NULL,

        -- Success Result: JSON-serialized result data
        [SuccessResult] NVARCHAR(MAX) NULL,

        -- Failure Information: Captured when Status = 1
        [FailureType] NVARCHAR(500) NULL,
        [FailureMessage] NVARCHAR(MAX) NULL,
        [FailureStackTrace] NVARCHAR(MAX) NULL,

        -- Timestamps: For TTL and cleanup
        [StoredAt] DATETIME2(7) NOT NULL,
        [ExpiresAt] DATETIME2(7) NOT NULL,

        CONSTRAINT [PK_IdempotencyResponses] PRIMARY KEY CLUSTERED ([IdempotencyKey] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    PRINT 'Table IdempotencyResponses created successfully.';
END
ELSE
BEGIN
    PRINT 'Table IdempotencyResponses already exists. Skipping creation.';
END
GO

-- Create index for cleanup operations (find expired entries)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyResponses_ExpiresAt'
               AND object_id = OBJECT_ID('IdempotencyResponses'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdempotencyResponses_ExpiresAt]
        ON [dbo].[IdempotencyResponses] ([ExpiresAt] ASC)
        WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
              DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
        ON [PRIMARY];

    PRINT 'Index IX_IdempotencyResponses_ExpiresAt created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_IdempotencyResponses_ExpiresAt already exists. Skipping creation.';
END
GO

-- Optional: Create index for status queries (if needed for monitoring)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyResponses_Status_StoredAt'
               AND object_id = OBJECT_ID('IdempotencyResponses'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdempotencyResponses_Status_StoredAt]
        ON [dbo].[IdempotencyResponses] ([Status] ASC, [StoredAt] DESC)
        INCLUDE ([ExpiresAt])
        WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
              DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
        ON [PRIMARY];

    PRINT 'Index IX_IdempotencyResponses_Status_StoredAt created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_IdempotencyResponses_Status_StoredAt already exists. Skipping creation.';
END
GO

PRINT 'Migration 001_CreateIdempotencyTable completed successfully.';
GO
