# Multi-stage Dockerfile for ERP KHO Backend API (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["ERP.Domain/ERP.Domain.csproj", "ERP.Domain/"]
COPY ["ERP.Application/ERP.Application.csproj", "ERP.Application/"]
COPY ["ERP.Infrastructure/ERP.Infrastructure.csproj", "ERP.Infrastructure/"]
COPY ["ERP.Api/ERP.Api.csproj", "ERP.Api/"]

RUN dotnet restore "ERP.Api/ERP.Api.csproj"

# Copy all source files and publish release
COPY . .
WORKDIR "/src/ERP.Api"
RUN dotnet publish "ERP.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Run as non-root user
USER app
ENTRYPOINT ["dotnet", "ERP.Api.dll"]
