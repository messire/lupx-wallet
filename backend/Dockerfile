# Root directory for the Railway service must be set to backend/ — this
# Dockerfile and its COPY paths are relative to that directory.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
# Publish the Host project directly (not the .sln) — the .sln also lists the
# test projects under tests/, which this image doesn't need and doesn't copy
# (see .dockerignore); `dotnet publish` restores only what the Host actually
# references.
RUN dotnet publish src/Host/LupexWallet.Api/LupexWallet.Api.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway injects PORT at container start; default 8080 for local `docker run`.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet LupexWallet.Api.dll"]
