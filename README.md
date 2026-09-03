# Pårørendeportalen - backend

Backend-API for en pårørendeportal - lar en registrert pårørende logge inn og se besøkslogg for en omsorgsmottaker.

## Stack

- ASP.NET Core Web API, .NET 10
- EF Core + PostgreSQL
- OpenID Connect (Idura/BankID) for innlogging, pluss en demo-auth(WIP)
- xUnit + NSubstitute for tester

## Prosjektstruktur

```
src/Parorendeportalen.Api/   Controllers → Services → Repositories → Integrations
tests/Parorendeportalen.Api.Tests/
fhir/                        lokale FHIR-profiler + validering
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

# valgfritt: gir omsorgsmottakerne et fødselsnummer, så synkroniseringen finner dem
dotnet user-secrets set "CareRecipients:Seed:0:Name" "Vigdis Quist" --project src/Parorendeportalen.Api
dotnet user-secrets set "CareRecipients:Seed:0:NationalId" "<syntetisk nummer fra Tenor>" --project src/Parorendeportalen.Api

# 3. kjør API
dotnet run --project src/Parorendeportalen.Api
```

API-et lytter på `http://localhost:5109` (se `Properties/launchSettings.json`). Migreringer kjører automatisk ved oppstart.

`Kinship:SeedGrants:0:NationalId` kobler testidentiteten din til en omsorgsmottaker, så du kommer forbi innlogging. Du lager en testidentitet på https://ra-preprod.bankidnorge.no/#!/generate og bruker nummeret derfra. `Idura:ClientSecret` trengs for OIDC-innlogging i Development og Production (ikke i Demo-modus).

Testene starter en Postgres i Docker med Testcontainers, så Docker må kjøre:

```bash
dotnet test
```

Kode formateres med CSharpier (`dotnet csharpier format .`), CI feiler hvis den ikke er kjørt. Pakker er låst med `packages.lock.json` - legger du til eller oppdaterer en pakke, kjør `dotnet restore` på nytt og commit den oppdaterte lock-filen.

## CI

GitHub Actions kjører restore, format-sjekk, build og test på hver PR mot `main` ([.github/workflows/ci.yml](.github/workflows/ci.yml)).

## Innlogging

To ulike oppsett, styrt av `ASPNETCORE_ENVIRONMENT`:

- **Development/Production** - OIDC-innlogging via Idura/BankID (krever `Idura:ClientSecret` som user-secret).
- **Demo** - en fake auth-handler til demo-deploy, ingen identitetsleverandør nødvendig.

## Endepunkter så langt

- `GET /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`
- `GET /api/carerecipients`, `GET /api/carerecipients/{id}`
- `GET /api/visits?careRecipientId={id}`, `GET /api/visits/{id}?careRecipientId={id}`
- `GET /health` (anonym)

En pårørende kan ha tilgang til flere tjenestemottakere, så `careRecipientId` er
påkrevd på visits-endepunktene. `GET /api/auth/me` returnerer hvilke
tjenestemottakere du har tilgang til.

## Synkronisering

`Integrations/Synthetic/` er kilden besøkene kommer fra i dag, en stand-in for et
kommunalt EPJ. `Integrations/Sync/` kjører den: én `BackgroundService` per kilde,
med et vannmerke nøklet `(SourceSystem, ResourceType)` og en idempotent upsert på
`(Origin, ExternalId)`. Hver kjøring legger igjen en rad i `SyncRuns` med status,
tellere og eventuell feil.

Vannmerket flyttes bare når en kjøring går gjennom, så pollintervallet er
retryen: neste tikk henter nøyaktig det forrige feilet på. Rader med
`Origin.Portal` er skrevet av en pårørende og røres aldri.

```json
"VisitSync": {
  "Enabled": true,
  "PollInterval": "00:15:00",
  "MaxBackoffMultiplier": 8
}
```

Uten `CareRecipients:Seed` kjenner ikke portalen noe nummer for
omsorgsmottakerne. Synkroniseringen finner dem ikke, teller snapshotene som
uløste, og seederen legger inn noen besøk for hånd i stedet.

Hver seed-oppføring får en nøkkel som besøks-id-ene bygges av. Setter du den
ikke selv, brukes `Name`:

```bash
dotnet user-secrets set "CareRecipients:Seed:0:Key" "pasient-4711" --project src/Parorendeportalen.Api
```

Nøkkelen må følge personen. Endrer du den, eller endrer du `Name` uten å ha satt
en `Key`, får besøkene til den personen nye id-er og legges inn på nytt ved
siden av de gamle.

## FHIR

Integrasjonsporten (`Integrations/`) henter besøk fra eksterne kilder. `no-basis`
mangler profiler for `Encounter` og `CarePlan`, så de er definert lokalt i
[fhir/](fhir/README.md) og valideres mot HL7 sin egen validator.

```powershell
./fhir/validate.ps1
```

## Dokumentasjon

Kommer

