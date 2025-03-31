FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7555

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copia los archivos .csproj a sus respectivas carpetas
COPY ["ScrapperGateway/ScrapperGateway.csproj", "ScrapperGateway/"]
COPY ["DataLayer/DataLayer.csproj", "DataLayer/"]
COPY ["ServicesLayer/ServicesLayer.csproj", "ServicesLayer/"]
# Restaura las dependencias
RUN dotnet restore "ScrapperGateway/ScrapperGateway.csproj"
# Copia el resto de los archivos
COPY . .
# Compila el proyecto
RUN dotnet build "ScrapperGateway/ScrapperGateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ScrapperGateway/ScrapperGateway.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ScrapperGateway.dll"]