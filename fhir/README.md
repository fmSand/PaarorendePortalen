# FHIR-profiler

Lokale FHIR R4-profiler for `Encounter` og `CarePlan`, og et skript som validerer dem.

## Hvorfor lokale profiler

`no-basis` fra HL7 Norway dekker ikke `Encounter` (gjennomført hjemmebesøk) eller
`CarePlan` (vedtak og dagsplan). Sjekket mot `basisprofiler-r4`: 22
StructureDefinitions, ingen av de to.

Profilene her er derfor lokale, avledet fra internasjonal R4 og med de norske
identifikator-OID-ene. Publiserer HL7 Norway nasjonale profiler senere, går vi
over til dem og sletter disse.

## Kjøre

```powershell
./fhir/validate.ps1
```

Krever Docker. Første kjøring laster ned validator-jar (~200 MB) til `.tools/` og
FHIR-pakkene til Docker-volumet `fhir-package-cache`. Deretter tar en kjøring
rundt et minutt. Rydd cachen med `docker volume rm fhir-package-cache`.

Låste versjoner: validator_cli 6.10.3, hl7.fhir.no.basis 2.2.2. Flere
no-basis-profiler er fortsatt `draft` og kan endre seg, så versjonen oppgraderes
med vilje.

## Innhold

```
profiles/             de to StructureDefinitions og tre ValueSets
examples/valid/       instanser som skal validere rent
examples/invalid/     instanser som hver skal feile på én regel
expected-errors.json  hvilken regel hver invalid-instans skal feile på
validate.ps1          valideringsskriptet
.tools/               validator-jar og rapporter (gitignorert)
```

## Sjekke at reglene virker

Valideringen kjører HL7 sin egen validator i Docker, samme som validator.fhir.org.

`examples/invalid/` tester profilene. Hver fil bryter én regel og skal feile.
Skriptet sjekker også at feilen kommer fra vår profil og treffer riktig regel,
siden en fil kan feile av helt andre grunner.

Skriptet er sjekket ved å ødelegge profilene med vilje. Blir kjøringen grønn da,
tester skriptet ingenting.

Har to filer i `profiles/` samme kanoniske URL, bruker validatoren den ene og
ignorerer den andre uten å si fra. En glemt backup-fil der gjør at alt består.
Skriptet stopper på dette.

## Reglene

Felles: `identifier` er påkrevd, med `system` og `value`. En bar verdi er ikke
unik på tvers av kildesystemer.

**Encounter**

| Regel | Hvorfor |
|---|---|
| `status` begrenset til planned / in-progress / finished / cancelled | det `VisitStatus` kan produsere |
| `class` låst til `HH` | hjemmebasert omsorg |
| `subject` påkrevd, `no-basis-Patient` | bruk no-basis der den finnes |
| `period` og `period.start` påkrevd | et besøk trenger et tidspunkt |

`VisitStatus.Missed` mappes til `cancelled`. R4 har `noshow` på `Appointment`,
men ingenting tilsvarende på `Encounter`.

**CarePlan**

| Regel | Hvorfor |
|---|---|
| `status` begrenset til draft / active / on-hold / revoked / completed | `on-hold` brukes for eksempel under sykehusopphold |
| `intent` begrenset til `order` og `plan` | vedtaket er `order`, dagsplanen er `plan` |
| `period`, `period.start`, `author` påkrevd | et vedtak har gyldighetsperiode og vedtaksmyndighet |
| `activity` påkrevd | et vedtak innvilger minst én tjeneste |
| `activity.detail.scheduled[x]` låst til `Timing` | dagsplanen regnes ut ved å ekspandere regelen, og fritekst kan ikke ekspanderes |
