# PoC-002 revised rerun — Checklist Windows Map Editor

**Stato: `AWAITING_MANUAL_VALIDATION`.** La parte automatica del run
`poc-002-q256-rerun-v2-20260903T171732Z` è `PASS`; questa checklist è il gate
residuo. Non è ancora spuntata, non costituisce `PASS` e non autorizza PoC-003.

PoC-002 v1 resta `FAIL` sotto i criteri originali. Questa procedura applica
soltanto la [specifica revisionata congelata](../revised-rerun-spec.md) e usa
la generazione A già inclusa nell'aggregato automatico.

## Baseline e identità obbligatorie

- [ ] Windows 11 x64, build completa registrata.
- [ ] ETS2 stabile build `1.60.1.7`, Map Editor `win_x64`, profilo isolato e
      mod Workshop disattivati.
- [ ] Nessun cambio di TruckLib 0.5.1, .NET SDK 10.0.400/runtime 10.0.11,
      formato 907, fixture, scale, mapping o soglie.
- [ ] Aggregato automatico SHA-256
      `92fad2485734242539f51dc2b700fd1c269abee980bdec0fcd0d920f3369f9e1`.
- [ ] Adapter pre-editor `generation-a` SHA-256
      `7beb98d915aeb8dc893729d4083699fcc83008cc4dccac1685cde49649e49e2c`.
- [ ] JSON neutro v2 SHA-256
      `cf41d2d620372d238c10ce3f7b6323517f45cb345afb459bc04c8c1767d01651`.
- [ ] Manifest semantico SHA-256
      `5b3e211bb9aaedbedf7713140bf49a61af010f05f33f6edcb38587a67de003cb`.

Se una baseline o un hash non coincide, fermarsi: non sostituire la versione e
non classificare il tentativo come completamento di questo run.

## Preparazione immutabile

Creare un ID manuale nuovo sotto `manual-validation/runs/`, per esempio:

```text
manual-validation/runs/windows-v2-01/
├── before-editor/
│   ├── automatic-validation.json
│   ├── adapter-validation-v2.json
│   └── maps/<map-id>/<map-id>.mbd + sector files
├── working-copy/
│   └── maps/<map-id>/<map-id>.mbd + sector files
├── after-editor/
│   └── maps/<map-id>/<map-id>.mbd + sector files
├── logs/
├── screenshots/
├── manual-record.md
└── editor-readback-v2.json
```

- [ ] Copiare l'intera `native-generation-a` in `before-editor/` e registrare
      inventario, dimensioni e SHA-256 prima di qualsiasi apertura.
- [ ] Verificare 6 mappe, 8 Road, 16 nodi e 36 file nativi: 6 `.mbd` più 6
      gruppi `.aux/.base/.data/.desc/.snd`.
- [ ] Conservare `before-editor/` immutabile e lavorare esclusivamente su una
      copia byte-per-byte in `working-copy/`.
- [ ] Conservare dal report adapter UID dei nodi e 48 terne
      `expected_q/written_q/readback_q`; prima dell'editor devono essere tutte
      uguali.

Map-id obbligatori:

```text
east-scale-1
north-scale-1
oblique-scale-1
oblique-scale-0.1
tiny-offsets
near-native-radius
```

Per ogni map-id, copiare nella directory Windows di lavoro sia `<map-id>.mbd`
sia la directory settori sorella `<map-id>/`. Non combinare le sei mappe e non
creare collegamenti fra Road.

## Ciclo obbligatorio per ogni mappa

Ripetere tutti i passi per ciascuno dei sei map-id. Non spostare, riparare,
ricreare, collegare o ridisegnare alcun elemento.

1. [ ] Avviare il Map Editor sulla mappa esplicita, per esempio
       `eurotrucks2.exe -edit east-scale-1 -noworkshop`, e registrare il
       comando effettivo.
2. [ ] Conservare `editor.log.txt` dell'avvio e verificare che mappa e settori
       siano caricati senza errore bloccante.
3. [ ] Selezionare ogni Road e registrare UID, nodi e coordinate mostrate.
4. [ ] Ispezionare posizione, verso backward→forward, asse verticale `Y=0`,
       segni X/Z, orientamento e assenza di riflessioni/rotazioni inattese.
5. [ ] Confrontare `oblique-scale-1` e `oblique-scale-0.1`: stesso orientamento
       e rapporto 0,1; nessuno scaling tramite metadata.
6. [ ] Confermare che gli offset 0,001/0,01/0,1 m siano traslazioni di Road da
       circa 100 m, non Road millimetriche.
7. [ ] Eseguire **Map → Recompute map** e registrare integralmente warning ed
       errori con relativa classificazione.
8. [ ] Salvare senza altre modifiche e registrare il salvataggio riuscito.
9. [ ] Chiudere completamente Map Editor ed ETS2 e verificare che il processo
       non resti in background.
10. [ ] Riaprire la stessa mappa salvata con `-edit <map-id> -noworkshop`.
11. [ ] Ripetere la selezione e il controllo di UID, nodi, posizione, verso,
        scala, segni e orientamento.
12. [ ] Chiudere completamente l'editor e conservare separatamente entrambi i
        log e gli screenshot contestualizzati.

Una schermata supporta soltanto l'ispezione visuale; non prova la stabilità
Q256 né l'avvenuta chiusura/riapertura.

## Raccolta e confronto numerico post-editor

- [ ] Copiare il progetto effettivamente riaperto in
      `after-editor/maps/<map-id>/`, preservando `.mbd` e settori.
- [ ] Registrare file aggiunti, rimossi o modificati e relativi SHA-256.
- [ ] Riportare l'albero `after-editor/` sulla macchina dell'adapter senza
      normalizzarlo o ripararlo.
- [ ] Dalla directory `csharp/`, eseguire:

```bash
/usr/local/share/dotnet/dotnet restore --locked-mode
/usr/local/share/dotnet/dotnet build --configuration Release --no-restore
/usr/local/share/dotnet/dotnet run --configuration Release --no-build -- \
  --validate-revised-editor-save \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/automatic-validation.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-a/adapter-validation-v2.json \
  ../manual-validation/runs/windows-v2-01/after-editor \
  ../manual-validation/runs/windows-v2-01/editor-readback-v2.json
```

- [ ] Conservare stdout, stderr, exit code e `editor-readback-v2.json`.
- [ ] Verificare **48/48** confronti esatti, per UID e componente:
      `q_after = q_before = q_expected`.
- [ ] Richiedere `delta_q = 0` per X, Y e Z. Non applicare tolleranze floating
      e non concedere un ulteriore intervallo 1/256 m dopo il save.
- [ ] Se un codice cambia, registrare mappa, Road, UID, asse, prima/dopo e
      delta; classificare la persistenza `FAIL` e investigare.

Il readback TruckLib resta diagnostico: anche 48/48 uguaglianze non dimostrano
da sole che l'operatore abbia svolto Recompute, save, chiusura completa e
riapertura. `manual-record.md`, log e inventari devono provarne la sequenza.

## Semantica degli assi e chiusura

- [ ] Dichiarare separatamente `X=E, Y=H, Z=-N` **confermata** o **respinta**
      come semantica geografica visuale nel Map Editor.
- [ ] Motivare l'esito con est/nord/obliquo asimmetrici, versi, segni e
      posizioni osservate; non dedurlo dalla sola aritmetica dell'adapter.
- [ ] Registrare operatore, timestamp, build Windows/ETS2 completa, profilo,
      mod/catalogo attivi, comandi, log, inventari e hash.
- [ ] Verificare che nessuna geometria sia stata riparata manualmente.

Il rerun può diventare `PASS` soltanto se il ciclo manuale è documentato, la
semantica degli assi è confermata e ogni codice Q256 resta identico. In caso
contrario usare `FAIL` o `BLOCKED` secondo la specifica. Non modificare PoC-001
o il run v1 e non iniziare PoC-003 senza una successiva decisione esplicita.
