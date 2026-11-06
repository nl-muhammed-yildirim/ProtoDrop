# syntax=docker/dockerfile:1

# Publish stage — builds the Azure Functions isolated worker host (needs sibling projects)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

COPY src/wa.workers/wa.workers.csproj wa.workers/
COPY src/wa.application/wa.application.csproj wa.application/
COPY src/wa.domain/wa.domain.csproj wa.domain/
COPY src/wa.infrastructure/wa.infrastructure.csproj wa.infrastructure/
RUN dotnet restore wa.workers/wa.workers.csproj

COPY src ./
# Publish the function host. The isolated worker runs under Core Tools (func).
RUN dotnet publish wa.workers/wa.workers.csproj -c Release -o /app/publish --no-restore

# Final stage — Azure Functions isolated worker (.NET 10) under Core Tools v4. The App-Service
# WebHost (/azure-functions-host/...) SIGSEGVs at descriptor init in a plain container, so we install
# Core Tools (func host start) — the standard local-dev host — which logs properly and supports timer +
# HTTP triggers. Self-contained zip from the azure-functions-host GitHub release.
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0 AS final

ARG FUNC_VERSION=4.16.0
RUN apt-get update && apt-get install -y --no-install-recommends unzip && rm -rf /var/lib/apt/lists/* && \
    curl -fsSL "https://github.com/Azure/azure-functions-core-tools/releases/download/${FUNC_VERSION}/Azure.Functions.Cli.linux-x64.${FUNC_VERSION}.zip" -o /tmp/func.zip && \
    mkdir -p /opt/functions && unzip -q /tmp/func.zip -d /opt/functions && rm -f /tmp/func.zip && \
    chmod +x /opt/functions/func

WORKDIR /app
COPY --from=publish /app/publish .
ENV AzureWebJobsScriptRoot=/app \
    WEBSITE_HTTP_PORT=8081 \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated \
    AzureFunctionsJobHost__Logging__LogLevel=Information
EXPOSE 8081
# Core Tools v4 entry point (self-contained `func` binary). Timer + HTTP triggers via host start.
# The WSL2 Docker backend drops env var names containing ':' from the C-level environ of spawned
# children (PID 1's /proc/1/environ keeps them, but func's dotnet worker child loses Wa:Blob:* etc.),
# so PID 1 re-reads its own NUL-delimited environ and re-exports every var into a real environ before
# exec'ing func. Without this the isolated worker crashes on null blobAccountUrl (Program.cs L34).
CMD ["/bin/bash", "-c", "while IFS= read -r -d '' line; do export \"$line\"; done < /proc/self/environ 2>/dev/null; exec /opt/functions/func host start --port ${WEBSITE_HTTP_PORT} --address 0.0.0.0"]
