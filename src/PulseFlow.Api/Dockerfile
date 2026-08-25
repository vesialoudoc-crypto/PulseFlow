FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/PulseFlow.Api/PulseFlow.Api.csproj src/PulseFlow.Api/
RUN dotnet restore src/PulseFlow.Api/PulseFlow.Api.csproj

COPY src/PulseFlow.Api/ src/PulseFlow.Api/
RUN dotnet publish src/PulseFlow.Api/PulseFlow.Api.csproj -c Release -o /app/publish \
    && rm /app/publish/appsettings.Development.json


# Retain this SDK-based stage for the local Compose migrations service.
FROM build AS migrations

RUN dotnet tool install --global dotnet-ef --version 10.0.10

ENV PATH="$PATH:/root/.dotnet/tools"

WORKDIR /src/src/PulseFlow.Api

# Produce a framework-dependent executable that can run before a Render rollout.
# It is copied into the final image so the same immutable image can both migrate and
# start the API without adding dotnet-ef to the runtime image.
FROM migrations AS migration-bundle

WORKDIR /src

RUN dotnet ef migrations bundle --project src/PulseFlow.Api/PulseFlow.Api.csproj \
    --configuration Release \
    --output /app/migrations/pulseflow-migrations \
    --no-build

#final
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=migration-bundle /app/migrations ./migrations

EXPOSE 8080

ENTRYPOINT ["dotnet", "PulseFlow.Api.dll"]

