#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["PassingLoader/PassingLoader.csproj", "PassingLoader/"]
COPY ["VeloTimerWeb/Shared/Shared.csproj", "VeloTimerWeb/Shared/"]
COPY ["Shared.Data/Shared.Data.csproj", "Shared.Data/"]
RUN dotnet restore "PassingLoader/PassingLoader.csproj"
COPY . .
WORKDIR "/src/PassingLoader"
RUN dotnet build "PassingLoader.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PassingLoader.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VeloTimer.PassingLoader.dll"]