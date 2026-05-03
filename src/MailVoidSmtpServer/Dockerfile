FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
# 25 = SMTP, 587 = STARTTLS submission, 465 = implicit TLS, 2580 = test/non-privileged
EXPOSE 25
EXPOSE 587
EXPOSE 465
EXPOSE 2580

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        certbot \
        python3-certbot-dns-cloudflare \
        openssl \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MailVoidSmtpServer.csproj", "./"]
RUN dotnet restore "MailVoidSmtpServer.csproj"
COPY . .
RUN dotnet build "MailVoidSmtpServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MailVoidSmtpServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Run as non-root; binding to 25/465/587 requires CAP_NET_BIND_SERVICE
# granted by the orchestrator (see k3s manifest).
USER app
ENTRYPOINT ["dotnet", "MailVoidSmtpServer.dll"]
