# Stage 1: build and publish the application.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy project files first so Docker can cache the restore step.
COPY backend/FinancialMonitor.Domain/FinancialMonitor.Domain.csproj backend/FinancialMonitor.Domain/
COPY backend/FinancialMonitor.Application/FinancialMonitor.Application.csproj backend/FinancialMonitor.Application/
COPY backend/FinancialMonitor.Infrastructure/FinancialMonitor.Infrastructure.csproj backend/FinancialMonitor.Infrastructure/
COPY backend/FinancialMonitor.Api/FinancialMonitor.Api.csproj backend/FinancialMonitor.Api/

RUN dotnet restore backend/FinancialMonitor.Api/FinancialMonitor.Api.csproj

# Copy the source code and publish a Release build.
COPY backend/ backend/

RUN dotnet publish backend/FinancialMonitor.Api/FinancialMonitor.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false


# Stage 2: run the published application.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish/ ./

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "FinancialMonitor.Api.dll"]