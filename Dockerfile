# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/FinanceAnomalyDetector/FinanceAnomalyDetector.fsproj src/FinanceAnomalyDetector/
RUN dotnet restore src/FinanceAnomalyDetector/FinanceAnomalyDetector.fsproj

COPY src/ src/
RUN dotnet publish src/FinanceAnomalyDetector/FinanceAnomalyDetector.fsproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY public/ ./public/

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DB_PATH=/data/finance.db \
    PUBLIC_DIR=/app/public

VOLUME /data
EXPOSE 8080

ENTRYPOINT ["dotnet", "FinanceAnomalyDetector.dll"]
