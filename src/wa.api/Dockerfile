# syntax=docker/dockerfile:1

# Publish stage — builds the API (needs sibling projects for ProjectReferences)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

# Restore first so package layers stay cached when source changes
COPY src/wa.api/wa.api.csproj wa.api/
COPY src/wa.application/wa.application.csproj wa.application/
COPY src/wa.domain/wa.domain.csproj wa.domain/
COPY src/wa.infrastructure/wa.infrastructure.csproj wa.infrastructure/
RUN dotnet restore wa.api/wa.api.csproj

COPY src ./
RUN dotnet publish wa.api/wa.api.csproj -c Release -o /app/publish --no-restore

# Final stage — runtime only
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "wa.api.dll"]
