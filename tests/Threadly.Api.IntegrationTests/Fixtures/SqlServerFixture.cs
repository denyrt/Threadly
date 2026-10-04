using Testcontainers.MsSql;
using Threadly.Api.IntegrationTests.Fixtures;
using Xunit;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace Threadly.Api.IntegrationTests.Fixtures;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04")
        .WithEnvironment("MSSQL_PID", "Developer")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return container.DisposeAsync();
    }
}
