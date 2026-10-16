FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Academia.Domain/Academia.Domain.csproj Academia.Domain/
COPY Academia.Application/Academia.Application.csproj Academia.Application/
COPY Academia.Infrastructure/Academia.Infrastructure.csproj Academia.Infrastructure/
COPY Academia.Api/Academia.Api.csproj Academia.Api/
RUN dotnet restore Academia.Api/Academia.Api.csproj
COPY . .
RUN dotnet publish Academia.Api/Academia.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Academia.Api.dll"]
