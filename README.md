# Threadly

Threadly is a fullstack SPA built as a test assignment, inspired by Threads: a minimalist app for posts, comments, and nested conversations.

**Stack:** .NET 10, ASP.NET Core, EF Core 10, SQL Server 2022, Angular 22, TypeScript, Aspire, Docker, and Nginx.

**Build:** from the repository root, run `dotnet build Threadly.slnx --configuration Release`. For the frontend, run `npm --prefix src/threadly-web ci`, then `npm --prefix src/threadly-web run build`.

**Tests:** run `dotnet test --solution Threadly.slnx --configuration Release --ignore-exit-code 8`; for the frontend, run `npm --prefix src/threadly-web run test:ci`. Exit code 8 is allowed while some C# test projects are empty.

## Root comments API

Start Docker with Linux containers, then run `dotnet run --project orchestration/Threadly.AppHost`. AppHost starts SQL Server, provisions the database, runs the migration worker, and starts the API only after migrations finish successfully. Find the API URL in the Aspire dashboard. Request examples are in `src/Threadly.Api/Threadly.Api.http`.

| Operation | Endpoint | Response |
| --- | --- | --- |
| Create | `POST /api/comments` | `201 Created`, comment DTO, and a `Location` pointing to the new comment |
| Get by ID | `GET /api/comments/{id}` | `200 OK` with the DTO, or `404 Not Found` with ProblemDetails |
| Get page | `GET /api/comments?page=1` | `200 OK` with `{ items, page, pageSize, totalCount }` |

The create payload contains `username`, `email`, and `text`. Username is required, contains only ASCII letters and digits, and has at most 32 characters. Email is required, must be a valid email address, and has at most 254 characters. Text is required and has at most 2000 characters; surrounding whitespace is trimmed by the domain model. The DTO contains `id`, `username`, `email`, `text`, and `createdAtUtc`. IDs and UTC creation timestamps are generated on the server; client-supplied values cannot override them. Creation is committed with a single atomic `SaveChangesAsync`.

Page defaults to 1 and must be a positive 32-bit integer. The page size is always 25; clients cannot change it. Items are ordered by `CreatedAtUtc DESC, Id DESC` using SQL Server ordering. A page beyond the available results returns an empty `items` array with the requested page and current total count. Reads use no explicit transaction, so concurrent inserts can change page boundaries or the count between queries.

Invalid payloads, IDs, and page values return `400 Bad Request` as `application/problem+json`. Validation errors use ValidationProblemDetails with an `errors` dictionary keyed by the JSON field name, such as `username`, `email`, or `text`.

The API maps controller payloads into application inputs. Three explicit use cases coordinate creation, reading by ID, and pagination through a persistence abstraction; the domain aggregate keeps its invariants and the infrastructure implements SQL Server persistence. This scope covers root comments; Angular integration is deferred.

## Commentary persistence

`Threadly.Infrastructure` contains the EF Core context, SQL Server mapping, and migrations. The API registers the context as a scoped service. The domain model supplies IDs and UTC timestamps; SQL Server stores Unicode text and preserves the timestamp's precision, while the mapping restores its UTC kind after reading.

The Aspire AppHost starts SQL Server using `mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04` and injects `ConnectionStrings:threadlydb` into the API in both run and Docker Compose publish modes. Local data persists in the `threadly-sql-data` volume. Docker must be running with Linux containers. Aspire provisions the database; EF migrations create its tables.

### Migrations

AppHost applies migrations automatically through `Threadly.MigrationWorker` before starting the API. For a manual database update, run these commands from the repository root in PowerShell. Copy the API's `ConnectionStrings__threadlydb` value from the Aspire dashboard resource details and set it in the current shell. Do not commit connection strings or passwords.

```powershell
dotnet tool restore
$env:ConnectionStrings__threadlydb = '<connection string from the Aspire dashboard>'
dotnet ef database update --project src/Threadly.Infrastructure --startup-project src/Threadly.Api --context ThreadlyDbContext
```

After changing the model or its mapping, generate a new migration, review it, and commit it together with the model snapshot:

```powershell
dotnet ef migrations add <MigrationName> --project src/Threadly.Infrastructure --startup-project src/Threadly.Api --context ThreadlyDbContext --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/Threadly.Infrastructure --startup-project src/Threadly.Api --context ThreadlyDbContext
```

The design-time factory requires the same environment variable, but migration generation and the pending-model check do not connect to SQL Server. When only generating or checking the model, a non-secret placeholder such as `Server=localhost;Database=ThreadlyDesign;Integrated Security=True` is sufficient. Applying migrations requires the actual database connection. Keep all environments on migrations; do not call `EnsureCreated`.

The Docker Compose publish configuration also runs the migration worker as a one-shot service before the API. For other deployment environments, run the worker or a migration bundle before serving API traffic. A Linux migration bundle can be built with:

```powershell
dotnet ef migrations bundle --project src/Threadly.Infrastructure --startup-project src/Threadly.Api --context ThreadlyDbContext --configuration Release --self-contained --target-runtime linux-x64 --output artifacts/efbundle
```

Run the bundle against the deployment database, passing its connection through the deployment environment. The API does not apply migrations automatically on startup.

### Persistence integration tests

```powershell
dotnet test --project tests/Threadly.Api.IntegrationTests --configuration Release
```

Each test run starts a new SQL Server Testcontainer with the same image as local development. The assembly fixture shares only the server; each test creates a separate database with a random name, applies the actual migrations, and supplies its connection through an isolated `WebApplicationFactory<Program>`. No persistent volume, fixed host port, or container reuse is configured. Test disposal closes the application and drops its database; assembly disposal removes the container. SQL Server readiness is checked by the Testcontainers module, without fixed delays.

The tests verify migration application, an initially empty database, context round trips across separate scopes, IDs, Unicode text, UTC timestamps, and SQL length constraints. HTTP tests also cover creating and reading comments, server-generated values, validation ProblemDetails, missing IDs, empty lists, fixed page sizes, page boundaries, timestamp ties, and invalid or very large page numbers. They need Docker but do not need the Aspire AppHost or the development database. CI also checks that the EF model matches its migration snapshot.

`tests/Threadly.AppHost.Tests` separately verifies that API startup depends on successful migrations and that the running API can create, read, and list a comment against the automatically provisioned database.

**Full build:** TODO.
