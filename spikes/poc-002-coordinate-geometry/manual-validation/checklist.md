# PoC-002 v1 — Checklist diagnostica Windows Map Editor

**Stato: `NOT_EXECUTED`.** La validazione automatica ha già assegnato al PoC
lo stato `FAIL`, perché il readback TruckLib prima dell'editor supera 0,001 m.
Questa procedura è pronta per un'eventuale indagine diagnostica autorizzata;
non può trasformare da sola il run corrente in `PASS` e non autorizza PoC-003.

Questa checklist conserva intenzionalmente il criterio originale di v1. Dopo
la revisione DT-07, il rerun distinto resta `NOT_EXECUTED` e usa la
[`specifica congelata revisionata`](../revised-rerun-spec.md). Le due
esecuzioni non devono condividere o sovrascrivere evidenze.

## Baseline obbligatoria

- [ ] Windows 11 x64 registrato con build completa.
- [ ] ETS2 stabile `1.60.1.7s`/build sperimentale già validata da PoC-001.
- [ ] Map Editor `win_x64`, profilo isolato e mod Workshop disattivati.
- [ ] TruckLib `0.5.1`, .NET SDK `10.0.400`, runtime `10.0.11` per il readback.
- [ ] Nessun cambio di build ETS2, catalogo, TruckLib, fixture, scala, soglia o
      geometria rispetto al run automatico.
- [ ] Hash di `neutral-model.json` uguale a
      `169c6b77226ca9d3d5d6f79a25b10d70b76ddb2d6613248d857ac33027c0e33e`.

Se una versione non coincide, fermarsi e classificare il tentativo come una
nuova baseline, non come completamento del run corrente.

## Preparazione immutabile

Usare un ID di run nuovo, per esempio `windows-01`, sotto
`manual-validation/runs/`. Conservare tre alberi separati:

```text
manual-validation/runs/windows-01/
├── before-editor/
│   └── maps/<map-id>/<map-id>.mbd + <map-id>/sector files
├── after-editor/
│   └── maps/<map-id>/<map-id>.mbd + <map-id>/sector files
├── logs/
├── screenshots/
└── editor-readback.json
```

- [ ] Copiare senza modifiche il run nativo scelto in `before-editor/`.
- [ ] Registrare inventario, dimensione e SHA-256 di ogni file.
- [ ] Verificare sei mappe, otto Road e trentasei file nativi: sei `.mbd` e sei
      gruppi `.aux/.base/.data/.desc/.snd`.
- [ ] Conservare `before-editor/` immutato; lavorare soltanto su una copia.
- [ ] Per ogni map-id, copiare nella directory Windows
      `Documents\Euro Truck Simulator 2\mod\user_map\map\` sia `<map-id>.mbd`
      sia la directory sorella `<map-id>\` con i settori.

Map-id obbligatori:

```text
east-scale-1
north-scale-1
oblique-scale-1
oblique-scale-0.1
tiny-offsets
near-native-radius
```

## Valori da riconoscere

Le coordinate seguenti sono aspettative native `X/Y/Z` in metri della scena,
non una dichiarazione che il Map Editor usi già una semantica geografica
confermata.

| Mappa/Road | Inizio atteso | Fine attesa |
| --- | --- | --- |
| `east-scale-1` | `(0, 0, 0)` | `(123,456000002; 0; ≈0)` |
| `north-scale-1` | `(0, 0, 0)` | `(≈0; 0; -234,567000000)` |
| `oblique-scale-1` | `(0, 0, 0)` | `(193,815694746; 0; -256,038000301)` |
| `oblique-scale-0.1` | `(0, 0, 0)` | `(19,381569475; 0; -25,603800030)` |
| `tiny-offset-0.001` | `(0,001; 0; 0)` | `(100,001; 0; 0)` |
| `tiny-offset-0.01` | `(0,01; 0; 0)` | `(100,01; 0; 0)` |
| `tiny-offset-0.1` | `(0,1; 0; 0)` | `(100,1; 0; 0)` |
| `near-native-radius` | `(9668,731155428; 0; -506,716728347)` | `(9874,448839591; 0; -517,497935331)` |

Il manifest automatico contiene i valori non arrotondati e il readback
TruckLib già quantizzato. Non usare una schermata per la misura millimetrica.

## Ciclo obbligatorio per ciascuna mappa

Ripetere l'intero ciclo per tutti e sei i map-id. Non riparare, spostare,
ricreare o collegare manualmente alcuna Road.

1. [ ] Avviare il Map Editor con la mappa esplicita e senza Workshop, per
       esempio `eurotrucks2.exe -edit east-scale-1 -noworkshop`.
2. [ ] Conservare `editor.log.txt` dall'avvio e registrare il comando reale.
3. [ ] Confermare caricamento della mappa e di tutti i settori senza errore
       bloccante.
4. [ ] Selezionare ogni Road; registrare UID, nodi e coordinate mostrate.
5. [ ] Controllare verso backward → forward, posizione, asse verticale `Y=0`,
       segni X/Z e assenza di riflessione o rotazione inattesa.
6. [ ] Confrontare `oblique-scale-1` con `oblique-scale-0.1`: stessa direzione e
       rapporto 0,1, senza scaling tramite metadata.
7. [ ] Confermare che le tre Road tiny restino lunghe circa 100 m e siano solo
       traslate; non interpretarle come Road millimetriche.
8. [ ] Eseguire **Map → Recompute map** e registrare ogni warning/error con la
       sua classificazione.
9. [ ] Salvare la mappa senza altre modifiche e registrare il messaggio di
       salvataggio riuscito.
10. [ ] Chiudere completamente Map Editor ed ETS2; non lasciare il processo in
        background.
11. [ ] Copiare log e inventario intermedio senza sovrascrivere le evidenze.
12. [ ] Riaprire la stessa mappa salvata con `-edit <map-id> -noworkshop`.
13. [ ] Ripetere selezione, UID, coordinate, direzione, posizione, scala e
        orientamento.
14. [ ] Chiudere completamente l'editor e conservare il secondo log.

Una screenshot orientata può supportare i punti 4–7, ma non prova né sostituisce
la soglia da 0,001 m.

## Raccolta dopo editor e readback numerico

- [ ] Copiare ogni set salvato in
      `after-editor/maps/<map-id>/`, mantenendo `.mbd` e directory settori
      sorelle.
- [ ] Registrare inventario, dimensione e SHA-256 di tutti i file aggiunti,
      rimossi o cambiati dall'editor.
- [ ] Copiare `after-editor/` sulla macchina con l'adapter, senza normalizzare o
      riparare i file.
- [ ] Eseguire dalla directory `csharp/`:

```bash
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- \
  --validate-editor-save \
  ../manual-validation/runs/windows-01/after-editor \
  ../output/run-automatic/neutral-model.json \
  ../manual-validation/runs/windows-01/editor-readback.json
```

- [ ] Conservare stdout, stderr, exit code e `editor-readback.json`.
- [ ] Verificare numericamente ogni estremo dopo la riapertura; il massimo
      errore aggiunto complessivo deve essere `<= 0,001 m` senza arrotondare il
      valore presentato.
- [ ] Verificare Hausdorff dei rettifili `<= 1,0 m`, raggio `<= 10.000 m`,
      verso preservato e assenza di rotazione/riflessione inattesa.
- [ ] Separare chiaramente osservazione visiva, readback TruckLib e inferenze.

Il readback TruckLib è diagnostico e non dimostra che i passaggi manuali siano
avvenuti. Nel run corrente è atteso un esito numerico `FAIL` già prima
dell'editor; anche un ciclo editor completato non rimuove quel fallimento.

## Chiusura del verbale diagnostico

- [ ] Registrare build Windows/ETS2 completa, catalogo/mod attivi, comandi,
      operatore, timestamp e hash.
- [ ] Allegare i due log distinti per ogni mappa e le immagini contestualizzate.
- [ ] Descrivere warning/errori senza riclassificarli silenziosamente.
- [ ] Dichiarare esplicitamente `X=E, Y=H, Z=-N` confermata o smentita per
      direzione/orientamento editor, separandola dalla precisione già fallita.
- [ ] Non modificare soglie, fixture o PoC-001; non iniziare PoC-003.

## Criterio post-editor proposto durante la RCA (nota storica)

La [RCA Q256](../evidence/native-q256-rca.md) conferma che TruckLib 0.5.1
serializza ogni componente di `Node.Position` come
`trunc(float32_axis*256)/256`. Ciò non modifica le caselle e il confronto
`<= 0,001 m` congelati sopra: il run corrente resta `FAIL`.

Alla data della RCA, una futura decisione PRD avrebbe potuto adottare un
modello consapevole di Q256. Le aspettative proposte erano le seguenti e non
vanno spuntate nel run corrente:

- [ ] calcolare prima dell'editor l'`Int32` atteso per ogni nodo e asse come
      `trunc_toward_zero(float32_scene_axis * 256)`;
- [ ] conservare i codici pre-editor dopo il readback TruckLib;
- [ ] completare comunque ispezione, **Map → Recompute map**, save, chiusura
      completa, riapertura e nuova ispezione;
- [ ] richiedere che ogni codice X/Y/Z post-editor sia identico sia al codice
      atteso sia al codice pre-editor;
- [ ] classificare ogni cambiamento di codice come drift aggiuntivo, salvo una
      diversa trasformazione nativa dimostrata e approvata esplicitamente;
- [ ] non concedere automaticamente un altro intervallo di 1/256 m a ogni save:
      Q256 è idempotente nel dominio del PoC.

La revisione DT-07 del 2 settembre 2026 ha successivamente adottato questo
modello per un **nuovo rerun**, senza modificare il run o la checklist v1. I
criteri normativi e il ciclo futuro sono in
[`revised-rerun-spec.md`](../revised-rerun-spec.md); il loro stato resta
`NOT_EXECUTED`.
