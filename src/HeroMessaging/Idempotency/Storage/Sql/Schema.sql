-- Idempotency Response Storage Table
-- Compatible with SQL Server and PostgreSQL (with minor syntax adjustments)

-- SQL Server Version
CREATE TABLE IdempotencyResponses (
    -- Primary Key
    IdempotencyKey NVARCHAR(450) NOT NULL PRIMARY KEY,

    -- Response Status
    Status TINYINT NOT NULL, -- 0 = Success, 1 = Failure

    -- Success Data (JSON serialized)
    SuccessResult NVARCHAR(MAX) NULL,

    -- Failure Information
    FailureType NVARCHAR(500) NULL,
    FailureMessage NVARCHAR(MAX) NULL,
    FailureStackTrace NVARCHAR(MAX) NULL,

    -- Timestamp Information
    StoredAt DATETIME2 NOT NULL,
    ExpiresAt DATETIME2 NOT NULL,

    -- Indexes for performance
    INDEX IX_IdempotencyResponses_ExpiresAt NONCLUSTERED (ExpiresAt ASC)
);

-- PostgreSQL Version (adjust types)
/*
CREATE TABLE idempotency_responses (
    -- Primary Key
    idempotency_key VARCHAR(450) NOT NULL PRIMARY KEY,

    -- Response Status
    status SMALLINT NOT NULL, -- 0 = Success, 1 = Failure

    -- Success Data (JSON/JSONB)
    success_result JSONB NULL,

    -- Failure Information
    failure_type VARCHAR(500) NULL,
    failure_message TEXT NULL,
    failure_stack_trace TEXT NULL,

    -- Timestamp Information
    stored_at TIMESTAMP NOT NULL,
    expires_at TIMESTAMP NOT NULL
);

-- Index for cleanup operations
CREATE INDEX idx_idempotency_responses_expires_at
    ON idempotency_responses(expires_at);
*/
