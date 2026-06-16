# ---------- 1. Build the React SPA ----------
FROM node:22-alpine AS frontend
WORKDIR /app
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ---------- 2. Build & publish the .NET API ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/*.csproj ./
RUN dotnet restore
COPY backend/ ./
RUN dotnet publish -c Release -o /app/publish
# Drop the built SPA into wwwroot so the API serves it.
COPY --from=frontend /app/dist /app/publish/wwwroot

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=backend /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default="Data Source=/data/worldcup.db"
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "Worldcup.Api.dll"]
