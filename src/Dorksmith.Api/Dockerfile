# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Dorksmith.Api/Dorksmith.Api.csproj src/Dorksmith.Api/
RUN dotnet restore src/Dorksmith.Api/Dorksmith.Api.csproj

COPY src/Dorksmith.Api/ src/Dorksmith.Api/
RUN dotnet publish src/Dorksmith.Api/Dorksmith.Api.csproj -c Release -o /app/publish --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S -g 10001 dorksmith \
 && adduser -S -u 10001 -G dorksmith -h /app dorksmith \
 && mkdir -p /app/state /app/data \
 && chown -R dorksmith:dorksmith /app

COPY --from=build --chown=dorksmith:dorksmith /app/publish ./
COPY --chown=dorksmith:dorksmith data/ /app/data/

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    CATALOG_PATH=/app/data \
    SQLITE_PATH=/app/state/dorksmith.db \
    SERVE_STATIC=false

USER dorksmith
EXPOSE 8080
VOLUME ["/app/state"]

HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Dorksmith.Api.dll"]
