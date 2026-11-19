-- ORVA permanent schema, migration 002.
-- Adds explicit medicine GST configuration tracking and immutable HSN snapshot on invoice lines.
-- Does not destructively modify migration 001.

PRAGMA foreign_keys = ON;

ALTER TABLE medicine
    ADD COLUMN gst_rate_configured INTEGER NOT NULL DEFAULT 0 CHECK (gst_rate_configured IN (0, 1));

ALTER TABLE invoice_line
    ADD COLUMN hsn_code_snapshot TEXT;

UPDATE app_meta
SET meta_value = '2'
WHERE meta_key = 'schema_version';

INSERT OR IGNORE INTO app_meta(meta_key, meta_value)
VALUES ('schema_version', '2');