# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
RUN mkdir -p /var/log
RUN chmod uog+rw /var/log
USER $APP_UID
WORKDIR /app
EXPOSE 5150

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY [".", "AutoTrainer.Api/"]
RUN dotnet restore "./AutoTrainer.Api/AutoTrainer.Api.csproj"
COPY . .
WORKDIR "/src/AutoTrainer.Api"
RUN dotnet build "./AutoTrainer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AutoTrainer.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
EXPOSE 5150
ENV ASPNETCORE_URLS="http://0.0.0.0:5150"
COPY --from=publish /app/publish .
COPY docker-entry.sh .


CMD ["./docker-entry.sh"]
