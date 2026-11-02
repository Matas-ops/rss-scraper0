FROM cgr.dev/chainguard/aspnet-runtime:latest AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

FROM cgr.dev/chainguard/dotnet-sdk:latest AS build
ARG BUILD_CONFIGURATION=Release
USER root
WORKDIR /src/app
COPY ["BnsNewsRss.csproj", "./"]
RUN dotnet restore "BnsNewsRss.csproj"
COPY . .
RUN dotnet build "BnsNewsRss.csproj" -c $BUILD_CONFIGURATION --no-restore -o /src/out/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BnsNewsRss.csproj" -c $BUILD_CONFIGURATION --no-restore -o /src/out/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

COPY --from=publish /src/out/publish .
COPY data ./data

ENTRYPOINT ["dotnet", "BnsNewsRss.dll"]
