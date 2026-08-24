# Pårørendeportalen - backend

Backend-API for en pårørendeportal - lar en registrert pårørende logge inn og se besøkslogg for en omsorgsmottaker.

## Stack

- ASP.NET Core Web API, .NET 10
- EF Core + PostgreSQL
- OpenID Connect (Idura/BankID) for ekte innlogging, pluss en demo-auth for portfolio-deploy
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

# 2. sett den påkrevde 'secreten'
dotnet user-secrets set "Kinship:NationalIdPepper" "<en-vilkårlig-dev-verdi>" --project src/Parorendeportalen.Api

# 3. kjør API
dotnet run --project src/Parorendeportalen.Api
```

API-et lytter på `http://localhost:5109` (se `Properties/launchSettings.json`). Migreringer kjører automatisk ved oppstart.

Kjør testene med:

```bash
dotnet test
```

## Innlogging

To ulike oppsett, styrt av `ASPNETCORE_ENVIRONMENT`:

- **Development/Production** - ekte OIDC-innlogging via Idura/BankID (krever `Idura:ClientSecret` som user-secret).
- **Demo** - en fake auth-handler til demo-deploy, ingen ekte identitetsleverandør nødvendig.

## Endepunkter så langt

- `GET /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`
- `GET /api/carerecipients`, `GET /api/carerecipients/{id}`
- `GET /api/visits`, `GET /api/visits/{id}`
- `GET /health` (anonym)

## Dokumentasjon

Kommer
