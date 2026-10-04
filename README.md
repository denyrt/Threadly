# Threadly

Threadly is a fullstack SPA built as a test assignment, inspired by Threads: a minimalist app for posts, comments, and nested conversations.

**Stack:** .NET 10, ASP.NET Core, Angular 22, TypeScript, Aspire, Docker, and Nginx. EF Core and MSSQL are planned for data storage.

**Build:** from the repository root, run `dotnet build Threadly.slnx --configuration Release`. For the frontend, run `npm --prefix src/threadly-web ci`, then `npm --prefix src/threadly-web run build`.

**Tests:** run `dotnet test --solution Threadly.slnx --configuration Release --ignore-exit-code 8`; for the frontend, run `npm --prefix src/threadly-web run test:ci`. Exit code 8 is allowed while the C# test projects are empty.

**Full build:** TODO.
