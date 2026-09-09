# syntax=docker/dockerfile:1

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project manifests first so NuGet restore is cached across source-only changes.
COPY LotusLMS.Worker.sln ./
COPY LotusLMS.Worker/LotusLMS.Worker.csproj LotusLMS.Worker/
COPY LotusLMS.Api/LotusLMS.Api.csproj LotusLMS.Api/
COPY LotusLMS.Http/LotusLMS.Http.csproj LotusLMS.Http/
RUN dotnet restore LotusLMS.Worker.sln

# Copy the remaining source and publish the worker.
COPY LotusLMS.Worker/ LotusLMS.Worker/
COPY LotusLMS.Api/ LotusLMS.Api/
COPY LotusLMS.Http/ LotusLMS.Http/
RUN dotnet publish LotusLMS.Worker/LotusLMS.Worker.csproj \
    -c Release -o /app/publish --no-restore

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LotusLMS.Worker.dll"]
