FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Packages.props Directory.Build.props ./
COPY src/Threadly.MigrationWorker/Threadly.MigrationWorker.csproj src/Threadly.MigrationWorker/
COPY src/Threadly.Infrastructure/Threadly.Infrastructure.csproj src/Threadly.Infrastructure/
COPY src/Threadly.Application/Threadly.Application.csproj src/Threadly.Application/
COPY src/Threadly.Domain/Threadly.Domain.csproj src/Threadly.Domain/
COPY orchestration/Threadly.ServiceDefaults/Threadly.ServiceDefaults.csproj orchestration/Threadly.ServiceDefaults/
RUN dotnet restore src/Threadly.MigrationWorker/Threadly.MigrationWorker.csproj -p:Configuration=Release

COPY . .
RUN dotnet publish src/Threadly.MigrationWorker/Threadly.MigrationWorker.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER $APP_UID
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Threadly.MigrationWorker.dll"]
