# Product Category and Barcode

Categories are flat master data protected by database `product_category.read/manage` permissions. Category assignment/removal on a Product requires `product.update`; barcode mutations require the independent `product_barcode.manage`; exact barcode lookup requires `product.read`. There are no Product/Category/Barcode permission aliases. Codes are trimmed, converted to uppercase, limited to 50 characters, and unique using SQL Server case-insensitive comparison. Inactive categories cannot be assigned to a product; existing products keep and display an inactive category. A category referenced by any product cannot be deleted.

Creating a product with a category and explicitly assigning a category both validate the active category inside a serializable database transaction. The foreign key uses restrictive delete behavior, so concurrent deletion cannot leave an invalid reference. Database uniqueness and foreign-key errors are returned as business conflicts.

Products may have multiple barcodes. Barcode values are trimmed, 1–64 characters, limited to ASCII letters, digits, `.`, `_`, and `-`. Values retain leading zeroes and letter case. Equality and the database unique index use binary case-sensitive comparison. Barcodes identify products only; they are not generated, validated as GTIN, treated as credentials, or associated with packaging or unit conversion.

The existing product PUT contract remains unchanged, so clients that omit the new fields preserve category and barcode data. Category assignment/removal and barcode addition/removal use explicit endpoints. Exact barcode lookup is separate from product-code search and returns product master data only, with no inventory quantities or warehouse data.

## Local full-stack QA checkpoint

Revision `cb35f8ed` plus the follow-up working-tree fixes was exercised on 2026-09-13 with Chrome, the real frontend and API on loopback, and an owned SQL Server Express database. Run ID `03e0d3d6fb8448388fa86e354c533b2b` used database `ERP_KHO_BrowserQA_03e0d3d6fb8448388fa86e354c533b2b`; the database carried an exact `LocalBrowserFullStackQA` ownership marker.

The browser run covered category create/search/edit, category normalization, inactive-category display and editing of another product field, multiple case-sensitive barcodes, leading zero preservation, exact lookup when a barcode equals another product code, scanner-style Enter, not-found state clearing, reload persistence, and Viewer read-only behavior. Real HTTP checks covered Admin/Manager mutation, Viewer mutation denial, invalid/duplicate/overlength barcodes, cross-product barcode deletion denial, inactive assignment denial, referenced-category deletion denial, and the legacy product PUT preserving category and barcode data. Catalog operations left inventory transactions, stock rows, and reservations unchanged at zero. Keyboard simulation verifies scanner-as-keyboard behavior; it is not a physical scanner test.

The run found and fixed an EF Core no-tracking include cycle in exact barcode lookup and a UI stale-result issue. The local harness binds only to `127.0.0.1`, records exact process/database ownership in its manifest, and refuses cleanup unless the database marker matches. Runtime evidence lives under ignored `TestResults/BrowserQA`; credentials and the owned database are removed during exact cleanup.
