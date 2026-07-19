using Xunit;

// Every WebApplicationFactory applies migrations to the same external PostgreSQL
// fixture. Running test classes concurrently races application startup against
// cleanup queries and can leave the shared schema only partially initialized.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
