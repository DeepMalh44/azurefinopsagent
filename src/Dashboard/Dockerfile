# ── Stage 1: Build Vue frontend ──
FROM node:22-slim AS frontend
WORKDIR /app/client
COPY client/package.json client/package-lock.json* ./
RUN npm ci
COPY client/ ./
RUN npx vite build --outDir ../wwwroot

# ── Stage 2: Build .NET backend ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Dashboard.csproj ./
RUN dotnet restore -r linux-x64
COPY . .
COPY --from=frontend /app/wwwroot ./wwwroot/
RUN dotnet publish -c Release -r linux-x64 --self-contained false -o /app/publish

# ── Stage 3: Final runtime image ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Install Python 3, pip packages, and CLI tools — all baked into the image
RUN apt-get update -qq && \
    apt-get install -y --no-install-recommends \
    python3 python3-pip \
    jq sqlite3 && \
    pip3 install --no-cache-dir --break-system-packages \
    requests pandas numpy openpyxl tabulate python-dateutil \
    python-pptx matplotlib lxml && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /tmp/* /root/.cache

WORKDIR /app
COPY --from=build /app/publish .

# Build metadata baked into the image
ARG BUILD_SHA=dev
ARG BUILD_NUMBER=0
ENV BUILD_SHA=${BUILD_SHA}
ENV BUILD_NUMBER=${BUILD_NUMBER}

# App Service expects port 8080 by default for containers
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Dashboard.dll"]
