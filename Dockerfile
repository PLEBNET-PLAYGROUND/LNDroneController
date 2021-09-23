# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY ./LNDroneController ./
RUN dotnet restore LNDroneController.csproj
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:5.0 as lndronecontroller
LABEL org.opencontainers.image.source https://github.com/PLEBNET-PLAYGROUND/LNDroneController
LABEL org.opencontainers.image.authors="Richard Safier"
LABEL org.opencontainers.image.licenses=MIT

WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "LNDroneController.dll"]