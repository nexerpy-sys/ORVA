-- Migration 003: Foreign-key performance indexes for high-volume pharmacy registers,
-- historical invoice detail lookups, cancellations, and sales returns.

CREATE INDEX IF NOT EXISTS ix_invoice_line_invoice
    ON invoice_line(invoice_id);

CREATE INDEX IF NOT EXISTS ix_payment_invoice
    ON payment(invoice_id);

CREATE INDEX IF NOT EXISTS ix_sales_return_invoice
    ON sales_return(invoice_id);

CREATE INDEX IF NOT EXISTS ix_sales_return_line_return
    ON sales_return_line(return_id);

CREATE INDEX IF NOT EXISTS ix_sales_return_line_invoice_line
    ON sales_return_line(invoice_line_id);

CREATE INDEX IF NOT EXISTS ix_purchase_line_purchase
    ON purchase_line(purchase_id);
