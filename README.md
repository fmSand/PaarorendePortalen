# Pårørendeportalen - backend

Backend-API for en pårørendeportal. En registrert pårørende logger inn med BankID
og ser besøksloggen til en omsorgsmottaker, avgrenset til kategoriene
omsorgsmottakeren har samtykket til å dele.

Funksjonen finnes delvis fra før. I Oslo viser DigiHelse på helsenorge.no
planlagte hjemmebesøk til pårørende med fullmakt, men ved utgangen av 2024 var
rundt 30 % av kommunene og drøyt halve befolkningen dekket. Midt-Norge kjører
Helseplattformen med HelsaMi i stedet, og der mangler besøk fra hjemmetjenesten
helt.

Dette er den samme oppgaven løst på nytt på backend, mot publiserte norske
standarder. Ingen kommunal eller nasjonal kilde er åpen for en privatperson:
tilgang krever organisasjonsnummer, medlemskap i Helsenett, Normen-etterlevelse og en kommunal
kunde. Besøkene kommer derfor fra en syntetisk kilde, bak samme port en kommunal
kilde ville koblet seg på.

## Stack

- ASP.NET Core Web API, .NET 10
- EF Core + PostgreSQL
- OpenID Connect (Idura/BankID) for innlogging, pluss en demo-auth (WIP)
- xUnit + NSubstitute for tester

## Struktur

```
src/Parorendeportalen.Api/   Controllers → Services → Repositories
tests/Parorendeportalen.Api.Tests/
fhir/                        lokale FHIR-profiler + validering
```

`Repositories/` leser fra egen database og serverer API-et. `Integrations/`
henter fra fremmede systemer for å skrive inn.
Derfor heter portene der `Fetch…ChangedSinceAsync` mens repositories heter `Get…Async`, og
derfor har `Integrations/` sin egen skrivesti.

## Kjøre lokalt

Forutsetter .NET 10 SDK (låst i `global.json`) og Docker.

```bash
# 1. start Postgres
docker compose up -d

# 2. sett user-secrets
dotnet user-secrets set "Kinship:NationalIdPepper" "<en-vilkårlig-dev-verdi>" --project src/Parorendeportalen.Api
dotnet user-secrets set "Kinship:SeedGrants:0:NationalId" "<nummeret til testidentiteten>" --project src/Parorendeportalen.Api
dotnet user-secrets set "Idura:ClientSecret" "<hemmelig fra Idura>" --project src/Parorendeportalen.Api

# valgfritt: gir omsorgsmottakerne et fødselsnummer, så synkroniseringen finner dem
dotnet user-secrets set "CareRecipients:Seed:0:Name" "Vigdis Quist" --project src/Parorendeportalen.Api
dotnet user-secrets set "CareRecipients:Seed:0:NationalId" "<syntetisk nummer fra Tenor>" --project src/Parorendeportalen.Api

# 3. kjør API
dotnet run --project src/Parorendeportalen.Api
```

API-et lytter på `http://localhost:5109` (se `Properties/launchSettings.json`).
Migreringer kjører automatisk ved oppstart.

`Kinship:SeedGrants:0:NationalId` kobler testidentiteten din til en
omsorgsmottaker, så du kommer forbi innlogging. Testidentiteter lages på
https://ra-preprod.bankidnorge.no/#!/generate. `Idura:ClientSecret` trengs i
Development og Production, og ikke i Demo.

Har du en database fra før, må den bort (seederen hopper over en database med
rader, så den blir stående uten samtykke og besøkslisten svarer 403):

```bash
docker compose down -v && docker compose up -d
```

## Tester

```bash
dotnet test
```

Repository- og integrasjonstestene kjører mot en Postgres som Testcontainers
starter i Docker, så Docker må kjøre. Resten er enhetstester med NSubstitute.

## Innlogging

To oppsett, styrt av `ASPNETCORE_ENVIRONMENT`:

- **Development/Production** - OIDC-innlogging via Idura/BankID.
- **Demo** - en fake auth-handler til demo-deploy, ingen identitetsleverandør nødvendig.

## Tilgangsstyring og samtykke

To porter. En `KinshipGrant` sier hvem som kan se noe om en omsorgsmottaker, et
`Consent` sier hvilke kategorier de kan se, og et oppslag trenger begge. Mangler
slektskapet, får du 404. Mangler samtykket, får du 403.

All lesing av helsedata går gjennom `IHealthDataAccessPolicy`, som sjekker begge,
skriver en rad i `AccessLogEntries` og svarer først da. Loggen er append-only og
holder interne id-er og en kategori, aldri navn eller fødselsnummer. Den har
ingen leseendepunkt: en pårørende skal ikke se loggen til den de representerer.
Portalen leser samtykker og slektskap, og skriver ingen av delene. Begge seedes,
som en stand-in for de nasjonale komponentene som utsteder dem.

## Endepunkter

- `GET /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`
- `GET /api/carerecipients`, `GET /api/carerecipients/{id}`
- `GET /api/visits?careRecipientId={id}`, `GET /api/visits/{id}?careRecipientId={id}`
- `GET /api/consents?careRecipientId={id}`
- `GET /api/notifications`, `POST /api/notifications/{id}/read`, `POST /api/notifications/read`
- `GET /api/notifications/preferences`, `PUT /api/notifications/preferences/{kind}`
- `GET /health` (anonym)

En pårørende kan ha tilgang til flere omsorgsmottakere, så `careRecipientId` er
påkrevd på visits-endepunktene. `GET /api/auth/me` returnerer hvem du har tilgang
til, og `GET /api/consents` hvilke kategorier du kan se for den enkelte.

## Varsler

Når synkroniseringen ser at et besøk er lagt til, flyttet, gjennomført, avlyst
eller ikke gjennomført, legger den igjen en rad i `ChangeEvents` i samme lagring
som besøket. En egen `BackgroundService` leser de ubehandlede radene og skriver
ett varsel per pårørende som har både slektskap og samtykke for kategorien
akkurat da.

`GET /api/notifications` gir de siste 50 varslene på tvers av omsorgsmottakerne
du følger, pluss antall uleste. Lesingen går gjennom samme tilgangspolicy som
besøksloggen og logges per (omsorgsmottaker, kategori) du har samtykke for. Et
varsel er en peker: type, kategori, omsorgsmottaker, besøks-id og tidspunkt,
aldri notatene. Trekkes samtykket, forsvinner varslene fra innboksen ved neste
lesing.

`PUT /api/notifications/preferences/{kind}` med `{ "enabled": false }` skrur av
en type (`Added`, `Rescheduled`, `Completed`, `Cancelled`, `Missed`, `Updated`).
Alt er på til du velger noe annet.

```json
"Notifications": {
  "Enabled": true,
  "PollInterval": "00:01:00",
  "BatchSize": 100
}
```

## Synkronisering

`Integrations/Synthetic/` er kilden besøkene kommer fra i dag. `Integrations/Sync/`
kjører den: én `BackgroundService` per kilde, med et vannmerke nøklet
`(SourceSystem, ResourceType)` og en idempotent upsert på `(Origin, ExternalId)`.
Hver kjøring legger igjen en rad i `SyncRuns` med status, tellere og eventuell feil.

Vannmerket flyttes bare når en kjøring går gjennom, så pollintervallet er retryen:
neste tikk henter nøyaktig det forrige feilet på. Rader med `Origin.Portal` er
skrevet av en pårørende og røres aldri.

```json
"VisitSync": {
  "Enabled": true,
  "PollInterval": "00:15:00",
  "MaxBackoffMultiplier": 8
}
```

Uten `CareRecipients:Seed` kjenner ikke portalen noe nummer for omsorgsmottakerne.
Synkroniseringen finner dem ikke, teller snapshotene som uløste, og seederen legger
inn noen besøk for hånd i stedet. Hver seed-oppføring får en `Key` som besøks-id-ene
bygges av, med `Name` som standard. Endrer du den, får personen nye besøks-id-er.

## FHIR

`no-basis` mangler profiler for `Encounter` og `CarePlan`, så de er definert lokalt
i [fhir/](fhir/README.md) og valideres mot HL7 sin egen validator.

```powershell
./fhir/validate.ps1
```

## CI

GitHub Actions kjører restore, format-sjekk, build og test på hver PR mot `main`
([.github/workflows/ci.yml](.github/workflows/ci.yml)).

Kode formateres med CSharpier (`dotnet csharpier format src tests`). Pakker er låst
med `packages.lock.json`: legger du til eller oppdaterer en pakke, kjør
`dotnet restore` og commit den oppdaterte lock-filen.
