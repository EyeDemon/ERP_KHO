using Xunit;

// SQL Server LocalDB serializes some database-level DDL even when tests use
// distinct, owned database names. Parallel CREATE/DROP DATABASE operations
// caused timeouts in CI #607 while API integration fixtures were being
// created and disposed. Keep the full API suite deterministic until these
// fixtures can share a separately managed SQL test server.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
