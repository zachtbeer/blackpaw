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
    private string? _initializationError;

    /// <summary>
    /// Indicates whether the container was successfully initialized.
    /// Tests should check this before attempting to use the container.
    /// </summary>
    public bool IsAvailable => _container != null && _initializationError == null;

    /// <summary>
    /// Error message if initialization failed, null otherwise.
    /// </summary>
    public string? InitializationError => _initializationError;

    /// <summary>
    /// The SQL Server container instance.
    /// </summary>
    public MsSqlContainer Container => _container
        ?? throw new InvalidOperationException($"Container not initialized. {_initializationError ?? "Ensure InitializeAsync has been called."}");

    /// <summary>
    /// Connection string for the SQL Server container.
    /// Includes TrustServerCertificate=true for container connections.
    /// </summary>
    public string ConnectionString
    {
        get
        {
            if (!IsAvailable)
                throw new InvalidOperationException($"Container not available: {_initializationError}");

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
        try
        {
            _container = new MsSqlBuilder()
                .WithPassword("YourStrong@Passw0rd!")
                .Build();

            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            _initializationError = $"Failed to start SQL Server container: {ex.Message}";
            _container = null;
        }
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
