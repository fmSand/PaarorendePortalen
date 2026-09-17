# Pårørendeportalen

Backend-API for en pårørendeportal. En pårørende logger inn med BankID og kan se
besøk fra hjemmetjenesten, vedtak og dagsplan for en omsorgsmottaker. Den
pårørende får varsler når et besøk endres, og kan legge inn egne avtaler. Hva hver
pårørende får se, bestemmes av samtykket omsorgsmottakeren har gitt.

Noe av dette finnes allerede. I Oslo viser DigiHelse på helsenorge.no planlagte
hjemmebesøk til pårørende med fullmakt, men ved utgangen av 2024 var bare rundt
30 % av kommunene og drøyt halve befolkningen med. Midt-Norge bruker
Helseplattformen med HelsaMi, og der vises ikke besøk fra hjemmetjenesten.

Dette prosjektet bygger den samme funksjonen som et backend-API, etter publiserte
norske standarder. En privatperson får ikke tilgang til kommunale eller nasjonale
helsesystemer. Det krever blant annet organisasjonsnummer, medlemskap i Norsk
helsenett, at Normen følges og en kommune som kunde. Besøkene kommer derfor fra en
syntetisk kilde med testdata. En kommunal kilde kan kobles på samme sted senere.

## Stack

- ASP.NET Core Web API, .NET 10
- EF Core + PostgreSQL
- OpenID Connect (Idura/BankID) for innlogging
- xUnit + NSubstitute for tester

## Kjøre lokalt

Du trenger .NET 10 SDK (versjonen står i `global.json`) og Docker.

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

API-et kjører på `http://localhost:5109`. Databasen oppdateres automatisk ved
oppstart.

Hva innstillingene er:

- `Kinship:NationalIdPepper`: en hemmelig verdi som brukes når fødselsnumre
  hashes. Lokalt kan du velge hva som helst.
- `Kinship:SeedGrants:0:NationalId`: fødselsnummeret til BankID-testbrukeren din.
  Den blir registrert som pårørende til omsorgsmottakerne. Uten den blir du avvist
  etter innlogging. Testbrukere lages på https://ra-preprod.bankidnorge.no/#!/generate.
- `Idura:ClientSecret`: trengs for BankID-innlogging.

Testdata legges bare inn i en tom database. Har du en database fra en eldre
versjon og får 403 på besøkene, slett den og start på nytt:

```bash
docker compose down -v && docker compose up -d
```

## Utvikling

| Kommando                                                                                           | Hva den gjør                                                       |
| -------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `dotnet tool restore`                                                                              | installerer CSharpier og dotnet-ef fra `.config/dotnet-tools.json` |
| `dotnet csharpier format src tests`                                                                | formaterer koden                                                   |
| `dotnet build Parorendeportalen.slnx -c Release -warnaserror`                                      | bygger med advarsler som feil (som CI)                             |
| `dotnet test Parorendeportalen.slnx -c Release --no-build`                                         | kjører testene på Release-bygget                                   |
| `dotnet ef migrations add <Navn> --project src/Parorendeportalen.Api --output-dir Data/Migrations` | lager en ny migrering                                              |
| `./fhir/validate.ps1`                                                                              | validerer FHIR-profilene, se [FHIR](#fhir)                         |

### Tester

Docker må kjøre. Testene som bruker databasen starter en egen Postgres i Docker
med Testcontainers. Resten er enhetstester med NSubstitute.

Testene i `Pipeline/` starter hele API-et mot en tom database og sender vanlige
HTTP-forespørsler til den. Oppstarten kjører migreringene, så en migrering som
feiler gir en rød test. De sjekker også at en forespørsel som endrer noe blir
avvist når antiforgery-tokenet mangler.

### CI

GitHub Actions kjører restore, formatsjekk, build og test på hver PR mot `main`
([.github/workflows/ci.yml](.github/workflows/ci.yml)). Formatsjekken feiler på kode
som ikke er kjørt gjennom CSharpier.

Pakkeversjoner er låst i `packages.lock.json`, og CI kjører restore med
`--locked-mode`. Legger du til eller oppdaterer en pakke, kjør `dotnet restore` og
commit den oppdaterte lock-filen.

## Struktur

```
src/Parorendeportalen.Api/
  Controllers/        HTTP-endepunkter
  Services/           forretningslogikk og tilgangssjekk
  Repositories/       leser og skriver i portalens database
  Models/, Dtos/      entiteter og API-kontrakter
  Data/               DbContext, migreringer og testdata
  Integrations/       henter besøk fra kildesystemer (Sync/, Synthetic/)
  Notifications/      bakgrunnsjobben som lager varsler
  Authentication/     BankID via Idura
  Middleware/, Filters/, Extensions/
                      sikkerhetsheadere, antiforgery og oppsett
tests/Parorendeportalen.Api.Tests/
                      samme mapper som src/, pluss Pipeline/
fhir/                 FHIR-profiler og validering
```

`Models/`, `Services/`, `Dtos/` og `Repositories/` har undermapper per område:

| Mappe            | Inneholder                               |
| ---------------- | ---------------------------------------- |
| `Access/`        | Samtykke og tilgangslogg                 |
| `Kinship/`       | Omsorgsmottakere, pårørende og slektskap |
| `Notifications/` | Varsler og varselinnstillinger           |
| `Planning/`      | Vedtak og dagsplan                       |
| `Visits/`        | Besøk, egne avtaler og kommentarer       |

`Integrations/` henter data fra andre systemer og skriver dem inn i databasen. Metodene der heter derfor
`Fetch…ChangedSinceAsync`, og de i `Repositories/` heter `Get…Async`.

## Innlogging

Innlogging med BankID via Idura.

Antiforgery-cookien krever HTTPS i alle miljøer utenom Development, så
forespørsler som endrer noe må gå over HTTPS der.

## Tilgang og samtykke

To ting må være på plass før en pårørende får se noe om en omsorgsmottaker:

1. **Slektskap** (`KinshipGrant`): personen er registrert som pårørende til
   omsorgsmottakeren. Mangler det, svarer API-et 404, så det ikke avslører om
   omsorgsmottakeren finnes.
2. **Samtykke** (`Consent`): omsorgsmottakeren har samtykket til å dele denne
   typen informasjon, for eksempel besøk eller vedtak. Mangler det, svarer API-et 403.

Alle oppslag og endringer i helsedata sjekkes i `IHealthDataAccessPolicy`. Før
svaret sendes, lagres en rad i tilgangsloggen (`AccessLogEntries`): hvem, hvilken
omsorgsmottaker, hvilken kategori, om det var lesing eller skriving, og om
tilgangen ble gitt. Loggen lagrer bare id-er. Navn og fødselsnummer står ikke der.
Portalen legger bare til rader i loggen, og loggen kan ikke leses gjennom API-et.

Slektskap og samtykke kan ikke opprettes eller endres i portalen. I drift ville de
kommet fra nasjonale løsninger. Her legges de inn som testdata ved oppstart.

## Egne avtaler og kommentarer

En pårørende kan legge inn egne avtaler i kalenderen, for eksempel en legetime.
Pårørende kan også skrive kommentarer på besøk, både på egne avtaler og på besøk
fra hjemmetjenesten.

Når du legger inn en avtale eller en kommentar, velger du hvem som kan se den:

| Verdi     | Hvem ser den                                      |
| --------- | ------------------------------------------------- |
| `Private` | Bare du                                           |
| `Shared`  | Alle pårørende som har samtykke til å se besøkene |

Bare den som skrev en avtale eller kommentar kan endre eller slette den. Besøk fra
hjemmetjenesten kan ikke endres i portalen.

Egne avtaler lagres sammen med besøkene fra hjemmetjenesten. Synkroniseringen
endrer dem aldri, og de teller ikke med i dagsplanen.

## Vedtak og dagsplan

Et vedtak er kommunens beslutning om hvilken hjelp en omsorgsmottaker skal få. Det
har en tjenestetype, en regel for når tjenesten gis, og en liste med oppgaver.

Regelen sier hvilke ukedager og hvor mange ganger per dag.
"Hjemmesykepleie x2/dag man-fre" blir mandag til fredag, to ganger hver dag.
Regelen kan ikke uttrykke "annenhver uke". Den tilsvarer `Timing.repeat` i FHIR,
se CarePlan-profilen i `fhir/`.

Dagsplanen lagres ikke. Den regnes ut hver gang den hentes: vedtakene som gjelder
på datoen sier hvilke besøk som skal skje, og besøkene hjemmetjenesten har
rapportert viser hvordan det gikk.

| Verdi       | Betyr                                                              |
| ----------- | ------------------------------------------------------------------ |
| `Expected`  | Vedtaket gir et besøk, men hjemmetjenesten har ikke rapportert noe |
| `Planned`   | Besøket er planlagt                                                |
| `Completed` | Besøket er gjennomført                                             |
| `Missed`    | Besøket ble ikke gjennomført                                       |
| `Cancelled` | Besøket er avlyst                                                  |

Et besøk teller bare for vedtak med samme tjenestetype. Har hjemmetjenesten
rapportert flere besøk enn vedtaket gir, vises de ekstra besøkene også.

Datoen regnes i norsk tid (`Europe/Oslo`). Det gjelder også de to dagene i året
når vi bytter mellom sommertid og vintertid.

Vedtak har sin egen samtykkekategori. En pårørende kan ha lov til å se besøkene
uten å få se vedtakene. Dagsplanen krever samtykke til begge.

## Varsler

Når synkroniseringen ser at et besøk er lagt til, flyttet, utført, avlyst, ikke
utført eller endret, lagres en hendelse sammen med besøket. En bakgrunnsjobb går
gjennom nye hendelser og lager ett varsel til hver pårørende som har slektskap og
samtykke for kategorien.

Et varsel sier bare hva som skjedde, hvilket besøk og når. Notatene fra besøket
er ikke med.

- `GET /api/notifications` gir de 50 siste varslene for alle omsorgsmottakerne
  du følger, og hvor mange som er uleste.
- Trekker omsorgsmottakeren samtykket, vises varslene ikke lenger.
- `PUT /api/notifications/preferences/{kind}` med `{ "enabled": false }` skrur av
  én type varsel. Alle er på fra start.

| Verdi         | Vises som                    |
| ------------- | ---------------------------- |
| `Added`       | Nytt besøk                   |
| `Rescheduled` | Besøket er flyttet           |
| `Completed`   | Besøket er fullført          |
| `Cancelled`   | Besøket er avlyst            |
| `Missed`      | Besøket ble ikke gjennomført |
| `Updated`     | Besøket er endret            |

Innstillinger i `appsettings.json`:

```json
"Notifications": {
  "Enabled": true,
  "PollInterval": "00:01:00",
  "BatchSize": 100
}
```

`PollInterval` er hvor ofte jobben ser etter nye hendelser, og `BatchSize` hvor
mange den tar om gangen.

## Synkronisering

Besøkene kommer i dag fra en syntetisk kilde i `Integrations/Synthetic/`, som
lager testdata slik en kommunal journal kunne gjort. `Integrations/Sync/` henter
nye og endrede besøk fra kilden.

- Hver kilde har sin egen bakgrunnsjobb, så en kilde som er nede ikke stopper de andre.
- Portalen husker hvor langt den har kommet, og henter bare det som er endret siden sist.
- Et besøk som hentes flere ganger, lagres bare én gang.
- Feiler en kjøring, starter neste kjøring fra samme sted. Etter flere feil på
  rad venter jobben lenger, opptil 8 ganger det vanlige intervallet.
- Hver kjøring lagres i `SyncRuns` med status, hvor mange besøk som ble lagt til eller endret, og eventuell feil.
- Egne avtaler som pårørende har lagt inn, endres aldri av synkroniseringen.

Innstillinger i `appsettings.json`:

```json
"VisitSync": {
  "Enabled": true,
  "PollInterval": "00:15:00",
  "MaxBackoffMultiplier": 8
}
```

Synkroniseringen kobler besøk til omsorgsmottakere ved hjelp av fødselsnummer.
Uten `CareRecipients:Seed` har omsorgsmottakerne ikke noe fødselsnummer, så
synkroniseringen finner ingen å koble besøkene til. Da legger oppstarten inn noen
testbesøk for dem.

Hver oppføring i `CareRecipients:Seed` kan ha en `Key`. Den brukes i id-ene til
besøkene fra den syntetiske kilden, og er lik `Name` hvis du ikke setter den.
Endrer du den, får omsorgsmottakeren et nytt sett besøk, og de gamle blir liggende.

## API

En pårørende kan følge flere omsorgsmottakere, så de fleste endepunktene krever
`careRecipientId`. `GET /api/auth/me` viser hvem du har tilgang til, og
`GET /api/consents?careRecipientId={id}` hvilke kategorier du kan se for en av
dem.

[Parorendeportalen.Api.http](src/Parorendeportalen.Api/Parorendeportalen.Api.http)
har eksempler på forespørslene. I Development ligger OpenAPI-dokumentet på
`/openapi/v1.json`.

### Endepunkter

- `GET /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`
- `GET /api/antiforgery/token`
- `GET /api/carerecipients`, `GET /api/carerecipients/{id}`
- `GET /api/visits?careRecipientId={id}`, `GET /api/visits/{id}?careRecipientId={id}`
- `POST /api/visits`, `PUT /api/visits/{id}?careRecipientId={id}`, `DELETE /api/visits/{id}?careRecipientId={id}`
- `GET`/`POST /api/visits/{visitId}/comments?careRecipientId={id}`
- `PUT`/`DELETE /api/visits/{visitId}/comments/{id}?careRecipientId={id}`
- `GET /api/vedtak?careRecipientId={id}`, `GET /api/vedtak/{id}?careRecipientId={id}`
- `GET /api/dayplan?careRecipientId={id}&date={yyyy-MM-dd}`
- `GET /api/consents?careRecipientId={id}`
- `GET /api/notifications`, `POST /api/notifications/{id}/read`, `POST /api/notifications/read`
- `GET /api/notifications/preferences`, `PUT /api/notifications/preferences/{kind}`
- `GET /health` (krever ikke innlogging)

### Headere

Alle forespørsler som endrer noe (POST, PUT og DELETE) må ha med `X-XSRF-TOKEN`.
Verdien hentes fra `GET /api/antiforgery/token`. Tokenet hindrer at en annen
nettside kan sende forespørsler i ditt navn.

PUT og DELETE på besøk og kommentarer må i tillegg ha med `If-Match`, med
versjonen du fikk da du leste oppføringen, for eksempel `If-Match: "1234"`.
Versjonen står i feltet `version` og i `ETag`-headeren på svaret. Slik overskriver
ikke to pårørende hverandres endringer når de redigerer samme oppføring samtidig.

### Tidspunkter

Alle tidspunkter lagres og returneres i UTC. Du kan sende tidspunkt med hvilken
som helst offset. Eks: `2027-06-15T08:00:00+02:00` eller `2027-06-15T06:00:00Z`.

Tidspunktene du får tilbake har alltid offset `+00:00`:

```json
"scheduledAt": "2027-06-15T06:00:00+00:00"
```

Unntak: Datoen i `GET /api/dayplan?date={yyyy-MM-dd}` leses som norsk dato, se [Vedtak og dagsplan](#vedtak-og-dagsplan).

### Statuskoder

| Kode | Betyr                                                                                                                                          |
| ---- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| 400  | Forespørselen er ugyldig, for eksempel mangler `careRecipientId` eller `X-XSRF-TOKEN`, eller `If-Match` har feil format                        |
| 401  | Du er ikke logget inn                                                                                                                          |
| 403  | Omsorgsmottakeren har ikke samtykket til kategorien, eller du prøver å endre noe du ikke skrev eller et besøk fra hjemmetjenesten              |
| 404  | Oppføringen finnes ikke, du er ikke registrert som pårørende til omsorgsmottakeren, eller oppføringen er en annen pårørendes private oppføring |
| 412  | Noen andre har endret oppføringen etter at du leste den. Hent den på nytt                                                                      |
| 428  | `If-Match` mangler                                                                                                                             |
| 429  | Mer enn 100 forespørsler i minuttet fra samme IP-adresse                                                                                       |

## FHIR

De norske basisprofilene (`no-basis`) har ingen profil for `Encounter` (besøk)
eller `CarePlan` (vedtak). Derfor ligger egne profiler i [fhir/](fhir/README.md).
De sjekkes med HL7 sin validator i `./fhir/validate.ps1`. CI kjører den ikke, så
kjør den når du endrer noe under `fhir/`.
