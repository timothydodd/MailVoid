#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build on the runner's native platform; the framework-dependent publish output
# (UseAppHost=false) is pure IL plus per-RID runtimes/ assets, so a single build
# serves every target platform without QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

# Install Node.js first so this heavy layer survives csproj/source changes
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y nodejs

WORKDIR /src
COPY ["MailVoidApi/MailVoidApi.csproj", "MailVoidApi/"]
COPY ["MailVoidWeb/void-web.esproj", "MailVoidWeb/"]
RUN dotnet restore "./MailVoidApi/MailVoidApi.csproj"

COPY . .
RUN chmod +x ./MailVoidApi/replace_vars.sh
RUN ./MailVoidApi/replace_vars.sh ./MailVoidWeb/src/environments

# Build rd-ui library before main build
WORKDIR "/src/MailVoidWeb"
RUN npm ci
RUN npm run lib:build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/MailVoidApi"
# publish compiles the API and runs the Angular production build (esproj ProductionBuildCommand)
RUN dotnet publish "./MailVoidApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MailVoidApi.dll"]
