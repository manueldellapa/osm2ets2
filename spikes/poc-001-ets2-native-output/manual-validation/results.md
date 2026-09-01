# PoC-001 — Risultati della validazione manuale

**Stato finale: `PASSED`**

Validazione completata il **1 settembre 2026** su entrambi gli output originali
`run-01` e `run-02`. Il ciclo è stato eseguito senza creare o riparare
manualmente la Road e senza aggiungere file al set TruckLib prima della prima
apertura.

## Baseline effettiva

| Componente | Valore osservato |
| --- | --- |
| Sistema editor | Windows 11 x64, build OS `10.0.26200` |
| CPU registrata | Intel Core i7-8700, 6 core/12 thread |
| ETS2 | pack `1.60.1.7`, `1.60.1.7s`, revisione `26c95e307fd5` |
| Editor | Map Editor integrato `bin\win_x64\eurotrucks2.exe` |
| Avvio | `-edit poc001_minimal -noworkshop` |
| Formato mappa | 907 |
| Writer/readback | TruckLib 0.5.1, .NET 10 |
| Generazione e readback | macOS 26.6.2 ARM64, .NET SDK 10.0.400/runtime 10.0.11 |

I log Windows confermano sistema, comando, versione del pack, caricamento della
mappa e dei settori. Il file prodotto dal Map Editor è `editor.log.txt`; le
copie conservate hanno nomi descrittivi sotto le directory dei due run.

In entrambe le sessioni il log riporta un solo mod locale attivo (`user_map`) e
zero mod Workshop. L'installazione monta anche i pacchetti DLC ufficiali
presenti. Gli identificatori usati dalla Road sono stati verificati
separatamente in `base.scs` e `def.scs`, quindi il PoC non introduce una
dipendenza obbligatoria da DLC; il ciclo editor non costituisce però una prova
su un'installazione da cui i DLC siano stati fisicamente rimossi.

## Esito per criterio

| Criterio | `run-01` | `run-02` | Evidenza |
| --- | --- | --- | --- |
| Apertura diretta senza errore bloccante | `PASSED` | `PASSED` | log di apertura/salvataggio |
| Settori caricati | `PASSED` | `PASSED` | `Map base sectors successfully loaded` e `Map sectors loading finished` |
| Road visibile, nativa e selezionabile | `PASSED` | `PASSED` | osservazione manuale + ricerca UID nel log |
| UID Road riconosciuto dall'editor | `PASSED` | `PASSED` | `Item Road with UID ... found` |
| `Map > Recompute map` | `PASSED` | `PASSED` | `Rebuilding whole map` e `Rebuild done` |
| Salvataggio | `PASSED` | `PASSED` | `Map successfuly saved` |
| Chiusura completa e riapertura | `PASSED` | `PASSED` | log separato di riapertura |
| Road/nodi/riferimenti validi dopo save | `PASSED` | `PASSED` | readback TruckLib finale |
| Nessuna riparazione manuale | `PASSED` | `PASSED` | verbale operatore |

Per `run-01` sono conservati il
[log della prima apertura](run-01/run-01-open-editor.log.txt), il
[log dopo recompute](run-01/run-01-after-recompute.log.txt), il
[log del salvataggio](run-01/run-01-after-manual-save.log.txt), il
[log di riapertura](run-01/run-01-reopen-editor.log.txt) e il
[readback finale](run-01/editor-save-readback.txt). La sessione
`accidental-close` è diagnostica e non è usata come ciclo di accettazione.
La directory `after-editor/` è la copia canonica post-editor; `editor-saved/` è
una seconda copia byte per byte identica. `accidental-close-user_map/` resta
separata e non contribuisce all'esito.

Per `run-02` sono conservati il
[log di apertura/recompute/salvataggio](run-02/run-02-after-save.log.txt), il
[log di riapertura](run-02/run-02-reopen-editor.log.txt) e il
[readback finale](run-02/editor-save-readback.txt).

La visibilità e la selezione sono state confermate dall'operatore; nel
repository non sono presenti screenshot. I log registrano comunque la ricerca
riuscita dell'esatto Road UID dopo l'apertura e dopo la riapertura.

## Stabilità semantica e UID

Entrambi i readback finali restituiscono
`EDITOR_SAVE_TRUCKLIB_READBACK_PASSED`, `Map items: 1`, `Nodes: 2` e
`UID stability: STABLE`.

| Oggetto | `run-01` prima → dopo | `run-02` prima → dopo |
| --- | --- | --- |
| Map | `0x42CBE855EFF3A4A6` → invariato | `0x455BDFB687F05036` → invariato |
| Road | `0x4009C748E369FF6F` → invariato | `0x4F8C798E2AAFFB0B` → invariato |
| Nodo backward | `0x4837EA1366D62307` → invariato | `0x46BAE13AB352A7A1` → invariato |
| Nodo forward | `0x4AD607379629B1FF` → invariato | `0x4AAB06802DD6B9F0` → invariato |

Il readback è stato ripetuto localmente sui due set `after-editor` durante la
consolidazione del verbale, con lo stesso esito.

## Lifecycle dei file

Il bootstrap accettato direttamente dal Map Editor contiene soltanto:

- `poc001_minimal.mbd`;
- `sec+0000+0000.base`;
- `sec+0000+0000.data`;
- `sec+0000+0000.desc`;
- `sec+0000+0000.aux`;
- `sec+0000+0000.snd`.

Dopo il lifecycle editor, in entrambi i run:

- `.mbd`, `.aux`, `.data`, `.desc` e `.snd` sono byte per byte invariati;
- il file `.base` mantiene 409 byte ma viene riscritto;
- viene creato `sec+0000+0000.layer` da 24 byte;
- vengono creati `poc001_minimal.set` e `poc001_minimal.expa`, entrambi da
  16 byte;
- viene creata `poc001_minimal.bak/` con la precedente `.base`;
- viene creata `autosave/` con una copia della mappa e del settore.

| Run | SHA-256 `.base` iniziale | SHA-256 `.base` dopo editor |
| --- | --- | --- |
| `run-01` | `b4497dd366ee545094c40d37cfd26500161f46a63cb593c28749d0a41e1d3992` | `a2f798efc81de18a40c27a62f8b63e3bda90ed963a21938e854842bc8fa042f9` |
| `run-02` | `e709b5147fa801f6811e24425ecba6e4433c8c6f076fa58eff76bcca7e1a455e` | `74a6b21a7275a631f4480bc2438dca3f97718c04b6c1d5121b93332ebb9124b9` |

Non è stato rimosso alcun file iniziale. Gli inventari completi post-editor sono
in [run-01/after-editor-files.sha256](run-01/after-editor-files.sha256) e
[run-02/after-editor-files.sha256](run-02/after-editor-files.sha256); gli
inventari Windows pre-editor sono in [run-01/before-files.csv](run-01/before-files.csv)
e [run-02/before-files.csv](run-02/before-files.csv).

Questa prova chiarisce la discrepanza iniziale: `.layer`, `.set` ed `.expa` non
sono necessari per aprire il bootstrap TruckLib minimale. Sono artefatti creati
dal normale lifecycle del Map Editor e non devono essere sintetizzati dal
generatore per questo scope.

## Warning ed errori ambientali

I log riportano, fra gli altri:

- `user_map` senza `manifest.sii`, con creazione di informazioni predefinite;
- assenza di `/def/background_map_legends.sii`;
- assenza di `poc001_minimal.climate.sii` durante la distribuzione del clima;
- in una sessione di `run-01`, warning `Unable to find 'sign' 'poc_5g0ak'`.

Il readback finale contiene un solo map item, la Road attesa, quindi nessun
elemento Sign è stato persistito nella mappa del PoC. Questi messaggi sono stati
classificati come ambientali/non bloccanti perché in entrambe le ripetizioni la
mappa viene caricata, ricomputata, salvata, riaperta e riletta senza perdita di
geometria o riferimenti.

## Vincolo Smart App Control

L'avvio locale dell'assembly non firmato su Windows è stato bloccato da Smart
App Control/`VerifiedAndReputableDesktop`. La protezione non è stata
disabilitata. La generazione e il readback sono quindi stati eseguiti su macOS,
mentre il ciclo Map Editor target è stato eseguito su Windows 11 x64.

Il gate di formato è superato: l'output programmatico è accettato e reso
persistente dall'editor target. Rimane un rischio separato di distribuzione e
firma dell'eseguibile Windows da trattare nella verifica ambiente/packaging; il
PoC non dimostra ancora l'esecuzione dell'adapter non firmato sotto quella
policy.

## Decisione

PoC-001 è `PASSED` per la baseline e lo scope minimale registrati. Il percorso
`C#/.NET 10 → TruckLib 0.5.1 → .mbd + settori → ETS2 1.60.1.7 Map Editor →
Recompute → Save → Close → Reopen → TruckLib readback` è stato completato due
volte con Road, nodi, UID e riferimenti preservati.

Il gate non estende il risultato a coordinate geografiche, scala 1:19, curve,
catene, intersezioni, prefab, OSM, multi-sector, limiti operativi o confine
Python/JSON/C#. PoC-002 non è stato iniziato.

## Redazione delle informazioni sensibili nelle evidenze

Le evidenze testuali incluse nel repository sono state sottoposte a redazione per motivi di privacy esclusivamente per quanto riguarda i percorsi del filesystem locale:

- la root locale del repository è rappresentata come `<REPO_ROOT>`;
- il profilo utente Windows è rappresentato come `C:/Users/<user>` oppure `C:\Users\<user>`.

I risultati della validazione, gli UID, i timestamp, gli hash, i warning, gli errori e le altre evidenze tecniche sono invece preservati senza modifiche.
