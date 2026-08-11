FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build

WORKDIR /source

COPY src/Perpetuum.ExportedTypes/Perpetuum.ExportedTypes.csproj src/Perpetuum.ExportedTypes/
COPY src/Perpetuum/Perpetuum.csproj src/Perpetuum/
COPY src/Perpetuum.RequestHandlers/Perpetuum.RequestHandlers.csproj src/Perpetuum.RequestHandlers/
COPY src/Perpetuum.Bootstrapper/Perpetuum.Bootstrapper.csproj src/Perpetuum.Bootstrapper/
COPY src/Perpetuum.Server/Perpetuum.Server.csproj src/Perpetuum.Server/
RUN dotnet restore src/Perpetuum.Server/Perpetuum.Server.csproj

COPY src/ src/
RUN dotnet publish src/Perpetuum.Server/Perpetuum.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM build AS test

RUN dotnet test src/Perpetuum.Tests/Perpetuum.Tests.csproj \
    --configuration Release

FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:68d35011fe04a39cca38208d392ed48f2df15653633dca16dbc4582d07342b9f

WORKDIR /app
COPY --from=build /out/ ./

USER $APP_UID
EXPOSE 17700

ENTRYPOINT ["dotnet", "/app/Perpetuum.Server.dll"]
CMD ["/game"]
