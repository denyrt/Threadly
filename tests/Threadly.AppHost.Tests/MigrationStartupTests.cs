using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Threadly.AppHost.Tests;

public sealed class MigrationStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Api_StartsOnlyAfterSuccessfulMigration(bool failMigration)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));

        CancellationToken cancellationToken = deadline.Token;
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Threadly_AppHost>(cancellationToken);

        RemoveFrontendResources(builder);
        RemoveSqlDataMounts(builder);

        ProjectResource migrations = GetProjectResource(builder, "threadly-migrations");
        ProjectResource api = GetProjectResource(builder, "threadly-api");
        builder.CreateResourceBuilder(api).WithHttpHealthCheck("/health", endpointName: "https");

        WaitAnnotation migrationDependency = Assert.Single(api.Annotations.OfType<WaitAnnotation>());
        Assert.Same(migrations, migrationDependency.Resource);
        Assert.Equal(WaitType.WaitForCompletion, migrationDependency.WaitType);
        Assert.Equal(0, migrationDependency.ExitCode);

        if (failMigration)
        {
            builder.CreateResourceBuilder(migrations).WithEnvironment("Migration__Timeout", "00:00:00");
        }

        await using DistributedApplication app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        ResourceEvent completion = await app.ResourceNotifications.WaitForResourceAsync(
            migrations.Name,
            resourceEvent => resourceEvent.Snapshot.ExitCode.HasValue,
            cancellationToken);

        if (failMigration)
        {
            Assert.NotEqual(0, completion.Snapshot.ExitCode);
            await AssertApiDoesNotStartAsync(app, api.Name, cancellationToken);
        }
        else
        {
            Assert.Equal(0, completion.Snapshot.ExitCode);
            await app.ResourceNotifications.WaitForResourceHealthyAsync(api.Name, cancellationToken);
        }
    }

    private static void RemoveFrontendResources(IDistributedApplicationTestingBuilder builder)
    {
        // Remove the frontend and its npm installer to avoid invoking the frontend toolchain.
        IResource[] frontendResources = builder.Resources
            .Where(resource => resource.Name.StartsWith("threadly-web", StringComparison.Ordinal))
            .ToArray();

        foreach (IResource resource in frontendResources)
        {
            builder.Resources.Remove(resource);
        }
    }

    private static void RemoveSqlDataMounts(IDistributedApplicationTestingBuilder builder)
    {
        // Each test should use a fresh database rather than the development data volume.
        SqlServerServerResource sql = Assert.IsType<SqlServerServerResource>(
            builder.Resources.Single(resource => resource.Name == "sql"));

        foreach (ContainerMountAnnotation mount in sql.Annotations.OfType<ContainerMountAnnotation>().ToArray())
        {
            sql.Annotations.Remove(mount);
        }
    }

    private static ProjectResource GetProjectResource(IDistributedApplicationTestingBuilder builder, string name)
    {
        return Assert.IsType<ProjectResource>(builder.Resources.Single(resource => resource.Name == name));
    }

    private static async Task AssertApiDoesNotStartAsync(
        DistributedApplication app,
        string apiResourceName,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource observationWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observationWindow.CancelAfter(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.ResourceNotifications.WaitForResourceAsync(
            apiResourceName,
            KnownResourceStates.Running,
            observationWindow.Token));
    }
}
