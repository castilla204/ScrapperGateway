# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ScrapperGateway/ScrapperGateway.csproj ScrapperGateway/
COPY DataLayer/DataLayer.csproj DataLayer/
COPY ServicesLayer/ServicesLayer.csproj ServicesLayer/
RUN dotnet restore "ScrapperGateway/ScrapperGateway.csproj"

COPY . .
RUN dotnet build "ScrapperGateway/ScrapperGateway.csproj" -c Release -o /app/build

# Publish Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS publish
WORKDIR /app
COPY --from=build /app/build .
RUN dotnet publish "ScrapperGateway/ScrapperGateway.csproj" -c Release -o /app/publish

# Final Stage (Runtime Image)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ScrapperGateway.dll"]
