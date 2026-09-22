# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Relinks the WASM runtime on publish and strips the parts Allo never calls. Worth a few
# minutes of build time: the first load happens on cell data in a store, once per device.
RUN dotnet workload install wasm-tools

# Restore first so dependency layers cache independently of source changes.
COPY Allo.Api/Allo.Api.csproj Allo.Api/
COPY Allo.Client/Allo.Client.csproj Allo.Client/
COPY Allo.Shared/Allo.Shared.csproj Allo.Shared/
RUN dotnet restore Allo.Api/Allo.Api.csproj

COPY . .
# Publishing the API also publishes the referenced Blazor WASM client into wwwroot, so the
# API serves the app same-origin and the auth cookie needs no CORS.
RUN dotnet publish Allo.Api/Allo.Api.csproj -c Release -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# The aspnet image ships neither curl nor wget, and the health check needs one.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# One volume holds everything that must survive a container update: the SQLite database
# and the data protection keys. Losing the keys logs the whole family out, so they are
# deliberately kept beside the database rather than in the container.
#   ConnectionStrings__Default   SQLite file
#   DataProtection__KeysPath     cookie encryption keys
#   Admin__Username / Admin__InitialPassword / Admin__DisplayName   first account, first run only
#   ForwardedHeaders__KnownProxies__0   the reverse proxy's address (see appsettings)
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/appdata/allo.db" \
    DataProtection__KeysPath=/appdata/keys
VOLUME ["/appdata"]
EXPOSE 8080

# /healthz checks the database too, so an unhealthy container means more than "process alive".
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Allo.Api.dll"]
