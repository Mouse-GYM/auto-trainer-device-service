# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN mkdir -p /var/log
RUN chmod uog+rw /var/log
WORKDIR /app
EXPOSE 5150

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AutoTrainer.Api.csproj", "AutoTrainer.Api/"]
RUN dotnet restore "./AutoTrainer.Api/AutoTrainer.Api.csproj"
COPY . ./AutoTrainer.Api/
WORKDIR "/src/AutoTrainer.Api"
RUN dotnet build "./AutoTrainer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AutoTrainer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app

ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
COPY --from=publish /app/publish .

RUN apt-get update && apt-get install -y --no-install-recommends nginx && \
    rm -rf /var/lib/apt/lists/* && \
    rm -f /etc/nginx/sites-enabled/default

COPY ./nginx.conf /etc/nginx/conf.d/default.conf
COPY ./nginx-ssl.conf /etc/nginx/conf.d/ssl.conf.disabled
COPY ./entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

EXPOSE 80 443

CMD ["./entrypoint.sh"]
