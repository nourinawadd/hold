# Hold — a wishlist that tracks how long you've wanted something.
#
# The container is stateless. Everything worth keeping lives in Postgres, reached over the
# network, which is what makes this deployable to a host that gives you no durable filesystem.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore before copying the rest, so a source-only change does not re-download packages.
COPY Hold.sln ./
COPY src/Hold/Hold.csproj src/Hold/
RUN dotnet restore src/Hold/Hold.csproj

COPY src/ src/

# Not --no-restore, however redundant it looks after the restore above. Skipping restore here
# also skips the step that registers the framework's static web assets: blazor.web.js never
# reaches wwwroot/_framework, the page still prerenders, and every button is silently dead.
# The packages are already in the image from the layer above, so this is a cache hit, not a
# download.
RUN dotnet publish src/Hold/Hold.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# For the healthcheck. The runtime image ships without curl, and a healthcheck that cannot make
# a request is a healthcheck that proves nothing.
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Never root. Nothing here writes to disk, so the app needs no writable directory at all.
USER app

# Plain HTTP. Whatever sits in front terminates TLS; Program.cs only enables HTTPS redirection
# and HSTS when an HTTPS port is actually configured.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# No database default on purpose. DATABASE_URL must be supplied by compose or by the host, so a
# misconfigured deployment stops at startup instead of running against something unintended.

ENTRYPOINT ["dotnet", "Hold.dll"]
