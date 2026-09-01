# PoC-001 — ETS2 Native Output Feasibility: risultati

**Stato finale: `PASSED`**

**Data fase automatica e validazione manuale: 1 settembre 2026**

La generazione programmata e la rilettura con TruckLib 0.5.1 sono riuscite in
due directory indipendenti. Entrambi gli output sono stati aperti, ricomputati,
salvati, chiusi e riaperti nel Map Editor ETS2 1.60.1.7 su Windows 11 x64. La
Road è rimasta visibile, nativa e selezionabile; il readback finale ha
confermato una Road, due nodi, UID stabili e riferimenti integri. PoC-001 supera
quindi il gate obbligatorio per lo scope minimale. PoC-002 non è stato iniziato.

## Perimetro e baseline

Sono stati implementati esclusivamente una mappa vuota, due nodi e una strada
rettilinea da `A=(100,0,100)` a `B=(200,0,100)`. Non sono presenti OSM, Python,
proiezioni, grafo stradale, intersezioni, prefab, CLI dell'MVP o pipeline
end-to-end.

Gli input canonici sono stati congelati senza modifiche durante l'esecuzione.
Gli hash seguenti identificano la baseline usata; il piano degli spike è stato
aggiornato soltanto dopo la chiusura del gate:

| Documento | SHA-256 |
| --- | --- |
| `tasks/prd-osm2ets2-mvp.md` | `c5e8d6f1a51a8980a042e53b40bf49ee1dc0dc6c8c9d1521a5659e50432e1e97` |
| `tasks/spikes-osm2ets2-mvp.md` | `c04c6c88965b206ddd5cd28c55827af0a0925088231340d8a5c51c530bdec0e2` |

Ambiente automatico: macOS 26.6.2 ARM64, .NET SDK 10.0.400, runtime 10.0.11,
TruckLib NuGet 0.5.1 esatto. L'assembly riporta commit
`bd745344fc52d3b2d70ce9ac7c88d61b99934805` e formato mappa 907. Il lock
comprende tutte le dipendenze transitive risolte; la build Release termina con
zero warning e zero errori.

È stato consultato direttamente il catalogo base di un'installazione legittima
macOS ETS2 1.60.1.7, Steam build `23966373`, per verificare gli asset. Questa
fase è stata seguita dal collaudo target su Windows 11 x64, build OS
`10.0.26200`, Map Editor `win_x64`, ETS2 `1.60.1.7s` revisione
`26c95e307fd5`. Impronte, versioni e fonti sono registrate in
[`baseline-and-source-verification.md`](../spikes/poc-001-ets2-native-output/evidence/baseline-and-source-verification.md).
I log del collaudo riportano un solo mod locale attivo (`user_map`) e zero mod
Workshop. L'installazione monta i DLC ufficiali presenti; gli asset della Road
sono stati verificati separatamente negli archivi base, ma il ciclo editor non
è una prova su un'installazione con i DLC fisicamente rimossi.

## Procedura eseguita

1. Verifica del pacchetto NuGet 0.5.1, del suo `.nuspec`, della documentazione
   pubblica e del sorgente esatto indicato dal pacchetto.
2. Verifica diretta in `def.scs` e `base.scs` dell'unità `road.ger1`, del modello
   `/road_template/ger/ger_road_1.pmd`, del look `ger_1`, della variante
   `broken_de` e dell'edge `ger_sh_15`.
3. Restore in locked mode e compilazione Release del progetto isolato.
4. Creazione con `new Map()` e `Road.Add(...)`; nessun UID assegnato dal PoC.
5. Salvataggio con `Map.Save(...)`, controllo dell'inventario e del formato 907.
6. Riapertura con `Map.Open(...)` e confronto di conteggi, UID, coordinate,
   token e riferimenti.
7. Ripetizione da zero come `run-01` e `run-02`.
8. Smoke test del validatore post-editor sul set non modificato di `run-01`.
   Questo prova il validatore, non il salvataggio dell'editor.
9. Copia separata di ciascun set minimale in
   `Documents\Euro Truck Simulator 2\mod\user_map\map` su Windows 11 x64.
10. Apertura diretta con `eurotrucks2.exe -edit poc001_minimal -noworkshop`;
    verifica visiva e selezione della Road tramite il suo UID.
11. Esecuzione di `Map > Recompute map`, salvataggio e chiusura completa.
12. Riapertura diretta della mappa salvata e nuova selezione della stessa Road.
13. Inventario del lifecycle dei file e conservazione dei log `editor.log.txt`.
14. Readback TruckLib dei due set post-editor e confronto con i manifest
    automatici originari.

## Risultati automatici

Entrambi i run riportano `automaticValidation: PASSED` e
`gateStatus: AWAITING_MANUAL_VALIDATION`.

| Dato | `run-01` | `run-02` |
| --- | --- | --- |
| Formato `.mbd` | 907 | 907 |
| Settori | 1 (`+0000,+0000`) | 1 (`+0000,+0000`) |
| Road item | 1 | 1 |
| Nodi | 2 | 2 |
| Map UID | `0x42CBE855EFF3A4A6` | `0x455BDFB687F05036` |
| Road UID | `0x4009C748E369FF6F` | `0x4F8C798E2AAFFB0B` |
| Nodo backward | `0x4837EA1366D62307` | `0x46BAE13AB352A7A1` |
| Nodo forward | `0x4AD607379629B1FF` | `0x4AAB06802DD6B9F0` |
| Lunghezza riletta | 100 | 100 |
| Esito readback | `PASSED` | `PASSED` |

Ogni output contiene un `.mbd` da 69 byte e un settore con `.base` da 409,
`.data` da 374, `.aux` da 28, `.snd` da 28 e `.desc` da 32 byte. UID e hash dei
file che li contengono differiscono fra i due run; struttura e semantica
controllata coincidono. Gli hash completi sono nei manifest:

- [`run-01/automatic-validation.json`](../spikes/poc-001-ets2-native-output/output/run-01/automatic-validation.json);
- [`run-02/automatic-validation.json`](../spikes/poc-001-ets2-native-output/output/run-02/automatic-validation.json).

I manifest conservano correttamente
`gateStatus: AWAITING_MANUAL_VALIDATION`: descrivono lo stato immediatamente
dopo la sola generazione. Il gate complessivo è chiuso dal verbale manuale
separato.

## Risultati Map Editor e readback finale

Il verbale completo, con log, inventari, warning e confronto binario, è in
[`manual-validation/results.md`](../spikes/poc-001-ets2-native-output/manual-validation/results.md).

| Criterio | `run-01` | `run-02` |
| --- | --- | --- |
| Apertura diretta e caricamento settori | `PASSED` | `PASSED` |
| Road visibile, nativa e selezionabile | `PASSED` | `PASSED` |
| Road UID riconosciuto dall'editor | `PASSED` | `PASSED` |
| `Map > Recompute map` | `PASSED` | `PASSED` |
| Salvataggio e chiusura completa | `PASSED` | `PASSED` |
| Riapertura e nuova selezione | `PASSED` | `PASSED` |
| Readback TruckLib post-editor | `PASSED` | `PASSED` |
| UID e riferimenti preservati | `PASSED` | `PASSED` |

Entrambi i readback finali riportano
`EDITOR_SAVE_TRUCKLIB_READBACK_PASSED`, `Map items: 1`, `Nodes: 2` e
`UID stability: STABLE`. Gli UID elencati nella tabella automatica sono rimasti
invariati dopo l'intero ciclo editor.

Il set minimale aperto direttamente contiene `.mbd`, `.base`, `.data`,
`.desc`, `.aux` e `.snd`, senza `.layer`. Al salvataggio, l'editor aggiunge
`.layer`, `.set`, `.expa`, `autosave/` e la directory `.bak`; riscrive `.base`
ma lascia byte per byte invariati `.mbd`, `.aux`, `.data`, `.desc` e `.snd`.
Nessun file iniziale viene rimosso.

## Risposte agli aspetti tecnici richiesti

| Aspetto | Risultato osservato |
| --- | --- |
| 1. Struttura minima verificata | `map/poc001_minimal.mbd` e cartella sorella `map/poc001_minimal/` con un settore `.base/.data/.desc/.aux/.snd`. Entrambi i run vengono aperti direttamente senza `.layer`. |
| 2. Ruolo e contenuto `.mbd` | TruckLib scrive header 907 e metadati globali: map UID, start position/rotation, un campo settore di significato ignoto upstream, game tag, scale 19/3 e correzione UI Europe. Road e nodi non sono nel `.mbd`. |
| 3. Sector files | `.base` contiene Road e nodi; `.data` il payload Road; `.aux` e `.snd` sono settori vuoti serializzati; `.desc` contiene metadati del settore. `.layer` non viene scritto con il layer predefinito. |
| 4. Identificatori/UID | Map, Road e due nodi hanno UID a 64 bit non zero e distinti generati da TruckLib. I due run producono UID diversi. Il PoC non inventa né forza valori. |
| 5. Nodi | Due nodi esterni: backward/red e forward/green. Il primo punta alla Road in avanti, il secondo all'indietro; i lati terminali sono null. Posizione serializzata fixed-point con fattore 256. |
| 6. Strada | Un `Road` nativo TruckLib, tipo `ger1`, lunghezza riletta 100, zero terreno laterale, lato destro configurato con look `ger_1`, variante `broken_de` ed edge `ger_sh_15`. |
| 7. Coordinate | Input `Vector3` esatti A/B; delta di 100 lungo X. TruckLib descrive le unità motore come metri e il readback post-editor mantiene i valori esatti. La trasformazione geografica resta fuori scope. |
| 8. Assi e orientamento | La settorizzazione TruckLib usa X/Z; Y è 0 nella fixture. I nodi mantengono quaternion `(0,-0.70710677,0,0.70710677)` dopo l'editor. Il rettifilo lungo +X è visibile, ma la convenzione geografica completa resta a PoC-002. |
| 9. Road look/definition | Tutti i token usati sono stati osservati direttamente negli archivi base ETS2 1.60.1.7; non sono stati dedotti dal solo sample. |
| 10. Prefab/asset | `Road.Add` non richiede prefab per il rettifilo. Il modello stradale base referenziato esiste; nessun asset è stato copiato. |
| 11. File aggiuntivi | `.layer`, `.set` ed `.expa` non servono al bootstrap minimale; sono creati dall'editor insieme a backup e autosave. Nessun file è stato aggiunto artificialmente al generatore. |
| 12. Capacità TruckLib 0.5.1 | Scrittura e rilettura riuscite su .NET 10/macOS ARM64; la rilettura riesce anche dopo il salvataggio ETS2 e preserva proprietà, UID e riferimenti. |
| 13. Compatibilità ETS2 1.60.x | Verificata sperimentalmente con Map Editor Windows x64 ETS2 1.60.1.7, formato 907, per la singola Road/singolo settore testati. |

## Discrepanze e problemi incontrati

La documentazione SCS mostra cinque file di settore comprendenti `.layer` e
tratta `.snd` come aggiuntivo quando sono presenti suoni; mostra inoltre `.epa`
e `.set`. Il writer TruckLib 0.5.1 crea invece un `.snd` vuoto e omette `.layer`
quando nessun item usa un layer non predefinito. Mostra anche una collocazione
`user_map/map` diversa dalla struttura riportata nella pagina SCS più vecchia.
Il primo controllo automatico, che presumeva `.layer`, è stato corretto dopo
aver verificato `WriteLayer` nel sorgente esatto. Nessun workaround o file
fittizio è stato introdotto.

La discrepanza è ora risolta empiricamente: il set TruckLib senza `.layer` viene
aperto direttamente. Il lifecycle editor crea `.layer`, `.set` ed `.expa`; il
nome realmente osservato è `.expa`, non `.epa`. Il generatore minimale non deve
anticipare questi artefatti.

I log `editor.log.txt` riportano warning/errori ambientali per `manifest.sii`,
`background_map_legends.sii`, il file climate della mappa e, in una sessione,
un identificatore Sign non trovato. Nessuno impedisce caricamento, recompute,
salvataggio o riapertura. Il readback contiene sempre un solo map item Road,
quindi nessun Sign è persistito nell'output del PoC.

Smart App Control/`VerifiedAndReputableDesktop` ha bloccato su Windows
l'assembly locale non firmato. La protezione non è stata disabilitata:
generazione e readback sono stati eseguiti su macOS, il ciclo Map Editor target
su Windows. È un rischio separato di firma/distribuzione da portare alla
verifica ambiente/packaging; non modifica l'esito del formato nativo.

## Assunzioni

Confermate dall'intero ciclo:

- TruckLib 0.5.1 espone davvero le API necessarie per creare, salvare e riaprire
  una Map con una Road;
- il pacchetto scelto è compilabile con .NET 10;
- il writer produce `.mbd` e settori formato 907;
- UID e riferimenti vengono creati e serializzati senza assegnazioni manuali;
- gli asset stradali scelti sono nel catalogo base locale 1.60.1.7;
- un rettifilo non richiede prefab;
- il set TruckLib minimale è accettato direttamente dal Map Editor 1.60.1.7;
- Road, nodi, UID e riferimenti persistono dopo recompute, save e riapertura;
- `.layer`, `.set` ed `.expa` sono creati dal lifecycle editor e non sono
  prerequisiti del bootstrap testato.

Invalidate o corrette:

- l'assunzione che una mappa sul layer predefinito produca sempre `.layer`.
  TruckLib 0.5.1 lo omette deliberatamente e l'editor accetta l'output;
- l'estensione osservata creata dall'editor è `.expa`, non `.epa`.

Ancora aperte:

- trasformazione WGS84 → AEQD → coordinate ETS2 e scala 1:19;
- convenzione geografica completa degli assi;
- curve, catene e continuità fra segmenti;
- intersezioni T/quattro vie e prefab;
- conversione OSM e confine Python → JSON → C#;
- comportamento multi-sector e limiti operativi;
- esecuzione/distribuzione dell'adapter non firmato sotto Smart App Control.

## Criteri di successo

| Criterio | Esito | Evidenza |
| --- | --- | --- |
| 1. Output generato programmaticamente | `PASSED` | due output e manifest |
| 2. Map Editor 1.60.x apre senza errori bloccanti | `PASSED` | log Windows di entrambi i run |
| 3. Strada visibile | `PASSED` | osservazione manuale + UID trovato nel log |
| 4. Map Editor salva | `PASSED` | `Map successfuly saved` + set post-editor |
| 5. Chiusura e riapertura riuscite | `PASSED` | log separati di riapertura |
| 6. Struttura valida dopo il salvataggio | `PASSED` | due readback TruckLib con UID stabili |

Tutti i criteri obbligatori sono superati in entrambe le ripetizioni. Lo stato
complessivo è `PASSED`.

## Conseguenze architetturali e prossima azione

Per lo scope verificato è confermata la fattibilità del percorso
`ETS2-independent model → C# adapter → TruckLib → native ETS2 map`. TruckLib
0.5.1 e il formato 907 possono essere mantenuti come baseline candidata del
profilo `ets2-1.60-native-v1` per la Road rettilinea minimale.

PoC-001 non blocca più il gate successivo, ma non autorizza a trasferire il
risultato a geometrie, topologie o pipeline non provate. PoC-002 non è stato
iniziato in questa attività. Prima di avviarlo, il suo input deve restare
limitato alle fixture e alle trasformazioni definite dal piano; il problema di
firma/Smart App Control va registrato nella futura verifica ambiente senza
disabilitare la protezione come workaround implicito.
