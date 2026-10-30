namespace Cocinaris.IntegrationTests;

/// <summary>Container images shared by the integration tests.</summary>
internal static class TestImages
{
    /// <summary>
    /// PostgreSQL 18 with pgvector (PLAN-0055, ADR-0010): the migrations create the vector and
    /// pg_trgm extensions, so the plain postgres image no longer migrates.
    /// </summary>
    public const string PostgreSql = "pgvector/pgvector:0.8.7-pg18-trixie";
}
