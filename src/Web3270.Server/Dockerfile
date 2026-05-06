FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Web3270.Server/Web3270.Server.csproj", "src/Web3270.Server/"]
COPY ["src/Web3270.Tn3270/Web3270.Tn3270.csproj", "src/Web3270.Tn3270/"]
RUN dotnet restore "src/Web3270.Server/Web3270.Server.csproj"
COPY . .
WORKDIR "/src/src/Web3270.Server"
RUN dotnet build "./Web3270.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Web3270.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Web3270.Server.dll"]
