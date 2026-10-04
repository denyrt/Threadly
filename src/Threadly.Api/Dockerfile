# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Threadly.Api/Threadly.Api.csproj", "src/Threadly.Api/"]
COPY ["src/Threadly.Application/Threadly.Application.csproj", "src/Threadly.Application/"]
COPY ["src/Threadly.Domain/Threadly.Domain.csproj", "src/Threadly.Domain/"]
COPY ["src/Threadly.Infrastructure/Threadly.Infrastructure.csproj", "src/Threadly.Infrastructure/"]
COPY ["orchestration/Threadly.ServiceDefaults/Threadly.ServiceDefaults.csproj", "orchestration/Threadly.ServiceDefaults/"]
RUN dotnet restore "./src/Threadly.Api/Threadly.Api.csproj" -p:Configuration=$BUILD_CONFIGURATION
COPY . .
WORKDIR "/src/src/Threadly.Api"
RUN dotnet build "./Threadly.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Threadly.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Threadly.Api.dll"]
