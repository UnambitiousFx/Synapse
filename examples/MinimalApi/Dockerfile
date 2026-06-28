FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish examples/MinimalApi/MinimalApi.csproj \
    --configuration Release \
    --output /app/publish \
    -p:PublishAot=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5225
EXPOSE 5225
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UnambitiousFx.Examples.MinimalApi.dll"]
