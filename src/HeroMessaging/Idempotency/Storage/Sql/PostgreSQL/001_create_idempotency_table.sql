-- Migration: Create Idempotency Responses Table for PostgreSQL
-- Version: 001
-- Date: 2025-11-07
-- Description: Creates the table for storing idempotency responses with indexes

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS idempotency_responses (
    -- Primary Key: Idempotency key generated from message
    idempotency_key VARCHAR(450) NOT NULL,

    -- Response Status: 0 = Success, 1 = Failure
    status SMALLINT NOT NULL,

    -- Success Result: JSONB for efficient querying
    success_result JSONB NULL,

    -- Failure Information: Captured when status = 1
    failure_type VARCHAR(500) NULL,
    failure_message TEXT NULL,
    failure_stack_trace TEXT NULL,

    -- Timestamps: For TTL and cleanup
    stored_at TIMESTAMP NOT NULL,
    expires_at TIMESTAMP NOT NULL,

    CONSTRAINT pk_idempotency_responses PRIMARY KEY (idempotency_key)
);

-- Add comment for documentation
COMMENT ON TABLE idempotency_responses IS 'Stores idempotency responses for exactly-once processing semantics';
COMMENT ON COLUMN idempotency_responses.idempotency_key IS 'Unique key identifying the cached response (format: idempotency:{MessageId})';
COMMENT ON COLUMN idempotency_responses.status IS 'Response status: 0 = Success, 1 = Failure';
COMMENT ON COLUMN idempotency_responses.success_result IS 'JSON-serialized success result data';
COMMENT ON COLUMN idempotency_responses.failure_type IS 'Fully qualified exception type name for failures';
COMMENT ON COLUMN idempotency_responses.failure_message IS 'Exception message for failures';
COMMENT ON COLUMN idempotency_responses.stored_at IS 'UTC timestamp when response was cached';
COMMENT ON COLUMN idempotency_responses.expires_at IS 'UTC timestamp when cache entry expires';

-- Create index for cleanup operations (find expired entries)
CREATE INDEX IF NOT EXISTS idx_idempotency_responses_expires_at
    ON idempotency_responses(expires_at);

-- Create index for status and timestamp queries (monitoring)
CREATE INDEX IF NOT EXISTS idx_idempotency_responses_status_stored_at
    ON idempotency_responses(status, stored_at DESC)
    INCLUDE (expires_at);

-- Optional: Create partial index for active (non-expired) entries
CREATE INDEX IF NOT EXISTS idx_idempotency_responses_active
    ON idempotency_responses(idempotency_key)
    WHERE expires_at > NOW();

DO $$
BEGIN
    RAISE NOTICE 'Migration 001_create_idempotency_table completed successfully.';
END $$;
