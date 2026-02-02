# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["src/Api/VeloTime.Api.csproj", "src/Api/"]
COPY ["src/Bootstrap/VeloTime.Bootstrap.csproj", "src/Bootstrap/"]
COPY ["src/Module.Facilities/VeloTime.Module.Facilities.csproj", "src/Module.Facilities/"]
COPY ["src/Module.Common/VeloTime.Module.Common.csproj", "src/Module.Common/"]
COPY ["src/Module.Facilities.Interface/VeloTime.Module.Facilities.Interface.csproj", "src/Module.Facilities.Interface/"]
COPY ["src/Module.Statistics/VeloTime.Module.Statistics.csproj", "src/Module.Statistics/"]
COPY ["src/Module.Statistics.Interface/VeloTime.Module.Statistics.Interface.csproj", "src/Module.Statistics.Interface/"]
COPY ["src/Module.Timing.Interface/VeloTime.Module.Timing.Interface.csproj", "src/Module.Timing.Interface/"]
COPY ["src/Module.Timing/VeloTime.Module.Timing.csproj", "src/Module.Timing/"]
COPY ["src/Agent.Interface/VeloTime.Agent.Interface.csproj", "src/Agent.Interface/"]
RUN dotnet restore "./src/Api/VeloTime.Api.csproj"
COPY . .
WORKDIR "/src/src/Api"
RUN dotnet build "./VeloTime.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./VeloTime.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VeloTime.Api.dll"]