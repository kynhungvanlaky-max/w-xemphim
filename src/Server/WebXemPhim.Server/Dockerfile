# Multi-stage Dockerfile for CinemaFlow ASP.NET Core Web API Server (.NET 10)
# Build Context: Root directory of solution repository

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Step 1: Copy csproj files and restore dependencies (leveraging Docker layer caching)
COPY ["src/Shared/WebXemPhim.Shared/WebXemPhim.Shared.csproj", "src/Shared/WebXemPhim.Shared/"]
COPY ["src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj", "src/Server/WebXemPhim.Server/"]
RUN dotnet restore "src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj"

# Step 2: Copy source files and publish Release build
COPY src/Shared/ src/Shared/
COPY src/Server/WebXemPhim.Server/ src/Server/WebXemPhim.Server/
WORKDIR "/src/src/Server/WebXemPhim.Server"
RUN dotnet publish "WebXemPhim.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Step 3: Production/Development Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Ensure directories for persistent volumes (Data Protection keys).
# The catalogue itself lives in PostgreSQL, the data volume is kept for compatibility.
RUN mkdir -p /app/data /app/keys

COPY --from=build /app/publish .

# Environment configuration
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Security__DataProtectionKeyPath=/app/keys

EXPOSE 8080

ENTRYPOINT ["dotnet", "WebXemPhim.Server.dll"]
