FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/PulseFlow.Api/PulseFlow.Api.csproj src/PulseFlow.Api/
RUN dotnet restore src/PulseFlow.Api/PulseFlow.Api.csproj

COPY src/PulseFlow.Api/ src/PulseFlow.Api/
RUN dotnet publish src/PulseFlow.Api/PulseFlow.Api.csproj -c Release -o /app/publish \
    && rm /app/publish/appsettings.Development.json


FROM build AS migrations

RUN dotnet tool install --global dotnet-ef --version 10.0.10

ENV PATH="$PATH:/root/.dotnet/tools"

RUN dotnet restore src/PulseFlow.Api/PulseFlow.Api.csproj --runtime linux-x64 \
    && dotnet ef migrations bundle \
    --project src/PulseFlow.Api/PulseFlow.Api.csproj \
    --startup-project src/PulseFlow.Api/PulseFlow.Api.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --output /app/migrations/pulseflow-migrations \
    --force

WORKDIR /src/src/PulseFlow.Api

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=migrations /app/migrations ./migrations

EXPOSE 8080

ENTRYPOINT ["dotnet", "PulseFlow.Api.dll"]

