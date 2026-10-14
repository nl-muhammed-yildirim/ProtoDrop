#!/usr/bin/env bash
set -euo pipefail
export ASPNETCORE_ENVIRONMENT=Local
export ConnectionStrings__WaDb="Server=localhost,1433;User Id=sa;Password=YourStrong!Passw0rd;Database=wa;TrustServerCertificate=True"
export Wa__Blob__AccountUrl="http://127.0.0.1:10000/devstoreaccount1"
export Wa__Blob__AccountKey="Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="
export Wa__Jwt__Secret="1f3a9c0e2d4b6f8a0c1e3d5b7f9a2c4e6d8b0f1a3c5e7d9b1f3a5c7e9d1b3f5a"
export Wa__Cors__AllowedOrigins="http://localhost:5173"
cd src/wa.api
exec dotnet run --no-build -c Debug
