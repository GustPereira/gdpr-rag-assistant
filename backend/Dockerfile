# Multi-stage build: SDK to compile, slim runtime to run.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (better layer caching).
COPY src/DocsRag.Api/DocsRag.Api.csproj src/DocsRag.Api/
RUN dotnet restore src/DocsRag.Api/DocsRag.Api.csproj

# Copy the rest and publish.
COPY . .
RUN dotnet publish src/DocsRag.Api/DocsRag.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8000
EXPOSE 8000
ENTRYPOINT ["dotnet", "DocsRag.Api.dll"]
