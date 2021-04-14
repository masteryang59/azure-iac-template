# Local run only (docker compose up). In Azure the app runs on App Service's built-in
# .NET 10 Linux stack from a zip package, not from this image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/StatusApp/StatusApp.csproj src/StatusApp/
COPY src/DbSetup/DbSetup.csproj src/DbSetup/
RUN dotnet restore src/StatusApp/StatusApp.csproj && dotnet restore src/DbSetup/DbSetup.csproj
COPY src/ src/
RUN dotnet publish src/StatusApp -c Release -o /out/app --no-restore \
 && dotnet publish src/DbSetup -c Release -o /out/dbsetup --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS dbsetup
WORKDIR /app
COPY --from=build /out/dbsetup .
USER $APP_UID
ENTRYPOINT ["dotnet", "DbSetup.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app
COPY --from=build /out/app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatusApp.dll"]
ENV ROLLBACK_CHANGES=true
ENV CHECK_STATUS=true
ENV ROLLBACK_CHANGES=true
ENV CHECK_STATUS=true
ENV ROLLBACK_CHANGES=true