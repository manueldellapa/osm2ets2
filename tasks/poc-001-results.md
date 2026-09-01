# PoC-001 — ETS2 Native Output Feasibility: risultati

**Stato: `AWAITING_MANUAL_VALIDATION`**

**Data fase automatica: 1 settembre 2026**

La generazione programmata e la rilettura con TruckLib 0.5.1 sono riuscite in
due directory indipendenti. Non è disponibile in questo ambiente il Map Editor
ETS2 1.60.x su Windows 11 x64; apertura, visibilità, recompute, salvataggio e
riapertura non sono stati simulati. Il PoC non è quindi `PASSED` e PoC-002 non è
stato iniziato.

## Perimetro e baseline

Sono stati implementati esclusivamente una mappa vuota, due nodi e una strada
rettilinea da `A=(100,0,100)` a `B=(200,0,100)`. Non sono presenti OSM, Python,
proiezioni, grafo stradale, intersezioni, prefab, CLI dell'MVP o pipeline
end-to-end.

Gli input canonici sono stati conservati senza modifiche:

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
installazione non sostituisce il collaudo Windows. Impronte, versioni e fonti
sono registrate in
[`baseline-and-source-verification.md`](../spikes/poc-001-ets2-native-output/evidence/baseline-and-source-verification.md).

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

## Risposte agli aspetti tecnici richiesti

| Aspetto | Risultato osservato |
| --- | --- |
| 1. Struttura minima candidata | `map/poc001_minimal.mbd` e cartella sorella `map/poc001_minimal/` con un settore. È il set prodotto, non ancora il set sufficiente dimostrato dall'editor. |
| 2. Ruolo e contenuto `.mbd` | TruckLib scrive header 907 e metadati globali: map UID, start position/rotation, un campo settore di significato ignoto upstream, game tag, scale 19/3 e correzione UI Europe. Road e nodi non sono nel `.mbd`. |
| 3. Sector files | `.base` contiene Road e nodi; `.data` il payload Road; `.aux` e `.snd` sono settori vuoti serializzati; `.desc` contiene metadati del settore. `.layer` non viene scritto con il layer predefinito. |
| 4. Identificatori/UID | Map, Road e due nodi hanno UID a 64 bit non zero e distinti generati da TruckLib. I due run producono UID diversi. Il PoC non inventa né forza valori. |
| 5. Nodi | Due nodi esterni: backward/red e forward/green. Il primo punta alla Road in avanti, il secondo all'indietro; i lati terminali sono null. Posizione serializzata fixed-point con fattore 256. |
| 6. Strada | Un `Road` nativo TruckLib, tipo `ger1`, lunghezza riletta 100, zero terreno laterale, lato destro configurato con look `ger_1`, variante `broken_de` ed edge `ger_sh_15`. |
| 7. Coordinate | Input `Vector3` esatti A/B; delta di 100 lungo X. TruckLib descrive le unità motore come metri e il readback mantiene i valori esatti. Il significato nel target editor resta da osservare. |
| 8. Assi e orientamento | La settorizzazione TruckLib usa X/Z; Y è 0 nella fixture. Entrambi i nodi sono riletti con quaternion `(0,-0.70710677,0,0.70710677)`. Corrispondenza visiva, verso e asse verticale nell'editor non sono ancora convalidati. |
| 9. Road look/definition | Tutti i token usati sono stati osservati direttamente negli archivi base ETS2 1.60.1.7; non sono stati dedotti dal solo sample. |
| 10. Prefab/asset | `Road.Add` non richiede prefab per il rettifilo. Il modello stradale base referenziato esiste; nessun asset è stato copiato. |
| 11. File aggiuntivi | TruckLib non ha prodotto `.layer`, `.epa` o `.set`. La necessità o creazione di questi file da parte dell'editor è irrisolta. Nessun file è stato aggiunto artificialmente. |
| 12. Capacità TruckLib 0.5.1 | Scrittura e rilettura della struttura candidata riuscite su .NET 10/macOS ARM64. Proprietà e riferimenti sopravvivono al round trip TruckLib. |
| 13. Compatibilità ETS2 1.60.x | TruckLib dichiara formato 907 per 1.59–1.60 e gli asset esistono nel catalogo locale 1.60.1.7. La compatibilità effettiva col Map Editor Windows resta non dimostrata. |

## Discrepanze e problemi incontrati

La documentazione SCS mostra cinque file di settore comprendenti `.layer` e
tratta `.snd` come aggiuntivo quando sono presenti suoni; mostra inoltre `.epa`
e `.set`. Il writer TruckLib 0.5.1 crea invece un `.snd` vuoto e omette `.layer`
quando nessun item usa un layer non predefinito. Mostra anche una collocazione
`user_map/map` diversa dalla struttura riportata nella pagina SCS più vecchia.
Il primo controllo automatico, che presumeva `.layer`, è stato corretto dopo
aver verificato `WriteLayer` nel sorgente esatto. Nessun workaround o file
fittizio è stato introdotto.

## Assunzioni

Confermate automaticamente:

- TruckLib 0.5.1 espone davvero le API necessarie per creare, salvare e riaprire
  una Map con una Road;
- il pacchetto scelto è compilabile con .NET 10;
- il writer produce `.mbd` e settori formato 907;
- UID e riferimenti vengono creati e serializzati senza assegnazioni manuali;
- gli asset stradali scelti sono nel catalogo base locale 1.60.1.7;
- un rettifilo non richiede prefab.

Invalidata:

- l'assunzione che una mappa sul layer predefinito produca sempre `.layer`.
  TruckLib 0.5.1 lo omette deliberatamente.

Ancora aperte:

- sufficienza del set di file e della collocazione;
- accettazione di formato e metadati da parte del Map Editor 1.60.x;
- visibilità, editabilità, assi/orientamento e significato delle unità nel target;
- comportamento di recompute e salvataggio;
- persistenza dopo chiusura e riapertura;
- eventuale creazione di `.layer`, `.epa`, `.set` o altri file da parte
  dell'editor.

## Criteri di successo

| Criterio | Esito | Evidenza |
| --- | --- | --- |
| 1. Output generato programmaticamente | `PASSED` | due output e manifest |
| 2. Map Editor 1.60.x apre senza errori bloccanti | `NOT_EXECUTED` | richiede Windows 11 x64 |
| 3. Strada visibile | `NOT_EXECUTED` | richiede Map Editor |
| 4. Map Editor salva | `NOT_EXECUTED` | richiede Map Editor |
| 5. Chiusura e riapertura riuscite | `NOT_EXECUTED` | richiede Map Editor |
| 6. Struttura valida dopo il salvataggio | `NOT_EXECUTED` | ciclo editor + confronto richiesti |

La sola prima riga non soddisfa il gate. Lo stato complessivo resta quindi
`AWAITING_MANUAL_VALIDATION`.

## Conseguenze architetturali e prossima azione

Non è ancora autorizzato considerare TruckLib 0.5.1 compatibile con il profilo
`ets2-1.60-native-v1`, né avviare PoC-002. Il percorso `.mbd` + settori rimane
una candidata tecnicamente scrivibile, con rischio concentrato
nell'accettazione e persistenza nel Map Editor.

La prossima azione raccomandata è eseguire, senza modifiche manuali alla mappa,
entrambi i cicli descritti in
[`manual-validation/checklist.md`](../spikes/poc-001-ets2-native-output/manual-validation/checklist.md)
su Windows 11 x64 con una build ETS2 1.60.x registrata. Se il set viene rifiutato
o perde la strada al salvataggio, fermare lo spike con `FAILED`, conservare log
e output e valutare prima una correzione circoscritta dell'adapter/TruckLib;
writer o formato alternativi richiedono una revisione esplicita del PRD.
