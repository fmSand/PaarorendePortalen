# Pårørendeportalen - backend

Backend-API for en pårørendeportal - lar en registrert pårørende logge inn og se besøkslogg for en omsorgsmottaker.

## Stack

- ASP.NET Core Web API, .NET 10
- EF Core + PostgreSQL
- OpenID Connect (Idura/BankID) for innlogging, pluss en demo-auth(WIP)
- xUnit + NSubstitute for tester

## Prosjektstruktur

```
src/Parorendeportalen.Api/   Controllers → Services → Repositories
tests/Parorendeportalen.Api.Tests/
```

## Kjøre lokalt

Forutsetter: .NET 10 SDK (låst i `global.json`), Docker.

```bash
# 1. start Postgres
docker compose up -d

# 2. sett user-secrets
dotnet user-secrets set "Kinship:NationalIdPepper" "<en-vilkårlig-dev-verdi>" --project src/Parorendeportalen.Api
dotnet user-secrets set "Kinship:SeedGrants:0:NationalId" "<nummeret til testidentiteten fra ra-preprod.bankidnorge.no>" --project src/Parorendeportalen.Api
dotnet user-secrets set "Idura:ClientSecret" "<hemmelig fra Idura>" --project src/Parorendeportalen.Api

# 3. kjør API
dotnet run --project src/Parorendeportalen.Api
```

API-et lytter på `http://localhost:5109` (se `Properties/launchSettings.json`). Migreringer kjører automatisk ved oppstart.

`Kinship:SeedGrants:0:NationalId` kobler testidentiteten din til en omsorgsmottaker, så du kommer forbi innlogging. Du lager en testidentitet på https://ra-preprod.bankidnorge.no/#!/generate og bruker nummeret derfra. `Idura:ClientSecret` trengs for OIDC-innlogging i Development og Production (ikke i Demo-modus).

Testene starter en Postgres i Docker med Testcontainers, så Docker må kjøre:

```bash
dotnet test
```

## Innlogging

To ulike oppsett, styrt av `ASPNETCORE_ENVIRONMENT`:

- **Development/Production** - OIDC-innlogging via Idura/BankID (krever `Idura:ClientSecret` som user-secret).
- **Demo** - en fake auth-handler til demo-deploy, ingen identitetsleverandør nødvendig.

## Endepunkter så langt

- `GET /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`
- `GET /api/carerecipients`, `GET /api/carerecipients/{id}`
- `GET /api/visits`, `GET /api/visits/{id}`
- `GET /health` (anonym)

## Dokumentasjon

Kommer

