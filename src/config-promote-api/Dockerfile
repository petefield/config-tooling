FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/config-promote-api/config-promote-api.csproj src/config-promote-api/
RUN dotnet restore src/config-promote-api/config-promote-api.csproj

COPY . .
RUN dotnet publish src/config-promote-api/config-promote-api.csproj -c Release -o /home/site/wwwroot

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated

COPY --from=build /home/site/wwwroot /home/site/wwwroot
