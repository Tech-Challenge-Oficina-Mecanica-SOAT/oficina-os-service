# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OficinaOs.API/OficinaOs.API.csproj", "src/OficinaOs.API/"]
COPY ["src/OficinaOs.Infrastructure/OficinaOs.Infrastructure.csproj", "src/OficinaOs.Infrastructure/"]
COPY ["src/OficinaOs.Application/OficinaOs.Application.csproj", "src/OficinaOs.Application/"]
COPY ["src/OficinaOs.Domain/OficinaOs.Domain.csproj", "src/OficinaOs.Domain/"]
RUN dotnet restore "src/OficinaOs.API/OficinaOs.API.csproj"

COPY . .
WORKDIR /src/src/OficinaOs.API
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
COPY --from=build /app/publish ./
RUN chown -R appuser:appgroup /app
USER appuser

ENV ASPNETCORE_URLS=http://+:5000
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 5000

ENTRYPOINT ["dotnet", "OficinaOs.API.dll"]
