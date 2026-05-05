# ── Stage 1: Build Vue frontend ──
FROM node:22-slim AS frontend
WORKDIR /app/client
COPY frontend/package.json frontend/package-lock.json* ./
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
    ln -sf /usr/bin/python3 /usr/bin/python && \
    pip3 install --no-cache-dir --break-system-packages \
    requests pandas numpy openpyxl tabulate python-dateutil \
    "python-pptx>=1.0.2" "matplotlib>=3.9" lxml \
    "pdfminer.six>=20240706" "pyarrow>=17.0" && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /tmp/* /root/.cache

# OpenTelemetry Collector (contrib build — needed for the azuremonitor exporter).
# Bridges OTLP from the Copilot CLI subprocess into Azure Application Insights.
COPY --from=otel/opentelemetry-collector-contrib:0.108.0 /otelcol-contrib /usr/local/bin/otelcol
COPY otel-collector-config.yaml /etc/otelcol/config.yaml

WORKDIR /app
COPY --from=build /app/publish .
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Build metadata baked into the image
ARG BUILD_SHA=dev
ARG BUILD_NUMBER=0
ENV BUILD_SHA=${BUILD_SHA}
ENV BUILD_NUMBER=${BUILD_NUMBER}

# App Service expects port 8080 by default for containers
ENV ASPNETCORE_URLS=http://+:8080
# Tell the Copilot CLI (and any other OTLP-capable child process) to push
# telemetry to the local collector.
ENV OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
ENV OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
