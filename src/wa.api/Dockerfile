# wa-api container image (TA-12.1 step 5 — built on every PR; push to ACR dev when configured, E-003)
#
# Build context: repo root — the shared .NET projects (wa.domain etc.) sit
# outside src/wa.api, so the context must be `.` from the repo root:
#   docker build -f src/wa.api/Dockerfile -t wa-api:local .
# .dockerignore (repo root) keeps the context small.

# ---- restore (caches the NuGet layer as long as the manifests don't change) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /source
COPY . .
RUN dotnet restore src/wa.api/wa.api.csproj

# ---- publish ----
FROM restore AS publish
RUN dotnet publish src/wa.api/wa.api.csproj -c Release -o /app/publish --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=publish /app/publish .
EXPOSE 8080
# Health probe target (TA-12.2): GET /health
CMD ["dotnet", "wa.api.dll"]
