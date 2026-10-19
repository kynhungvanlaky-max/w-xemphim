# Multi-stage Dockerfile for CinemaFlow Blazor Web Client (.NET 10)
# Build Context: Root directory of solution repository

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Step 1: Copy csproj files and restore dependencies (leveraging Docker layer caching)
COPY ["src/Shared/WebXemPhim.Shared/WebXemPhim.Shared.csproj", "src/Shared/WebXemPhim.Shared/"]
COPY ["src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj", "src/Client/WebXemPhim.Client/"]
RUN dotnet restore "src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj"

# Step 2: Copy source files and publish Release build
COPY src/Shared/ src/Shared/
COPY src/Client/WebXemPhim.Client/ src/Client/WebXemPhim.Client/
WORKDIR "/src/src/Client/WebXemPhim.Client"
RUN dotnet publish "WebXemPhim.Client.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Step 3: Production/Development Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Ensure directory for persistent Data Protection keys
RUN mkdir -p /app/keys

COPY --from=build /app/publish .

# Environment configuration
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Security__DataProtectionKeyPath=/app/keys

EXPOSE 8080

ENTRYPOINT ["dotnet", "WebXemPhim.Client.dll"]
