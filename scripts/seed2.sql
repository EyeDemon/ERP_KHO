-- Disabled per docs/SEED_USAGE_POLICY.md: unguarded DELETE statements against ERP_KHO are prohibited.
-- Use tools/ERP.QaSeed (--apply) for controlled local QA seeding instead.
THROW 51099, N'Direct execution of scripts/seed2.sql is prohibited by docs/SEED_USAGE_POLICY.md. Use tools/ERP.QaSeed instead.', 1;

