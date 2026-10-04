using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
            await AssertCommentaryApiWorksAsync(app, api.Name, cancellationToken);
        }
    }

    private static async Task AssertCommentaryApiWorksAsync(
        DistributedApplication app, string apiResourceName, CancellationToken cancellationToken)
    {
        using HttpClient client = app.CreateHttpClient(apiResourceName, "https");
        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/comments", new
        {
            username = "Denis",
            email = "denis@example.com",
            text = "Created after AppHost migrations."
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        JsonElement created = await create.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Guid id = created.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);

        Uri location = Assert.IsType<Uri>(create.Headers.Location);
        using HttpResponseMessage read = await client.GetAsync(location, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        JsonElement reloaded = await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(id, reloaded.GetProperty("id").GetGuid());

        using HttpResponseMessage list = await client.GetAsync("/api/comments?page=1", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        JsonElement page = await list.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(25, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
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
