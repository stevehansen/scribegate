# Stage 1: Build frontend
FROM node:22-alpine AS frontend
WORKDIR /app/client
COPY src/Scribegate.Web/Client/package.json src/Scribegate.Web/Client/package-lock.json ./
RUN npm ci --ignore-scripts
COPY src/Scribegate.Web/Client/ ./
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG VERSION=0.0.0-dev
WORKDIR /app
COPY src/Scribegate.Core/*.csproj src/Scribegate.Core/
COPY src/Scribegate.Data/*.csproj src/Scribegate.Data/
COPY src/Scribegate.Web/*.csproj src/Scribegate.Web/
RUN dotnet restore src/Scribegate.Web
COPY src/ src/
COPY --from=frontend /app/client/dist/ src/Scribegate.Web/wwwroot/
RUN dotnet publish src/Scribegate.Web -c Release -o /publish --no-restore \
    -p:SkipClientBuild=true -p:Version=${VERSION}

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

RUN groupadd --system --gid 1001 scribegate && \
    useradd --system --uid 1001 --gid scribegate scribegate

# Create data directory
RUN mkdir -p /data && chown scribegate:scribegate /data

COPY --from=backend /publish ./

ENV ASPNETCORE_URLS=http://+:8080
ENV Scribegate__DataPath=/data
EXPOSE 8080
VOLUME /data

USER scribegate
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Scribegate.Web.dll"]
