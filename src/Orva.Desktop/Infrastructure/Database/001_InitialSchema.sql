-- ORVA permanent schema, migration 001.
-- Quantities are stored as milli-units (1.000 unit = 1000).
-- Monetary amounts are stored as integer paise. Never store money as REAL.

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS app_meta (
    meta_key TEXT PRIMARY KEY,
    meta_value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS shop_profile (
    profile_id INTEGER PRIMARY KEY CHECK (profile_id = 1),
    shop_id TEXT NOT NULL UNIQUE,
    shop_name TEXT NOT NULL,
    legal_name TEXT,
    address TEXT,
    phone TEXT,
    email TEXT,
    state_name TEXT,
    state_code TEXT,
    gstin TEXT,
    drug_licence_number TEXT,
    invoice_prefix TEXT NOT NULL DEFAULT 'INV',
    financial_year_start_month INTEGER NOT NULL DEFAULT 4 CHECK (financial_year_start_month BETWEEN 1 AND 12),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_setting (
    setting_key TEXT PRIMARY KEY,
    setting_value TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS medicine (
    medicine_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    normalized_name TEXT NOT NULL UNIQUE,
    generic_name TEXT,
    brand_name TEXT,
    strength TEXT,
    dosage_form TEXT,
    stock_unit_name TEXT NOT NULL DEFAULT 'unit',
    hsn_code TEXT,
    gst_rate_basis_points INTEGER NOT NULL DEFAULT 0 CHECK (gst_rate_basis_points BETWEEN 0 AND 10000),
    low_stock_threshold_milli INTEGER NOT NULL DEFAULT 0 CHECK (low_stock_threshold_milli >= 0),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS barcode_mapping (
    barcode TEXT PRIMARY KEY,
    medicine_id TEXT NOT NULL REFERENCES medicine(medicine_id),
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS stock_batch (
    batch_id TEXT PRIMARY KEY,
    medicine_id TEXT NOT NULL REFERENCES medicine(medicine_id),
    batch_number TEXT NOT NULL,
    expiry_yyyymm INTEGER NOT NULL CHECK (expiry_yyyymm BETWEEN 200001 AND 219912),
    stock_unit_name TEXT NOT NULL,
    mrp_paise INTEGER NOT NULL CHECK (mrp_paise >= 0),
    latest_purchase_price_paise INTEGER NOT NULL CHECK (latest_purchase_price_paise >= 0),
    quantity_milli INTEGER NOT NULL DEFAULT 0 CHECK (quantity_milli >= 0),
    batch_status TEXT NOT NULL DEFAULT 'ACTIVE' CHECK (batch_status IN ('ACTIVE', 'QUARANTINED', 'EXPIRED', 'BLOCKED')),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    UNIQUE (medicine_id, batch_number, expiry_yyyymm)
);

CREATE TABLE IF NOT EXISTS purchase_receipt (
    purchase_id TEXT PRIMARY KEY,
    received_at_utc TEXT NOT NULL,
    supplier_name_text TEXT,
    supplier_licence_text TEXT,
    document_type TEXT NOT NULL CHECK (document_type IN ('TAX_INVOICE', 'CASH_CREDIT_MEMO', 'NO_DOCUMENT', 'OTHER')),
    document_reference TEXT,
    notes TEXT,
    subtotal_paise INTEGER NOT NULL DEFAULT 0 CHECK (subtotal_paise >= 0),
    total_tax_paise INTEGER NOT NULL DEFAULT 0 CHECK (total_tax_paise >= 0),
    total_paise INTEGER NOT NULL DEFAULT 0 CHECK (total_paise >= 0),
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS purchase_line (
    purchase_line_id TEXT PRIMARY KEY,
    purchase_id TEXT NOT NULL REFERENCES purchase_receipt(purchase_id),
    batch_id TEXT NOT NULL REFERENCES stock_batch(batch_id),
    medicine_id TEXT NOT NULL REFERENCES medicine(medicine_id),
    quantity_milli INTEGER NOT NULL CHECK (quantity_milli > 0),
    unit_name_snapshot TEXT NOT NULL,
    unit_purchase_price_paise INTEGER NOT NULL CHECK (unit_purchase_price_paise >= 0),
    line_total_paise INTEGER NOT NULL CHECK (line_total_paise >= 0),
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS invoice_sequence (
    financial_year TEXT PRIMARY KEY,
    last_number INTEGER NOT NULL DEFAULT 0 CHECK (last_number >= 0)
);

CREATE TABLE IF NOT EXISTS invoice (
    invoice_id TEXT PRIMARY KEY,
    invoice_number TEXT NOT NULL UNIQUE,
    financial_year TEXT NOT NULL,
    issued_at_utc TEXT NOT NULL,
    issued_local_offset_minutes INTEGER NOT NULL,
    customer_name TEXT,
    customer_phone TEXT,
    customer_address TEXT,
    customer_gstin TEXT,
    place_of_supply_state_code TEXT,
    subtotal_paise INTEGER NOT NULL CHECK (subtotal_paise >= 0),
    discount_paise INTEGER NOT NULL DEFAULT 0 CHECK (discount_paise >= 0),
    taxable_value_paise INTEGER NOT NULL CHECK (taxable_value_paise >= 0),
    cgst_paise INTEGER NOT NULL DEFAULT 0 CHECK (cgst_paise >= 0),
    sgst_paise INTEGER NOT NULL DEFAULT 0 CHECK (sgst_paise >= 0),
    igst_paise INTEGER NOT NULL DEFAULT 0 CHECK (igst_paise >= 0),
    round_off_paise INTEGER NOT NULL DEFAULT 0,
    grand_total_paise INTEGER NOT NULL CHECK (grand_total_paise >= 0),
    status TEXT NOT NULL DEFAULT 'ISSUED' CHECK (status IN ('ISSUED', 'CANCELLED', 'CREDIT_NOTE_ISSUED')),
    cancellation_reason TEXT,
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS invoice_line (
    invoice_line_id TEXT PRIMARY KEY,
    invoice_id TEXT NOT NULL REFERENCES invoice(invoice_id),
    line_group_id TEXT NOT NULL,
    medicine_id TEXT NOT NULL REFERENCES medicine(medicine_id),
    batch_id TEXT NOT NULL REFERENCES stock_batch(batch_id),
    medicine_name_snapshot TEXT NOT NULL,
    batch_number_snapshot TEXT NOT NULL,
    expiry_yyyymm_snapshot INTEGER NOT NULL,
    quantity_milli INTEGER NOT NULL CHECK (quantity_milli > 0),
    unit_name_snapshot TEXT NOT NULL,
    unit_price_paise INTEGER NOT NULL CHECK (unit_price_paise >= 0),
    discount_paise INTEGER NOT NULL DEFAULT 0 CHECK (discount_paise >= 0),
    taxable_value_paise INTEGER NOT NULL CHECK (taxable_value_paise >= 0),
    gst_rate_basis_points INTEGER NOT NULL DEFAULT 0 CHECK (gst_rate_basis_points BETWEEN 0 AND 10000),
    cgst_paise INTEGER NOT NULL DEFAULT 0 CHECK (cgst_paise >= 0),
    sgst_paise INTEGER NOT NULL DEFAULT 0 CHECK (sgst_paise >= 0),
    igst_paise INTEGER NOT NULL DEFAULT 0 CHECK (igst_paise >= 0),
    line_total_paise INTEGER NOT NULL CHECK (line_total_paise >= 0),
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS payment (
    payment_id TEXT PRIMARY KEY,
    invoice_id TEXT NOT NULL REFERENCES invoice(invoice_id),
    method TEXT NOT NULL CHECK (method IN ('CASH', 'UPI', 'DEBIT_CARD', 'CREDIT_CARD')),
    amount_paise INTEGER NOT NULL CHECK (amount_paise > 0),
    reference_text TEXT,
    paid_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sales_return (
    return_id TEXT PRIMARY KEY,
    return_number TEXT NOT NULL UNIQUE,
    invoice_id TEXT NOT NULL REFERENCES invoice(invoice_id),
    returned_at_utc TEXT NOT NULL,
    reason TEXT NOT NULL,
    refund_method TEXT CHECK (refund_method IN ('CASH', 'UPI', 'DEBIT_CARD', 'CREDIT_CARD', 'ADJUSTMENT')),
    refund_amount_paise INTEGER NOT NULL DEFAULT 0 CHECK (refund_amount_paise >= 0),
    status TEXT NOT NULL DEFAULT 'COMPLETED' CHECK (status IN ('COMPLETED', 'CANCELLED')),
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sales_return_line (
    return_line_id TEXT PRIMARY KEY,
    return_id TEXT NOT NULL REFERENCES sales_return(return_id),
    invoice_line_id TEXT NOT NULL REFERENCES invoice_line(invoice_line_id),
    batch_id TEXT NOT NULL REFERENCES stock_batch(batch_id),
    quantity_milli INTEGER NOT NULL CHECK (quantity_milli > 0),
    resale_disposition TEXT NOT NULL CHECK (resale_disposition IN ('QUARANTINE', 'RESTOCK', 'DAMAGED', 'EXPIRED')),
    line_refund_paise INTEGER NOT NULL DEFAULT 0 CHECK (line_refund_paise >= 0)
);

CREATE TABLE IF NOT EXISTS stock_movement (
    movement_id TEXT PRIMARY KEY,
    batch_id TEXT NOT NULL REFERENCES stock_batch(batch_id),
    medicine_id TEXT NOT NULL REFERENCES medicine(medicine_id),
    movement_type TEXT NOT NULL CHECK (movement_type IN ('OPENING', 'PURCHASE', 'SALE', 'SALE_RETURN_RESTOCK', 'DAMAGE', 'EXPIRY_WRITE_OFF', 'ADJUSTMENT', 'IMPORT')),
    quantity_delta_milli INTEGER NOT NULL CHECK (quantity_delta_milli <> 0),
    reference_type TEXT,
    reference_id TEXT,
    reason TEXT,
    created_at_utc TEXT NOT NULL,
    created_local_text TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS stock_discrepancy (
    discrepancy_id TEXT PRIMARY KEY,
    batch_id TEXT NOT NULL REFERENCES stock_batch(batch_id),
    expected_quantity_milli INTEGER NOT NULL,
    counted_quantity_milli INTEGER NOT NULL CHECK (counted_quantity_milli >= 0),
    difference_milli INTEGER NOT NULL,
    reason TEXT NOT NULL,
    reconciled_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_log (
    audit_id TEXT PRIMARY KEY,
    action_type TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    summary TEXT NOT NULL,
    before_json TEXT,
    after_json TEXT,
    created_at_utc TEXT NOT NULL,
    created_local_text TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS expense_record (
    expense_id TEXT PRIMARY KEY,
    expense_date_utc TEXT NOT NULL,
    category TEXT NOT NULL,
    description TEXT,
    amount_paise INTEGER NOT NULL CHECK (amount_paise > 0),
    payment_method TEXT CHECK (payment_method IN ('CASH', 'UPI', 'DEBIT_CARD', 'CREDIT_CARD')),
    document_reference TEXT,
    created_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_medicine_name ON medicine(normalized_name);
CREATE UNIQUE INDEX IF NOT EXISTS ux_batch_normalized_identity ON stock_batch(medicine_id, UPPER(TRIM(batch_number)), expiry_yyyymm);
CREATE INDEX IF NOT EXISTS ix_batch_medicine_expiry ON stock_batch(medicine_id, expiry_yyyymm);
CREATE INDEX IF NOT EXISTS ix_purchase_date ON purchase_receipt(received_at_utc);
CREATE INDEX IF NOT EXISTS ix_invoice_date ON invoice(issued_at_utc);
CREATE INDEX IF NOT EXISTS ix_invoice_financial_year ON invoice(financial_year, invoice_number);
CREATE INDEX IF NOT EXISTS ix_return_date ON sales_return(returned_at_utc);
CREATE INDEX IF NOT EXISTS ix_stock_movement_batch_date ON stock_movement(batch_id, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_audit_entity_date ON audit_log(entity_type, entity_id, created_at_utc);

INSERT OR IGNORE INTO app_meta(meta_key, meta_value) VALUES ('schema_version', '1');