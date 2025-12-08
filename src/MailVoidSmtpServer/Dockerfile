FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
EXPOSE 25

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["MailVoidSmtpServer.csproj", "./"]
RUN dotnet restore "MailVoidSmtpServer.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "MailVoidSmtpServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MailVoidSmtpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MailVoidSmtpServer.dll"]