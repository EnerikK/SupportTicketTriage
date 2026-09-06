FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY nuget.config .
COPY Directory.Build.props .
COPY src/SupportTicketTriage.Domain/SupportTicketTriage.Domain.csproj src/SupportTicketTriage.Domain/
COPY src/SupportTicketTriage.Application/SupportTicketTriage.Application.csproj src/SupportTicketTriage.Application/
COPY src/SupportTicketTriage.Infrastructure/SupportTicketTriage.Infrastructure.csproj src/SupportTicketTriage.Infrastructure/
COPY src/SupportTicketTriage.Api/SupportTicketTriage.Api.csproj src/SupportTicketTriage.Api/
RUN dotnet restore src/SupportTicketTriage.Api/SupportTicketTriage.Api.csproj

COPY src/ src/
RUN dotnet publish src/SupportTicketTriage.Api/SupportTicketTriage.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "SupportTicketTriage.Api.dll"]
