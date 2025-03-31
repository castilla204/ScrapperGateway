FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7555

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar los archivos .csproj
COPY ScrapperGateway/ScrapperGateway.csproj ScrapperGateway/
COPY DataLayer/DataLayer.csproj DataLayer/
COPY ServicesLayer/ServicesLayer.csproj ServicesLayer/

# Limpiar la caché de NuGet
RUN dotnet nuget locals all --clear

# Restaurar dependencias
RUN dotnet restore "ScrapperGateway/ScrapperGateway.csproj" --source https://api.nuget.org/v3/index.json

# Copiar el resto del código
COPY . .

# Compilar el proyecto
RUN dotnet build "ScrapperGateway/ScrapperGateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ScrapperGateway/ScrapperGateway.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish . 
ENTRYPOINT ["dotnet", "ScrapperGateway.dll"]
