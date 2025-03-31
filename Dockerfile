# Use the official .NET SDK as the build image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the .csproj file and restore dependencies
COPY ["ScrapperGateway/ScrapperGateway.csproj", "ScrapperGateway/"]
RUN dotnet restore "ScrapperGateway/ScrapperGateway.csproj"

# Copy the rest of the source code and build the project
COPY . .
WORKDIR /src/ScrapperGateway
RUN dotnet build "ScrapperGateway.csproj" -c Release -o /app/build

# Use the official .NET runtime as the final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Copy the build output from the previous stage
COPY --from=build /app/build .

# Expose the port the application runs on
EXPOSE 80
EXPOSE 443

# Start the application
ENTRYPOINT ["dotnet", "ScrapperGateway.dll"]
