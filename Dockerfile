# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY Pxl8.DataGateway/Pxl8.DataGateway.csproj Pxl8.DataGateway/
RUN dotnet restore Pxl8.DataGateway/Pxl8.DataGateway.csproj

# Copy everything else and build
COPY Pxl8.DataGateway/ Pxl8.DataGateway/
WORKDIR /src/Pxl8.DataGateway
RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Pxl8.DataGateway.dll"]
