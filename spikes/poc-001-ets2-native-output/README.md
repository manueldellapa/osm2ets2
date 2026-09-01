# PoC-001 — ETS2 Native Output Feasibility

**Stato del gate: `AWAITING_MANUAL_VALIDATION`.** La generazione e la rilettura
automatica sono riuscite; il ciclo obbligatorio nel Map Editor ETS2 1.60.x su
Windows 11 x64 non è stato eseguito.

Questo progetto è uno spike isolato. Non è un adapter di produzione e non fa
parte automaticamente dell'architettura dell'MVP. Crea soltanto una mappa con
una strada rettilinea fra `(100, 0, 100)` e `(200, 0, 100)` usando le API reali di
TruckLib 0.5.1.

## Esecuzione

Serve il .NET SDK selezionato da `global.json`. Il lock fissa TruckLib 0.5.1 e le
dipendenze transitive risolte.

```bash
cd spikes/poc-001-ets2-native-output
dotnet restore --locked-mode
dotnet run
```

Senza argomenti l'output va in `output/run-current/`. Per conservare due
esecuzioni indipendenti:

```bash
dotnet run -- run-01
dotnet run -- run-02
```

Ogni esecuzione produce:

```text
output/<run-id>/
├── automatic-validation.json
└── map/
    ├── poc001_minimal.mbd
    └── poc001_minimal/
        ├── sec+0000+0000.aux
        ├── sec+0000+0000.base
        ├── sec+0000+0000.data
        ├── sec+0000+0000.desc
        └── sec+0000+0000.snd
```

Il programma verifica formato 907, conteggi, UID, coordinate, proprietà della
strada e riferimenti dopo `Map.Open`. L'assenza di `.layer` è intenzionale nel
writer TruckLib quando tutti gli elementi usano il layer predefinito; la sua
accettazione da parte del Map Editor resta da provare.

## Controllo dopo il salvataggio dell'editor

Dopo aver copiato il set salvato dal Map Editor in una directory isolata, è
possibile rileggerlo con TruckLib e confrontarlo con il manifest originario:

```bash
dotnet run -- --validate-editor-save <saved-mbd-path> <original-manifest-path>
```

Esempio per `run-01`:

```bash
dotnet run -- --validate-editor-save manual-validation/run-01/after-editor/map/poc001_minimal.mbd output/run-01/automatic-validation.json
```

Il controllo richiede una strada con geometria, asset e riferimenti validi.
Registra separatamente se l'editor ha conservato o cambiato gli UID, perché un
cambio deve essere esaminato ma non dimostra da solo una corruzione semantica.

La procedura completa e i nomi delle evidenze sono in
[`manual-validation/checklist.md`](manual-validation/checklist.md). Il risultato
tecnico è in [`tasks/poc-001-results.md`](../../tasks/poc-001-results.md).

