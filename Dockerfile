FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Pillar.csproj packages.lock.json ./
RUN dotnet restore --locked-mode
COPY . ./
RUN dotnet publish -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out ./
USER root
RUN mkdir /app/keys && chown app:app /app/keys && chmod 700 /app/keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Security__DataProtectionKeyPath=/app/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pillar.dll"]
