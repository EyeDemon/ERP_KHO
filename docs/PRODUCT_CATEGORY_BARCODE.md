# Product Category and Barcode

Categories are flat master data managed by the existing Admin/Manager catalog permission. Codes are trimmed, converted to uppercase, limited to 50 characters, and unique using SQL Server case-insensitive comparison. Inactive categories cannot be assigned to a product; existing products keep and display an inactive category. A category referenced by any product cannot be deleted.

Creating a product with a category and explicitly assigning a category both validate the active category inside a serializable database transaction. The foreign key uses restrictive delete behavior, so concurrent deletion cannot leave an invalid reference. Database uniqueness and foreign-key errors are returned as business conflicts.

Products may have multiple barcodes. Barcode values are trimmed, 1–64 characters, limited to ASCII letters, digits, `.`, `_`, and `-`. Values retain leading zeroes and letter case. Equality and the database unique index use binary case-sensitive comparison. Barcodes identify products only; they are not generated, validated as GTIN, treated as credentials, or associated with packaging or unit conversion.

The existing product PUT contract remains unchanged, so clients that omit the new fields preserve category and barcode data. Category assignment/removal and barcode addition/removal use explicit endpoints. Exact barcode lookup is separate from product-code search and returns product master data only, with no inventory quantities or warehouse data.
