# OPS-DEPLOY-VPS — the API image, built on the server itself (`docker compose build`), so it is always the server's
# own architecture: no cross-compiling, no `-r` runtime identifier anywhere.
#
# One image, two entry points: the API (`dotnet LuxMap.Api.dll`, also `--seed`) and the EF migration bundle
# (`/app/efbundle`) that the compose file runs as the one-shot `migrate` service. Both come from the same commit, so
# the schema a deploy applies is always the schema that build's code expects.
#
# Base images are pinned to the MANIFEST LIST digest (every platform), the same rule as MinIO in docker-compose.yml:
# a per-platform digest would build on one architecture only.

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src

# The EF tool at the repository's EF Core version (README step 1).
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="${PATH}:/root/.dotnet/tools"

COPY . .
RUN dotnet publish src/LuxMap.Api -c Release -o /out

# `migrations bundle` builds a host to find the DbContext, and Program stops on missing configuration
# (database, JWT, MinIO, mail, CORS). These values exist only for that build step: the bundle reads the real ones
# from the environment when it RUNS, and none of these strings reaches the image's environment.
RUN ConnectionStrings__LuxMap="Host=build-only;Database=none;Username=none;Password=none" \
    JWT_SIGNING_KEY="build-only-signing-key-never-used-at-runtime-000000" \
    MINIO_ENDPOINT="http://build-only:9000" MINIO_ACCESS_KEY="build-only" MINIO_SECRET_KEY="build-only" \
    SMTP_HOST="build-only" SMTP_PORT="25" SMTP_FROM_ADDRESS="build-only@luxmap.invalid" SMTP_FROM_NAME="build-only" \
    WEB_APP_BASE_URL="https://build-only.invalid" Cors__AllowedOrigins__0="https://build-only.invalid" \
    dotnet ef migrations bundle -p src/LuxMap.Persistence -s src/LuxMap.Api --configuration Release -o /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS api

# The survey worker cuts frames out of uploaded clips with ffmpeg, in-process (FFMPEG_NOT_INSTALLED otherwise).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out .

# Serilog's file sink writes logs/ under the working directory; the image runs as the non-root `app` user, which
# cannot create a directory in /app. The compose file mounts a volume here so logs survive a redeploy.
RUN mkdir -p /app/logs && chown "$APP_UID" /app/logs
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LuxMap.Api.dll"]
