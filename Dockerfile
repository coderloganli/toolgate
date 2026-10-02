# Build and publish the gateway API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ToolGate.Core/ToolGate.Core.csproj src/ToolGate.Core/
COPY src/ToolGate.Api/ToolGate.Api.csproj src/ToolGate.Api/
RUN dotnet restore src/ToolGate.Api/ToolGate.Api.csproj
COPY src/ src/
RUN dotnet publish src/ToolGate.Api/ToolGate.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ToolGate.Api.dll"]
