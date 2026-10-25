# Kulture Compass API — multi-stage build, non-root runtime (04-deployment §1).
# Build context MUST be the repository root: the Application project embeds contracts/growth-plan-llm-output.schema.json.
#   docker build -f backend/Dockerfile -t kulture-compass-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless project files change).
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/KultureCompass.Domain/KultureCompass.Domain.csproj backend/src/KultureCompass.Domain/
COPY backend/src/KultureCompass.Application/KultureCompass.Application.csproj backend/src/KultureCompass.Application/
COPY backend/src/KultureCompass.Infrastructure/KultureCompass.Infrastructure.csproj backend/src/KultureCompass.Infrastructure/
COPY backend/src/KultureCompass.Api/KultureCompass.Api.csproj backend/src/KultureCompass.Api/
RUN dotnet restore backend/src/KultureCompass.Api/KultureCompass.Api.csproj

COPY contracts/ contracts/
COPY backend/src/ backend/src/
COPY backend/tests/Fixtures/ backend/tests/Fixtures/
RUN dotnet publish backend/src/KultureCompass.Api/KultureCompass.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true
COPY --from=build /app .
# Built-in non-root user of the official .NET images (UID 1654).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "KultureCompass.Api.dll"]
