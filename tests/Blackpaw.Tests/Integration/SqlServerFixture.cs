using Testcontainers.MsSql;
using Xunit;

namespace Blackpaw.Tests.Integration;

/// <summary>
/// Shared SQL Server container fixture for integration tests.
/// Uses IAsyncLifetime to manage container lifecycle.
/// The container is shared across all tests in the collection to minimize startup time.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    /// <summary>
    /// The SQL Server container instance.
    /// </summary>
    public MsSqlContainer Container => _container
        ?? throw new InvalidOperationException("Container not initialized. Ensure InitializeAsync has been called.");

    /// <summary>
    /// Connection string for the SQL Server container.
    /// Includes TrustServerCertificate=true for container connections.
    /// </summary>
    public string ConnectionString
    {
        get
        {
            var baseConnStr = Container.GetConnectionString();
            // Ensure TrustServerCertificate is set for container connections
            if (!baseConnStr.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
            {
                baseConnStr += ";TrustServerCertificate=true";
            }
            return baseConnStr;
        }
    }

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong@Passw0rd!")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>
/// Collection definition for sharing the SQL Server container across tests.
/// Tests in this collection will share a single SQL Server container instance.
/// </summary>
[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    // This class has no code - it's a marker for xUnit to identify the collection
}
