# PRD: osm2ets2 — MVP di conversione della rete stradale OSM per ETS2

Data iniziale: 31 agosto 2026

Revisione DT-07: 2 settembre 2026

Esecuzione automatica del rerun PoC-002 revisionato: 3 settembre 2026

Stato: requisiti MVP e decisioni tecniche adottate; PoC-001 `PASSED`;
PoC-002 v1 `FAIL` sotto i criteri originali congelati; rerun PoC-002 con
criteri revisionati `AWAITING_MANUAL_VALIDATION` dopo il `PASS` automatico;
PoC-003 e PoC-004 `NOT_EXECUTED` e bloccati. Nessuna funzionalità MVP di
produzione è implementata.

Ambito: dalla selezione di una piccola area OSM a una base stradale modificabile nel Map Editor di Euro Truck Simulator 2

## 1. Introduzione e contesto

`osm2ets2` deve consentire a un autore di mappe di partire dai dati OpenStreetMap di una piccola area reale e ottenere automaticamente posizione, geometria e connessioni della rete stradale supportata, utilizzabili come base di lavoro per una mod di Euro Truck Simulator 2 (ETS2).

Il problema da risolvere è la ricostruzione manuale delle strade da zero. Il risultato è un progetto da aprire, verificare e rifinire con gli strumenti ETS2, non una mappa completa pronta per la pubblicazione.

L'utente primario è un modder che sa utilizzare una CLI e dispone dell'ambiente necessario al map editing di ETS2. Il secondo destinatario è lo sviluppatore che deve poter estendere la conversione senza riscrivere l'acquisizione OSM o il modello stradale.

### 1.1 Evidenze del repository

Alla verifica del 31 agosto 2026, il commit `8ddfea6` contiene i tre file iniziali sotto elencati; il presente PRD è l'unico documento aggiunto nel workspace durante la progettazione:

| Percorso verificato | Contenuto e conseguenza per il PRD |
| --- | --- |
| `README.md` | Descrive un generatore open-source OSM → ETS2; non specifica architettura o comportamento. |
| `LICENSE` | Contiene il testo GNU GPL versione 2. Non determina la licenza dei dati OSM o degli asset del gioco. |
| `.gitignore` | Contiene regole prevalentemente Python. Suggerisce un possibile orientamento, ma non conferma uno stack adottato. |

Alla stessa verifica non erano presenti `AGENTS.md` applicabili, codice
applicativo, manifest di dipendenze, test, configurazioni CI o ADR. Oltre al
presente documento non esistevano altri PRD. Non esistevano quindi API, moduli
o comandi di qualità da riutilizzare o da dichiarare già disponibili.

Tutto il comportamento descritto di seguito è da costruire. Il perimetro funzionale deriva dalla richiesta dell'utente; le scelte tecniche DT-01–DT-08 (§7) sono ora adottate a seguito del confronto delle alternative richiesto. I dettagli marcati come assunzioni o PoC non sono fatti verificati. La compatibilità ETS2 richiede G0 (§7.2).

**Aggiornamento del 2 settembre 2026:** le righe precedenti conservano la
fotografia iniziale del repository. Da allora sono stati aggiunti esclusivamente
spike sperimentali: PoC-001 ha superato il controllo nativo minimo e PoC-002 v1
ha prodotto `FAIL`, causando la revisione di precisione DT-07 in §7.9. Questo
non equivale a un'implementazione del prodotto né al superamento di G0.

**Aggiornamento del 3 settembre 2026:** il rerun PoC-002 revisionato ha
superato tutti i criteri automatici congelati ed è
`AWAITING_MANUAL_VALIDATION`. Il ciclo Windows Map Editor, la persistenza Q256
post-editor e la semantica geografica visuale degli assi restano non eseguiti;
il gate PoC-002 e G0 non sono quindi superati.

### 1.2 Baseline adottata per delimitare il MVP

| Tema | Baseline adottata |
| --- | --- |
| Interfaccia | CLI locale, senza interfaccia grafica o servizio web. |
| Sorgenti | Snapshot `.osm` e `.osm.pbf`, oppure download di un'area tramite bounding box. Con `--input`, un'eventuale `--bbox` ritaglia il file locale senza attivare download. |
| Destinazione | Base di mappa autonoma, in coordinate locali; nessun allineamento automatico alla mappa `europe` o a mod esistenti. |
| Scala | Fattore geometrico uniforme configurabile; valore predefinito `1`, cioè scala geometrica 1:1. |
| Altimetria | Base planare per le strade ordinarie. Ponti, tunnel e separazioni verticali sono riconosciuti e segnalati, non ricostruiti automaticamente. |
| Intersezioni | Grafo OSM corretto; nell'output nativo, raccordi semplici a T e a quattro bracci entro un profilo verificato. È ammesso il riuso minimo di prefab già presenti nel gioco. Non è prevista la generazione di nuovi prefab. |
| Output | Mappa nativa `.mbd` con settori tramite TruckLib 0.5.1, modello intermedio neutro e report; il collaudo Map Editor completo del prodotto resta obbligatorio e non eseguito. Gli esiti parziali degli spike sono riepilogati in §9. |
| Compatibilità | ETS2 1.60.x stabile su Windows 11 x64 per il collaudo editor; core/CLI/adapter da verificare anche su Ubuntu 24.04 x64 e macOS 14+ ARM64. |

Il riuso di raccordi semplici serve a rendere verificabile il requisito di rete connessa: non equivale a un generatore generale di junction, rotatorie o svincoli. Se questa capacità minima non è tecnicamente dimostrabile, il MVP non soddisfa il suo criterio principale; un grafo JSON o un insieme di strade sovrapposte non può sostituirla.

### 1.3 Significato di «utilizzabile» e «connessa»

- **Rete normalizzata:** un grafo di nodi e tratti stradali con geometrie, direzioni e riferimenti OSM coerenti.
- **Base ETS2 utilizzabile:** elementi stradali nativi selezionabili e modificabili nel Map Editor, persistenti dopo salvataggio e riapertura.
- **Connessione nel grafo:** adiacenza esplicita derivata dai dati OSM, non dalla sola vicinanza geometrica.
- **Connessione nell'editor:** collegamento effettivo fra gli elementi e i loro connettori; due estremità coincidenti visivamente non costituiscono una prova.
- **Caso non supportato:** elemento o raccordo riconosciuto ma non rappresentabile nel profilo MVP; deve restare rintracciabile e non essere contato come convertito con successo.

La rete sorgente può contenere componenti separate reali. Il tool deve conservarle, senza aggiungere collegamenti artificiali per rendere tutta l'area un'unica componente. Non viene promessa la percorribilità completa in gioco, la correttezza del navigatore o una simulazione del traffico.

## 2. Obiettivi

1. Eseguire da CLI il percorso completo da una sorgente OSM limitata a un output stradale apribile nel workflow ETS2 documentato.
2. Conservare forma riconoscibile, orientamento, proporzioni e connessioni della porzione di rete dichiarata supportata.
3. Supportare sia file locali riproducibili e utilizzabili offline sia acquisizione tramite bounding box.
4. Rendere espliciti scala, origine, sistema di coordinate, profilo stradale e compatibilità dell'output.
5. Fornire un rendiconto verificabile di ciò che è stato convertito, escluso o lasciato alla rifinitura.
6. Evitare modifiche ai dati sorgente e ai progetti ETS2 già esistenti dell'utente.
7. Separare acquisizione, modello geografico, geometria ed esportazione per consentire estensioni future senza implementarle nel MVP.

## 3. User stories

I criteri seguenti sono condizioni future di accettazione. Per ogni storia sono
richiesti i test pertinenti e i controlli di qualità che verranno configurati
nel prodotto secondo §7.7; i comandi presenti negli spike isolati non
costituiscono ancora la suite del prodotto.

### US-001: Avviare una conversione con parametri comprensibili

**Descrizione:** Come autore di mappe, voglio indicare sorgente, destinazione e scala da CLI per eseguire una conversione senza modificare codice.

**Criteri di accettazione:**

- [ ] Esiste il comando `osm2ets2 build` con `--input` oppure `--bbox`, e con `--output` obbligatorio.
- [ ] Senza `--input`, `--bbox` seleziona la sorgente online; con `--input`, un'eventuale `--bbox` è soltanto un filtro geografico locale. L'assenza di entrambi è un errore.
- [ ] `--bbox` accetta quattro numeri finiti nell'ordine `west,south,east,north`, verifica intervalli geografici e richiede `west < east`, `south < north`.
- [ ] Aree che attraversano l'antimeridiano, domini non coperti dalla proiezione o limiti del profilo superati producono un errore esplicativo.
- [ ] `--scale` accetta un fattore finito maggiore di zero e ha valore predefinito `1`.
- [ ] L'aiuto spiega formati, ordine delle coordinate, significato della scala, prerequisiti e stato ancora non completo della mappa generata.
- [ ] Parametri invalidi non avviano download, non modificano progetti preesistenti e restituiscono un codice di errore documentato.

### US-002: Convertire uno snapshot OSM locale

**Descrizione:** Come autore di mappe, voglio usare un file OSM locale per lavorare offline e ripetere una conversione sulla stessa sorgente.

**Criteri di accettazione:**

- [ ] Sono accettati snapshot OSM XML `.osm` e PBF `.osm.pbf` supportati dal parser scelto.
- [ ] Il parsing conserva l'identità distinta di nodi, way e relazioni lette; gli ID non vengono convertiti in rappresentazioni numeriche che ne perdono precisione.
- [ ] L'ordine dei nodi di ogni way e i riferimenti ai dati sorgente sono mantenuti, indipendentemente dall'ordine degli elementi nel file.
- [ ] File mancanti, corrotti, troncati, history/diff o con caratteristiche obbligatorie non supportate falliscono con una diagnosi; non sono interpretati come rete vuota.
- [ ] Una way candidata con un nodo referenziato mancante impedisce una build riuscita; non vengono inventate coordinate né collegati direttamente i nodi adiacenti al riferimento mancante.
- [ ] XML e PBF contenenti lo stesso snapshot producono lo stesso grafo normalizzato, al netto dei metadati relativi al contenitore sorgente.
- [ ] La modalità locale non effettua richieste di rete; i prerequisiti dell'esportatore devono essere già disponibili localmente.
- [ ] Riutilizzare lo snapshot acquisito con `--input` e la stessa `--bbox`, scala e profilo riproduce origine, trasformazione, grafo e contenuto nativo della build online, salvo metadati della modalità di acquisizione.

### US-003: Acquisire una piccola area tramite bounding box

**Descrizione:** Come autore di mappe, voglio fornire un rettangolo geografico per ottenere i dati necessari senza preparare manualmente un estratto.

**Criteri di accettazione:**

- [ ] La modalità bbox senza `--input` usa Overpass con endpoint esplicitamente configurato e budget di DT-08; in assenza di endpoint restituisce `invalid_input` senza richieste di rete.
- [ ] Vengono acquisiti i dati necessari alle way selezionate e tutti i nodi referenziati prima di costruire il grafo.
- [ ] Il percorso online supera la prova di acquisizione di una way che attraversa la bbox senza nodi interni. Una strategia che ne consente l'omissione non può essere dichiarata supportata come acquisizione completa dell'area.
- [ ] Quando il servizio usa un ordine bbox differente, la conversione dall'ordine CLI è coperta da un caso di prova asimmetrico.
- [ ] Timeout, indisponibilità, limitazione delle richieste e risposta incompleta sono distinti da un'area valida priva di strade supportate.
- [ ] Le richieste hanno timeout e tentativi limitati, rispettano le indicazioni del servizio e non aggirano un rifiuto cambiando automaticamente server.
- [ ] Lo snapshot acquisito viene conservato con provenienza e hash per consentirne il riutilizzo locale.
- [ ] La rete finale viene ritagliata al rettangolo secondo FR-13; tratti esterni necessari a leggere una way non ampliano silenziosamente l'area generata.

### US-004: Selezionare la rete stradale pertinente

**Descrizione:** Come autore di mappe, voglio sapere quali categorie OSM vengono incluse per ottenere una base stradale prevedibile.

**Criteri di accettazione:**

- [ ] La selezione predefinita applica esattamente la tabella delle classi in §4.2.
- [ ] Classi escluse, valori sconosciuti e geometrie stradali esplicitamente marcate come area sono classificati e contati con motivazione.
- [ ] La classe `unclassified` viene trattata come classe supportata; non viene confusa con una categoria sconosciuta.
- [ ] Tag utili al mapping, inclusi categoria, direzione, corsie quando interpretabili e informazioni verticali, restano disponibili alle fasi successive.
- [ ] Restrizioni di accesso, limiti per veicoli e restrizioni di svolta non vengono presentati come applicati al traffico; il report esplicita questo limite anche quando i relativi dati non sono disponibili nello snapshot.
- [ ] L'esclusione di una strada non genera collegamenti artificiali tra le strade rimanenti.

### US-005: Conservare la topologia OSM

**Descrizione:** Come autore di mappe, voglio che tratti e intersezioni mantengano le relazioni della sorgente per non dover correggere connessioni introdotte dal convertitore.

**Criteri di accettazione:**

- [ ] Le way sono suddivise in tratti nei nodi di intersezione condivisi, mantenendo i punti intermedi che descrivono la forma.
- [ ] Due linee che si incrociano senza nodo OSM condiviso non vengono unite, anche se le coordinate coincidono dopo trasformazione o arrotondamento.
- [ ] Sono conservati vicoli ciechi, anelli stradali validi e componenti disconnesse presenti nella sorgente.
- [ ] Sono riconosciuti `oneway=yes`, `oneway=no` e `oneway=-1`, inclusi gli alias booleani documentati dal profilo; l'ordine sorgente resta ricostruibile.
- [ ] Le implicazioni per `highway=motorway` e `junction=roundabout` rispettano l'eventuale valore esplicito `oneway=no`; non si assume che ogni strada `_link` sia a senso unico.
- [ ] I terminali sintetici creati dal ritaglio sono identificati come bordi area e hanno riferimenti stabili alla geometria sorgente.
- [ ] Non esistono riferimenti a nodi inesistenti o tratti di lunghezza nulla nel grafo validato; eliminazioni o anomalie sono rendicontate.

### US-006: Applicare coordinate locali e scala

**Descrizione:** Come autore di mappe, voglio una rete orientata e scalata in modo documentato per riconoscere l'area reale e rifinirla nell'editor.

**Criteri di accettazione:**

- [ ] La trasformazione passa da coordinate geografiche WGS84 a coordinate metriche locali e poi alla convenzione del profilo ETS2.
- [ ] Origine, proiezione, unità, orientamento degli assi, trasformazione e scala sono registrati nei metadati.
- [ ] L'origine predefinita è deterministica: centro della bbox ogni volta che questa è presente, anche con un file locale; solo in sua assenza si usa il centro dell'estensione delle geometrie candidate prima delle esclusioni dell'esportatore.
- [ ] Con `--scale 1`, un metro della geometria locale corrisponde a un metro della scena; con `--scale 0.1`, dieci metri corrispondono a uno, entro la tolleranza numerica dichiarata.
- [ ] Punti di controllo indipendenti verificano orientamento est/nord, distanze e assenza di riflessioni o scambi di assi.
- [ ] La conversione non salda nodi distinti dopo scaling, non altera l'adiacenza del grafo e non sposta silenziosamente le intersezioni per adattarle agli asset.
- [ ] Una scala che viola vincoli geometrici del profilo produce elementi non supportati o un errore, secondo §6.3, anziché geometrie dichiarate valide.

### US-007: Associare categorie OSM a strade ETS2

**Descrizione:** Come autore di mappe, voglio un mapping iniziale esplicito per ottenere elementi stradali coerenti con le categorie importate.

**Criteri di accettazione:**

- [ ] Un profilo di mapping versionato associa ciascuna categoria selezionabile a una regola di esportazione o a una limitazione esplicita.
- [ ] Il profilo iniziale distingue almeno strade ordinarie bidirezionali, strade a senso unico e collegamenti principali; i riferimenti agli asset sono verificati sull'ambiente target.
- [ ] Una carreggiata OSM a senso unico non diventa automaticamente una strada bidirezionale e due carreggiate separate non vengono duplicate come autostrade complete.
- [ ] Valori `lanes` assenti usano un default dichiarato; valori non interpretabili o non rappresentabili sono segnalati, senza promettere una ricostruzione esatta delle corsie.
- [ ] Tutti gli identificatori di strada o raccordo utilizzati sono risolvibili con le dipendenze dichiarate dal profilo.
- [ ] Un mapping non valido o una dipendenza obbligatoria mancante impediscono l'esportazione; non si usa un asset arbitrario come sostituto silenzioso.
- [ ] L'utente può selezionare un profilo o fornire un mapping locale nello stesso formato documentato; il contenuto effettivo viene identificato nei metadati di build.

### US-008: Generare strade native modificabili

**Descrizione:** Come autore di mappe, voglio aprire la geometria generata come elementi ETS2 reali per proseguire il lavoro senza ricalcare le strade.

**Criteri di accettazione:**

- [ ] L'esportatore produce il progetto `.mbd` e i settori scelti in DT-02, completo degli artefatti necessari al workflow verificato in G0.
- [ ] Una catena stradale con un tratto curvo viene generata come elementi stradali modificabili, non soltanto come immagine, mesh decorativa o linea di riferimento.
- [ ] I tratti consecutivi supportati hanno connessioni native persistenti e non soltanto estremità sovrapposte.
- [ ] L'adattamento alle geometrie native rispetta i limiti e le tolleranze congelati in G0; le differenze misurate sono riportate.
- [ ] Con TruckLib 0.5.1, i codici Q256 di ogni `Node.Position` coincidono
      esattamente con i codici attesi prima dell'editor e restano invariati
      dopo ricalcolo, salvataggio, chiusura e riapertura, secondo DT-07.
- [ ] Dopo apertura/importazione, eventuale ricalcolo previsto, salvataggio, chiusura e riapertura, strade, geometrie e connessioni restano disponibili.
- [ ] Versioni, formato nativo e prerequisiti sono dichiarati; un target esplicitamente incompatibile non riceve una conferma di compatibilità.
- [ ] Il percorso di verifica reale nel Map Editor è documentato e superato sull'ambiente target. Il solo test del serializzatore non è sufficiente.

### US-009: Collegare le intersezioni semplici supportate

**Descrizione:** Come autore di mappe, voglio che gli incroci semplici siano già connessi nell'output per evitare di ricostruire tutta la topologia nell'editor.

**Criteri di accettazione:**

- [ ] Il profilo iniziale include almeno un raccordo a T e uno a quattro bracci, a raso, per strade ordinarie a singola carreggiata nelle condizioni dichiarate dal profilo.
- [ ] Se necessari, sono usati prefab semplici già disponibili nell'installazione target; nessun nuovo asset 3D viene generato o redistribuito.
- [ ] L'algoritmo verifica compatibilità dei bracci, ingombro, direzione e tolleranze geometriche prima di applicare il raccordo.
- [ ] I connettori stradali vengono effettivamente collegati al raccordo; salvataggio e riapertura conservano la corrispondenza dei bracci.
- [ ] L'adattamento locale resta nel limite dichiarato dal profilo e viene associato al nodo OSM originario; oltre il limite il caso è non supportato.
- [ ] Rotatorie con raccordi, svincoli, intersezioni multilivello o altre configurazioni fuori profilo non vengono sostituiti con falsi incroci a raso.
- [ ] Ogni raccordo non supportato compare nel report con posizione e bracci coinvolti; le terminazioni native lasciate per rifinitura sono contate come tali, non come connessioni riuscite.

### US-010: Capire copertura, anomalie e interventi manuali

**Descrizione:** Come autore di mappe, voglio un report leggibile e analizzabile automaticamente per sapere cosa posso usare e cosa devo rifinire.

**Criteri di accettazione:**

- [ ] La console mostra fase corrente, esito, percorsi dei risultati e riepilogo delle limitazioni senza richiedere la lettura di un traceback per gli errori previsti.
- [ ] Un report JSON versionato separa contatori di elementi sorgente, tratti normalizzati, elementi nativi e raccordi; una way suddivisa non viene contata come più way importate.
- [ ] Ogni esclusione o mancata esportazione di una way candidata è rintracciabile tramite tipo/ID OSM, motivo e posizione quando disponibile.
- [ ] Una conversione parziale della stessa way è distinguibile da una conversione completa o da un'esclusione totale.
- [ ] La completezza della conversione si riferisce alla porzione richiesta dentro l'area: il ritaglio dei tratti esterni è riportato separatamente e, da solo, non rende una build `partial`.
- [ ] Il report distingue componenti sorgenti, tagli al bordo, disconnessioni dovute a esclusioni e difetti inattesi di esportazione.
- [ ] I tag verticali restano disponibili nella rappresentazione intermedia; i tratti non esportabili non vengono appiattiti e conteggiati come riusciti.
- [ ] Rete vuota, build fallita, build utilizzabile con limitazioni e build completa rispetto al profilo hanno esiti distinti secondo §6.3.

### US-011: Conservare provenienza e proteggere i progetti esistenti

**Descrizione:** Come autore di mappe, voglio risultati riproducibili e separati dal mio lavoro manuale per poter confrontare più conversioni senza perdere dati.

**Criteri di accettazione:**

- [ ] Il comando non modifica lo snapshot sorgente, gli archivi del gioco o una mappa/mod preesistente.
- [ ] Una destinazione esistente non vuota viene rifiutata prima della generazione; il MVP non richiede una modalità di sovrascrittura.
- [ ] Errori e interruzioni non pubblicano artefatti incompleti come build pronta; eventuali diagnostiche conservate dichiarano esplicitamente l'esito fallito.
- [ ] A parità di snapshot, configurazione e versioni, grafo e contenuto semantico dell'output sono riproducibili. Timestamp, durata e percorsi macchina possono differire.
- [ ] Il manifest registra hash sorgente, data snapshot se disponibile, versione del tool, profilo, dipendenze dell'esportatore, trasformazione e elenco degli artefatti.
- [ ] Il progetto generato include attribuzione a OpenStreetMap e ai suoi contributori, collegamento alla licenza dei dati e indicazioni separate dalla licenza del codice.
- [ ] Nessun asset proprietario del gioco viene copiato nell'output distribuibile; vengono registrati riferimenti e prerequisiti necessari.

### US-012: Completare il caso d'uso su un'area reale

**Descrizione:** Come autore di mappe, voglio una procedura ripetibile su un'area OSM reale per verificare che il tool riduca il lavoro di ricostruzione.

**Criteri di accettazione:**

- [ ] È documentato un estratto di una piccola area reale, con fonte, hash, data disponibile, dimensioni e rete attesa, scelto prima del collaudo finale.
- [ ] L'area comprende curve, continuità stradali e intersezioni semplici del profilo; non viene ridotta a un unico segmento dimostrativo.
- [ ] I comandi da file locale producono gli artefatti previsti; la modalità bbox è verificata separatamente e il suo snapshot può essere rieseguito offline.
- [ ] Seguendo le istruzioni si apre o importa il progetto nell'editor senza riscrivere a mano coordinate, geometrie o connessioni dichiarate supportate.
- [ ] Dopo il ciclo di salvataggio e riapertura, la porzione supportata è riconoscibile e non presenta connessioni mancanti o spurie rispetto al grafo atteso.
- [ ] Il verbale distingue chiaramente verifica automatica, verifica manuale e parti non supportate; nessuna fase non eseguita viene dichiarata superata.

## 4. Requisiti funzionali

### 4.1 Acquisizione e validazione

- **FR-1:** Il sistema deve esporre `osm2ets2 build`, con sorgente file tramite `--input` oppure online tramite `--bbox` in assenza di `--input`, destinazione `--output` e opzione `--scale`. Se presenti insieme, `--bbox` ritaglia il file locale senza accedere alla rete. La modalità online richiede l'endpoint configurato secondo DT-06.
- **FR-2:** Il sistema deve validare parametri, accessibilità della sorgente, destinazione e prerequisiti prima di attività costose o modifiche permanenti.
- **FR-3:** La bbox deve usare longitudine/latitudine WGS84 nell'ordine `west,south,east,north`, con valori finiti, intervalli validi e area non nulla; antimeridiano e domini non coperti dal profilo sono rifiutati.
- **FR-4:** Il sistema deve leggere snapshot `.osm` e `.osm.pbf` preservando ID, riferimenti e ordine delle way; la modalità file deve funzionare senza rete.
- **FR-5:** La modalità bbox online deve usare il provider Overpass separato dal parser, con selezione diretta delle way, recupero di tutti i nodi, endpoint configurabile e budget DT-08; deve conservare uno snapshot riutilizzabile. La strategia deve includere gli attraversamenti senza nodi interni: una limitazione nota o un errore che impedisce questa copertura non può produrre `success` o `partial`. Il replay con `--input` e la medesima `--bbox` deve riprodurre selezione, ritaglio e trasformazione.
- **FR-6:** Input strutturalmente invalidi, caratteristiche obbligatorie non supportate e riferimenti mancanti nelle way candidate devono impedire una build riuscita; non sono ammesse riparazioni geometriche implicite.
- **FR-7:** Un profilo di risorse versionato deve definire limiti numerici per estensione, dimensione input, elementi elaborabili, download e durata delle richieste; il superamento deve terminare con diagnosi e senza output dichiarato pronto.

I limiti numerici di FR-7 sono fissati in DT-08 come politica iniziale e richiedono POC-LIMITS per verificarne la sostenibilità. Il MVP non promette capacità illimitata o valori prestazionali non misurati. Per file locali la verifica deve essere progressiva se un limite non è ricavabile attendibilmente prima del parsing.

### 4.2 Selezione e normalizzazione

La seguente è la politica di selezione adottata, non una promessa che qualsiasi geometria appartenente a queste classi sia esportabile.

| Categoria OSM | Politica MVP |
| --- | --- |
| `motorway`, `trunk`, `primary`, `secondary`, `tertiary` | Includere nel grafo candidato; mapping secondo profilo. |
| `motorway_link`, `trunk_link`, `primary_link`, `secondary_link`, `tertiary_link` | Includere nel grafo candidato; nessuna ricostruzione automatica di svincoli complessi. |
| `unclassified`, `residential`, `service`, `living_street` | Includere nel grafo candidato; default espliciti quando mancano dettagli. |
| `footway`, `path`, `cycleway`, `steps`, `pedestrian`, `bridleway`, `track` | Escludere dal profilo iniziale. |
| `construction`, `proposed`, `abandoned`, `raceway`, `road` e altre classi non elencate | Escludere con motivazione; `road` è trattata come classificazione insufficiente per il mapping iniziale. |
| Way con `area=yes`, aree `area:highway`, elementi non stradali | Non convertirli in assi stradali. Un anello lineare privo di `area=yes` non è escluso soltanto perché chiuso. |

La distinzione fra classi stradali, percorsi e `unclassified`/`road` si basa sulla documentazione [OSM Highways](https://wiki.openstreetmap.org/wiki/Highways); la scelta dell'allowlist è una decisione di questo PRD.

- **FR-8:** Il sistema deve applicare la politica di selezione dichiarata, conservare i motivi delle esclusioni e non confondere selezione OSM con supporto dell'esportatore.
- **FR-9:** Il grafo deve conservare la provenienza OSM di nodi, tratti e trasformazioni; nodi sintetici e ID generati devono essere distinguibili e deterministici.
- **FR-10:** Le connessioni devono derivare dai nodi OSM condivisi dalle strade incluse; incroci geometrici, prossimità e arrotondamenti non devono creare adiacenze.
- **FR-11:** La normalizzazione deve preservare punti di forma, anelli e componenti sorgenti, dividendo le way dove necessario a rappresentare le intersezioni.
- **FR-12:** La normalizzazione deve conservare il verso sorgente e applicare il sottoinsieme documentato di `oneway`, incluse inversione `-1`, implicazioni supportate ed eccezioni esplicite. Valori reversibili, alternati, condizionali o sconosciuti devono essere segnalati come non supportati per l'esportazione direzionale.
- **FR-13:** Quando è presente `--bbox`, il sistema deve ritagliare i tratti al confine, creando terminali sintetici stabili e marcati; non deve perdere un tratto che attraversa l'area solo perché i suoi estremi sono esterni. Con `--input` senza bbox si usa l'estensione delle geometrie candidate, senza ritaglio aggiuntivo implicito.
- **FR-14:** Il sistema deve conservare `bridge`, `tunnel` e `layer` quando presenti. Non deve usare `layer` come altezza in metri né eliminare connessioni valide a un'estremità soltanto perché le way hanno layer differenti.
- **FR-15:** Ponti, tunnel e attraversamenti che richiedono una separazione verticale non dimostrata devono essere non esportabili nel profilo planare; non devono diventare falsi incroci a raso. Le discontinuità risultanti vanno dichiarate.
- **FR-16:** Tag di accesso, caratteristiche per camion e relazioni di restrizione eventualmente presenti devono restare rintracciabili o essere indicati come non applicati; il tool non deve dichiarare una rete legalmente instradabile o pronta per il traffico.
- **FR-17:** Il sistema deve validare integrità del grafo, riferimenti e geometrie; dati degeneri non strutturali devono avere una classificazione esplicita, senza eliminazioni silenziose.

Le way OSM sono sequenze ordinate di nodi; la coincidenza spaziale non sostituisce l'identità topologica. [OSM Elements](https://wiki.openstreetmap.org/wiki/Elements). Il verso di `oneway` dipende dall'ordine della way. [OSM Key:oneway](https://wiki.openstreetmap.org/wiki/Key:oneway). `layer` esprime ordinamento relativo, non una quota metrica. [OSM Key:layer](https://wiki.openstreetmap.org/wiki/Key:layer).

### 4.3 Coordinate, geometria e mapping

- **FR-18:** Il sistema deve trasformare WGS84 in AEQD ellissoidale locale secondo DT-07, produrre coordinate neutre est/nord/altezza e lasciare all'adapter la convenzione ETS2 verificata. Deve registrare CRS, origine, unità, versioni, parametri e misure separate per gli stadi float64 geografico/geometrico, scaling, float32 dell'adapter, Q256 di `Node.Position` e persistenza editor.
- **FR-19:** La scala deve moltiplicare uniformemente le distanze planimetriche rispetto all'origine deterministica; il default è `1`. La scala geometrica non deve essere confusa con i parametri del gioco per distanze di navigazione, tempo o economia.
- **FR-20:** L'esportatore deve applicare il piano di riferimento alle sole geometrie rappresentabili nel profilo planare; non deve inventare un modello di elevazione o dedurre quote reali dai layer.
- **FR-21:** Semplificazione, campionamento e adattamento alle curve native devono preservare topologia e punti vincolati e rispettare tolleranze misurabili del profilo. Non è prevista una semplificazione lossy opzionale attiva di default.
- **FR-22:** I vincoli di lunghezza, curvatura, ingombro e distanza dai raccordi devono essere verificati dopo scaling; un adattamento fuori tolleranza rende il caso non supportato, senza correzioni arbitrarie.
- **FR-23:** Un profilo versionato deve associare classi OSM, direzione e informazioni interpretabili sulle corsie a regole e asset ETS2 verificati, con default e fallback espliciti.
- **FR-24:** Il mapping deve mantenere distinta ogni carreggiata sorgente e non introdurre una strada bidirezionale dove è riconosciuta una carreggiata a senso unico.
- **FR-25:** Il sistema deve validare il profilo e la disponibilità delle dipendenze obbligatorie; il profilo MVP deve essere verificabile con gli asset del gioco base, senza DLC obbligatori.
- **FR-26:** La CLI deve consentire di selezionare un profilo tramite `--profile` e un eventuale mapping locale tramite `--mapping`; incompatibilità fra i due devono essere rifiutate. Il profilo MVP è il default dichiarato, senza selezioni implicite basate sul nome dell'area.

### 4.4 Output per il Map Editor

- **FR-27:** Il sistema deve usare l'adapter C#/.NET 10 con TruckLib 0.5.1 per produrre il progetto nativo `.mbd` con settori scelto in DT-02, contenente elementi stradali editabili nel Map Editor. Per ogni `TruckLib.ScsMap.Node.Position` l'adapter deve calcolare e verificare esattamente i codici Q256 richiesti da DT-07. L'IR generica è un artefatto intermedio e non soddisfa da sola il requisito.
- **FR-28:** Catene stradali e raccordi semplici del profilo devono avere connessioni native persistenti, dimostrate dopo apertura/importazione e dopo salvataggio e riapertura.
- **FR-29:** Il profilo iniziale deve coprire T e incroci a quattro bracci nel perimetro e nelle soglie DT-04, usando prefab esistenti verificati in POC-JUNCTION; rotatorie e junction complesse sono rinviate. Configurazioni fuori profilo devono avere posizione, motivazione e connessioni da rifinire esplicite.
- **FR-30:** Il progetto deve includere gli artefatti di §6.2 e istruzioni complete di apertura/importazione, ricalcolo, salvataggio, chiusura, riapertura e controllo, compreso il confronto componente per componente dei codici Q256 prima/dopo editor previsto da DT-07, senza richiedere la ricostruzione manuale delle parti dichiarate convertite. Il readback TruckLib resta diagnostico e non sostituisce il ciclo editor.
- **FR-31:** Il sistema deve registrare profilo target, versione del formato e dell'adattatore, requisiti di gioco e asset. La compatibilità dichiarata deve essere limitata alle combinazioni collaudate.

### 4.5 Operatività, report e riproducibilità

- **FR-32:** La CLI deve mostrare le fasi principali e generare un report strutturato con copertura, anomalie, elementi non supportati e rinvii alla sorgente.
- **FR-33:** Il report deve separare conteggi e stati per way, tratti, nodi e raccordi, riconciliare le conversioni parziali e distinguere topologia sorgente e nativa senza confrontare ingenuamente il numero di nodi. Il supporto è valutato sulla porzione in area: clipping esterno e oggetti interamente fuori area non sono mancate conversioni.
- **FR-34:** Il sistema deve distinguere gli esiti e i codici di uscita di §6.3; errori di infrastruttura non devono essere presentati come assenza di strade.
- **FR-35:** Il sistema deve proteggere input, archivi e progetti esistenti, rifiutare destinazioni non vuote e pubblicare un risultato pronto solo dopo validazione completa dell'esportazione prodotta.
- **FR-36:** La build deve essere semanticamente riproducibile a sorgente, configurazione e dipendenze fissate; i campi non deterministici devono essere esplicitamente esclusi dal confronto.
- **FR-37:** Ogni progetto deve includere provenienza e hash dei dati, parametri di conversione, attribuzione OSM e indicazioni sulla licenza dei dati, distinguendole dalla licenza del codice e dai diritti sugli asset ETS2.
- **FR-38:** La pipeline deve rispettare i livelli DT-03: sorgente, parser, modello geografico normalizzato, grafo, trasformazione, modello mappa neutro, adapter e verifica editor. Il contratto JSON tra Python e il processo C# deve essere versionato e privo di token, settori o strutture native ETS2; profilo e risultati di mapping restano separati.
- **FR-39:** La consegna deve includere una dimostrazione ripetibile su un'area reale e prove automatiche mirate, oltre al collaudo manuale nel Map Editor della combinazione dichiarata supportata.

### 4.6 Tracciabilità

| Storia | Requisiti principali |
| --- | --- |
| US-001 | FR-1, FR-2, FR-3, FR-7, FR-19, FR-26, FR-34 |
| US-002 | FR-4, FR-6, FR-9, FR-36 |
| US-003 | FR-3, FR-5, FR-6, FR-7, FR-13 |
| US-004 | FR-8, FR-16, FR-23 |
| US-005 | FR-9, FR-10, FR-11, FR-12, FR-13, FR-14, FR-17 |
| US-006 | FR-18, FR-19, FR-20, FR-21, FR-22 |
| US-007 | FR-12, FR-23, FR-24, FR-25, FR-26 |
| US-008 | FR-21, FR-22, FR-27, FR-28, FR-30, FR-31 |
| US-009 | FR-14, FR-15, FR-28, FR-29 |
| US-010 | FR-8, FR-15, FR-16, FR-17, FR-32, FR-33, FR-34 |
| US-011 | FR-2, FR-30, FR-35, FR-36, FR-37, FR-38 |
| US-012 | FR-5, FR-27, FR-28, FR-29, FR-30, FR-31, FR-39 |

## 5. Non-obiettivi e fuori scope

Il MVP non deve generare automaticamente una mappa ETS2 completa e pronta alla pubblicazione. Restano esclusi:

- edifici, vegetazione, landuse, paesaggio dettagliato e città completamente ricostruite;
- terreno realistico, dati di elevazione e ricostruzione automatica di ponti e tunnel;
- segnaletica completa, semafori avanzati, traffico e ricostruzione delle regole di circolazione;
- generazione di asset 3D, prefab personalizzati o complessi, rotatorie complete e svincoli;
- distributori, aziende/depot e punti di interesse;
- conversione di Europa, mondo o altre aree oltre i limiti dichiarati del MVP;
- replica di Google Maps o utilizzo di fonti proprietarie non autorizzate;
- aggiornamenti incrementali OSM, riconciliazione delle modifiche manuali e merge automatico con mappe esistenti;
- packaging completo della mod, pubblicazione su Workshop e compatibilità universale tra versioni ETS2;
- interfaccia grafica, navigazione per camion e supporto ad American Truck Simulator.

Sono comunque in scope le informazioni minime per riconoscere casi non supportati, evitare connessioni scorrette e consentire futuri ampliamenti. Il riuso dei pochi raccordi semplici previsti da US-009 non estende questo perimetro a un sistema generale di prefab e junction.

## 6. Considerazioni di design e utilizzo

### 6.1 Esperienza CLI adottata

I comandi seguenti descrivono il contratto futuro, non comandi già implementati:

```sh
osm2ets2 build \
  --bbox=<west,south,east,north> \
  --output ./build/map

osm2ets2 build \
  --input area.osm.pbf \
  --output ./build/map

osm2ets2 build \
  --input area.osm \
  --scale 0.1 \
  --profile <profilo-ets2-verificato> \
  --mapping ./road-mapping.json \
  --output ./build/map-scaled
```

Le parentesi angolari indicano valori da sostituire; nella shell il valore bbox può essere racchiuso tra virgolette. La forma con `=` è raccomandata anche per coordinate occidentali negative. Il primo esempio presuppone `OSM2ETS2_OVERPASS_URL` configurata; in alternativa si aggiunge `--overpass-url <endpoint>`. Il percorso del mapping nell'esempio è un futuro file dell'utente, non un file presente nel repository.

La CLI deve poter funzionare senza interazione, mostrare errori e avvisi testuali senza affidarsi al solo colore e riportare il percorso assoluto degli artefatti. Non occorrono percentuali di avanzamento se non misurabili: è sufficiente indicare acquisizione, parsing, normalizzazione, trasformazione, esportazione e verifica.

Con `--input` e `--bbox` insieme il file resta l'unica sorgente e viene ritagliato localmente: questa combinazione serve anche a rieseguire offline uno snapshot scaricato. La modalità bbox senza `--input` non deve scrivere coordinate o altri dati in servizi diversi dal provider necessario all'acquisizione. Non è richiesta telemetria. L'utente deve conoscere endpoint, timeout e limiti effettivi prima di usare la modalità online.

### 6.2 Contratto degli artefatti

I nomi seguenti definiscono il contratto adottato per l'output generato e non descrivono moduli già esistenti. Il set dei file nativi prodotti dall'adapter scelto sarà verificato in G0.

| Artefatto previsto | Contenuto richiesto |
| --- | --- |
| `project.json` | Versione schema, esito, hash sorgente, data snapshot se nota, bbox richiesta/effettiva, trasformazione, scala, modello di precisione DT-07, profilo, versioni e indice degli artefatti. |
| `network.json` | Grafo geografico versionato con geometrie WGS84, attributi normalizzati, adiacenze, ID e provenienza; nessuna struttura binaria o dipendenza ETS2. |
| `map-model.json` | Modello mappa indipendente da ETS2, in coordinate locali della scena con unità esplicite, connettività richiesta e riferimenti al grafo sorgente; input dell'adapter, separato dal profilo nativo. |
| `report.json` | Contatori riconciliabili, tempi misurati, diagnostiche, esclusioni, conversioni parziali e connessioni da rifinire; massimi numerici separati per stadio e riconciliazione completa dei codici Q256 attesi/effettivi. |
| `IMPORT.md` | Prerequisiti e sequenza esatta per apertura/importazione, ricalcolo, salvataggio, chiusura, riapertura e verifica, incluso il confronto dei codici Q256 prima/dopo editor. Include compatibilità, attribuzione e limitazioni. |
| Output nativo | Progetto `.mbd`, cartella settori corrispondente e accessori necessari al target; elementi stradali editabili, non sostituibili dal formato generico. |
| Snapshot acquisito | Obbligatorio per la modalità bbox online; formato leggibile dalla modalità `--input`, metadati e hash. Non è richiesta una copia del file locale fornito dall'utente. |

Il manifest e lo schema devono distinguere il risultato nativo generato e validato automaticamente dalla prova manuale di compatibilità del profilo. Una build ordinaria non può affermare che il suo specifico file sia stato aperto nell'editor se questa azione non è avvenuta.

La riproduzione offline deve riapplicare la stessa area con `--input <snapshot-acquisito> --bbox=<bbox-originale>`, mantenendo scala, profilo, mapping e versioni. La bbox determina la stessa origine e la stessa scelta di proiezione in entrambe le modalità; il test di replay deve confrontare anche i parametri della trasformazione. `IMPORT.md` deve riportare il comando completo con i valori effettivi e una nuova destinazione. Il manifest conserva la bbox originale: riutilizzare le way complete senza quel filtro può ampliare l'area e non è una riproduzione equivalente.

### 6.3 Esiti e report

| Esito | Codice adottato | Significato |
| --- | --- | --- |
| `success` | `0` | Acquisizione valida e artefatti validi prodotti; nessuna porzione candidata in area omessa o raccordo irrisolto rispetto al profilo. Esclusioni intenzionali di classi fuori scope e ritaglio al bordo sono comunque riportati. |
| `partial` | `3` | Output nativo valido e non vuoto, con elementi candidati o raccordi fuori dalle condizioni di supporto dichiarate. Le omissioni controllate e le connessioni da rifinire sono motivate; l'esito deve essere evidente anche agli script. |
| `empty` | `4` | Input valido, ma nessuna strada esportabile dopo selezione e valutazione; solo diagnostiche, nessuna mappa dichiarata pronta. |
| `invalid_input` | `2` | Parametri, dati locali, profilo o destinazione non validi; limiti dimensionali, di conteggio o dominio superati. |
| `failed` | `1` | Errore operativo: rete, risposta remota incompleta/sovradimensionata, timeout locale/remoto, I/O, dipendenze o serializzazione/validazione nativa non riuscita. |

I codici costituiscono il contratto CLI adottato, da mantenere coerente in documentazione e test. Un file incompleto, una violazione dell'integrità nativa, una connessione spuria, la perdita di una connessione dichiarata supportata o un errore dell'adattatore non sono esiti `partial`: la build fallisce. Solo i casi fuori dalle condizioni di supporto fissate prima della conversione possono essere omessi in modo controllato e dichiarato. Un difetto del convertitore non può essere riclassificato come limite del profilo.

Per ogni way candidata il report deve permettere di distinguere `converted`, `partial`, `unsupported` e `invalid`; le way escluse a monte hanno stato `ignored`. Lo stato `converted` significa che tutta la porzione richiesta in area è convertita; una porzione esterna ritagliata non causa `partial`. Le way interamente esterne hanno stato `ignored` con motivo `outside_area`; quelle tagliate conservano un'indicazione separata di clipping. I conteggi per way non sono sommabili ai conteggi per segmenti. La somma degli stati terminali deve riconciliare il totale delle way osservate dalla selezione, senza imporre di enumerare ogni oggetto non stradale.

La connettività viene misurata su due livelli: grafo normalizzato completo e sottografo effettivamente esportato. Nel secondo livello, raccordi e segmenti aggiunti dall'adattatore vanno ricondotti ai collegamenti sorgenti; non basta confrontare il numero delle componenti, perché due connessioni errate potrebbero compensarsi numericamente.

### 6.4 Scala e qualità geometrica

La riduzione della scala riguarda le posizioni e le distanze planimetriche, non comporta automaticamente la riduzione della larghezza dei modelli stradali ETS2. Un'area molto densa può quindi non essere rappresentabile alla scala richiesta. Il tool deve diagnosticare i conflitti invece di deformare liberamente la mappa.

La tolleranza di adattamento delle curve e quella per l'inserimento dei raccordi devono essere separate, espresse in unità della scena e fissate nel profilo verificato. Devono essere riportati lo scostamento massimo misurato e la posizione delle modifiche. La tolleranza non autorizza nuove connessioni, perdita di nodi di intersezione o fusione di carreggiate distinte.

## 7. Decisioni tecniche adottate

### 7.1 Stato delle decisioni

Le otto scelte seguenti sono adottate come baseline progettuale il 31 agosto 2026, in risposta alla richiesta di chiusura delle decisioni tecniche. Non costituiscono una dichiarazione di implementazione o di compatibilità sperimentale.

DT-07 è stata revisionata esplicitamente il 2 settembre 2026 in conseguenza del
`FAIL` di PoC-002 v1. DT-01–DT-06 e DT-08 restano invariate.

- **Confermato:** requisito dell'utente, evidenza del repository o comportamento documentato da una fonte primaria citata.
- **Adottato:** scelta del progetto dopo confronto delle alternative; vincolante per la successiva implementazione fino a revisione esplicita.
- **Da validare con PoC:** proprietà della combinazione concreta di strumenti, asset e dati che la sola documentazione non dimostra.

| ID | Decisione adottata | Verifica ancora necessaria |
| --- | --- | --- |
| DT-01 | ETS2 **1.60.x stabile**, Map Editor incluso, Windows 11 x64; profilo `ets2-1.60-native-v1`. | Build completa installata e catalogo del gioco base, apertura e persistenza. |
| DT-02 | Progetto nativo **`.mbd` più cartella settori**, scritto tramite **TruckLib 0.5.1**. | Set minimo effettivo di file, integrità, ricomputazione e riapertura. |
| DT-03 | Core Python, modello indipendente da ETS2, adapter **C#/.NET 10** in processo separato con contratto JSON versionato. | Interoperabilità, errori, determinismo e rispetto dei confini. |
| DT-04 | Rettifili, curve, continuità e T/4-vie semplici; riuso di un catalogo minimo di prefab esistenti. Rotatorie rinviate. | Identificatori, varianti, connettori e campo di applicabilità dei prefab. |
| DT-05 | **CPython 3.14.7**, uv, osmium, pyproj, Shapely, argparse/logging, pytest/Hypothesis, Ruff/mypy. | Ambiente risolto e riproducibile sulle piattaforme dichiarate. |
| DT-06 | `.osm` e `.osm.pbf` locali obbligatori; primo provider remoto **Overpass configurabile**, senza endpoint obbligatorio nel dominio. | Risposte complete, attraversamenti, policy dell'istanza, replay offline. |
| DT-07 | WGS84 → **AEQD ellissoidale locale** → E/N/H float64; scala geometrica esplicita; conversione float32 separata; per l'adapter selezionato, serializzazione Q256 di `TruckLib.ScsMap.Node.Position` verificata per TruckLib 0.5.1; persistenza editor separata. | Rerun completo PoC-002 sui criteri revisionati; semantica degli assi e stabilità dei codici Q256 dopo il ciclo Map Editor. |
| DT-08 | Profilo di ammissione **25 km² / diagonale 10 km**, con limiti espliciti di file, elementi e richieste. | Consumo reale e arresto controllato ai limiti. |

Le versioni nominate restano scelte di baseline del prodotto. Gli spike hanno
risolto soltanto i sottoinsiemi necessari ai propri esperimenti; lock, schemi e
profili completi del prodotto restano artefatti da introdurre
nell'implementazione.

### 7.2 G0 — PoC obbligatori prima di dichiarare il MVP utilizzabile

G0 conserva il suo ruolo di verifica iniziale dell'esportazione. PoC-001 ha
superato il percorso nativo minimo. PoC-002 v1 ha prodotto `FAIL` rispetto alla
soglia numerica originale e ha causato la revisione DT-07 documentata in §7.9;
il rerun sui criteri revisionati ha `PASS` automatico ed è
`AWAITING_MANUAL_VALIDATION`. G0 non è superato e PoC-003 e PoC-004 restano
`NOT_EXECUTED`, bloccati dal gate PoC-002 non superato. La tabella seguente
conserva l'insieme delle prove richieste, non il loro stato di esecuzione.

| Prova | Evidenza richiesta e condizione di superamento |
| --- | --- |
| POC-ENV | Installare le versioni fissate, leggere XML/PBF equivalenti, proiettare e ritagliare, scambiare il modello JSON fra Python e C#. Registrare lock, runtime e piattaforme; verificare anche schema sconosciuto, timeout e crash dell'adapter. |
| POC-ETS2 | Su Windows 11 x64 con ETS2 1.60.x stabile, registrare build completa, formato e impronta del catalogo base. Generare rettifilo, curva e catena connessa; aprire, usare **Map → Recompute map**, salvare, chiudere e riaprire. Controllare elementi editabili, nodi, geometrie, assi, scala e log dell'editor. Nessuna ricostruzione manuale. |
| POC-JUNCTION | Identificare nel gioco base un prefab T e uno a quattro bracci compatibili con il profilo; collegare automaticamente tutti i bracci, verificare dopo riapertura e misurare gli scostamenti entro DT-04. Un sample upstream non sostituisce questa verifica. |
| POC-OSM | Sull'istanza Overpass scelta verificare bbox asimmetrica, way senza nodi interni che attraversa l'area, uscita/rientro, riferimenti completi, risposta XML con errore e uguaglianza del replay locale. Nessun test ordinario di CI deve usare un server pubblico. |
| POC-LIMITS | Misurare tempi e picco di memoria su casi rappresentativi e prossimi alle soglie DT-08, comprese geometrie complesse e input compressi. Provare il superamento di ogni limite e l'assenza di output falsamente pronto. I limiti non sono un benchmark già superato. |

Il verbale deve distinguere risultati automatici, controlli manuali, avvisi noti motivati ed errori. Mancanza di asset, perdita di connessioni supportate o file nativi invalidi impediscono il superamento delle prove. La lettura di ritorno con TruckLib è utile ma non prova da sola la compatibilità con l'editor.

Dopo G0 occorre ripetere il percorso su un'area reale secondo US-012. La build completa e gli asset approvati entrano nel profilo versionato. Se una prova essenziale fallisce, si corregge l'adattatore oppure si revisiona esplicitamente la decisione: non si ridefinisce il successo come generazione di JSON, immagine o strade solo sovrapposte.

### 7.3 DT-01 — Versione target e strategia di evoluzione

**Confermato dalle fonti:** SCS ha pubblicato ETS2 1.60 stabile il 18 giugno 2026. TruckLib dichiara il formato mappa 907 per ETS2 1.59–1.60; questa dichiarazione non equivale a un test di `osm2ets2`. [SCS: rilascio 1.60](https://blog.scssoft.com/2026/06/euro-truck-simulator-2-160-update.html), [TruckLib: formati supportati](https://sk-zk.github.io/trucklib/master/).

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| ETS2 1.59 | Versione precedente con formato dichiarato compatibile. | Richiede una baseline più vecchia senza un beneficio dimostrato per questo progetto. | Non scelta. |
| ETS2 1.60 stabile | Rilascio ufficiale e corrispondenza documentata con il writer selezionato. | Patch, catalogo asset e comportamento editor vanno comunque verificati insieme. | **Adottata.** |
| 1.61 sperimentale o inseguimento di `latest` | Accesso anticipato alle evoluzioni del gioco. | Cambiamenti non collaudati e perdita di riproducibilità. L'annuncio sperimentale non prova compatibilità. | Fuori baseline. |
| Supporto immediato a più versioni | Più utenti potenziali. | Matrice asset/formati più ampia e costo sproporzionato per il primo MVP. | Rinviato. |

L'esistenza della linea sperimentale 1.61 è documentata da [SCS](https://blog.scssoft.com/2026/06/ets2-ats-161-experimental-beta.html); la decisione resta 1.60 stabile anche se, durante l'implementazione, risultassero disponibili release successive.

**Scelta adottata:** `ets2-1.60-native-v1`, gioco base, senza DLC obbligatori, collaudo nel Map Editor su Windows 11 x64. Non si inventa il numero della patch: POC-ETS2 deve registrare la build completa effettivamente installata. La famiglia 1.60 è la baseline scelta, mentre soltanto le build elencate nel profilo collaudato potranno essere dichiarate verificate.

**Versioni successive:** nessun aggiornamento automatico del writer, nessun riuso silenzioso del profilo con un gioco diverso. Una nuova linea ETS2 richiede profilo distinto, dipendenze fissate, controllo dei cambiamenti di formato e asset e ripetizione delle prove native. L'IR rimane riutilizzabile e gli output sono rigenerati in directory nuove; le mappe rifinite a mano non sono sovrascritte. Il caricamento di output più recenti con un adapter vecchio non è garantito.

**Rischio residuo:** la disponibilità futura della specifica build del gioco e dei suoi asset deve essere verificata prima del PoC. TruckLib supporta un formato alla volta; un suo aggiornamento può richiedere la conversione di mappe nell'editor. Una corrispondenza del solo numero di formato non certifica i riferimenti agli asset.

### 7.4 DT-02 — Formato di output e percorso nativo

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| JSON/GeoJSON/OBJ come risultato finale | Facile ispezione e riuso geografico. | Non dimostra strade native modificabili e connesse nel Map Editor. | Ammesso solo come supporto diagnostico, non come consegna MVP. |
| Writer binario ETS2 scritto in Python | Un solo runtime e controllo totale. | Formato non sufficientemente specificato, manutenzione e rischio di file apparentemente validi ma errati. | Non adottato. |
| Mappa `.mbd` con settori tramite TruckLib | Creazione/salvataggio già descritti dall'autore della libreria; adatto a progetto autonomo. | Dipendenza non ufficiale, runtime .NET, ricalcolo e compatibilità da provare. | **Adottato.** |
| Selezione `.sbd` | Formato realmente importabile nell'editor, utile per inserimenti futuri. | Richiede una mappa ospite e gestione dell'origine/importazione; non elimina la necessità del writer. | Rinviato, non richiesto in aggiunta al nativo. |
| Mid-format SCS e Conversion Tools | Strumenti SCS capaci di convertire anche risorse di mappa. | Non è stato individuato un contratto documentato per convertire l'IR proposta in strade native complete. | Spike alternativo solo se il percorso scelto fallisce. |

**Confermato:** il Map Editor gestisce mappe composte da più file e supporta l'importazione di selezioni `.sbd`. TruckLib documenta la creazione di una mappa, il salvataggio `.mbd` e l'assegnazione degli elementi ai settori. [SCS: file di mappa](https://modding.scssoft.com/wiki/Tutorials/Map_Editor/Introduction_to_the_Map_Editor/Saving,_Loading,_Sectors,_and_Files), [SCS: importazione](https://modding.scssoft.com/wiki/Documentation/Tools/Map_Editor/Shortcuts), [TruckLib: Map](https://sk-zk.github.io/trucklib/master/docs/TruckLib.ScsMap/map-class.html).

**Scelta adottata:** l'adapter usa **TruckLib 0.5.1**, versione NuGet esatta, per scrivere un progetto nativo in una directory isolata. Il pacchetto dichiara target .NET 10.0. Non si usa un branch mobile; versione e hash delle dipendenze saranno fissati nel lock dell'adapter. [Pacchetto TruckLib 0.5.1](https://www.nuget.org/packages/TruckLib/0.5.1).

Il contratto minimo di prodotto è un file `.mbd`, la corrispondente cartella dei settori e ogni file accessorio effettivamente necessario alla build target. Non si congela per supposizione una lista minima di estensioni né si eliminano file prodotti dal serializer: il set sufficiente deve essere verificato in POC-ETS2. Il sistema non deve presentare una singola `.mbd` priva dei dati richiesti come mappa completa.

L'output deve essere selezionabile, modificabile, salvabile e riapribile nel Map Editor. `IMPORT.md` descriverà la collocazione esatta e il caricamento verificati. Il ricalcolo previsto dal writer è un passaggio ammesso; il ricalco delle strade o il collegamento manuale di raccordi dichiarati supportati non lo è.

**Rischi/PoC:** TruckLib si dichiara alpha e segnala problemi possibili sui prefab. Il suo salvataggio può ripulire la directory dei settori, quindi l'adapter scrive solo nell'area temporanea della nuova build. [Limitazioni upstream](https://github.com/sk-zk/TruckLib). Non si afferma che il writer sia un'API ufficiale SCS.

I [Conversion Tools SCS](https://modding.scssoft.com/wiki/Documentation/Tools/Conversion_Tools) menzionano anche risorse di mappa: non sono esclusi perché limitati ai soli modelli 3D, ma perché manca una dimostrazione del percorso IR → strade per questo progetto. Il comando di debug `edit_save_text` non prova un'importazione testuale generica. [SCS: comandi console](https://modding.scssoft.com/wiki/Documentation/Engine/Console/Commands).

### 7.5 DT-03 — Architettura e contratto dell'adapter

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| Pipeline Python accoppiata al writer | Meno confini iniziali. | Parser e geometria diventano dipendenti da strutture ETS2 e runtime specifici. | Scartata. |
| Python con binding .NET nello stesso processo | Chiamate dirette alla libreria. | Packaging, ciclo di vita e gestione degli errori fra runtime più complessi; confini meno controllabili. | Non adottata. |
| Intero progetto C# | Un solo runtime e possibilità di mantenere comunque un'architettura disaccoppiata. | Richiederebbe rivalutare parser e librerie geografiche .NET invece di usare lo stack Python selezionato. | Alternativa valida, non scelta. |
| Core Python + processo C# con IR JSON | Confini verificabili, sostituibilità del writer e isolamento dei crash. | Due runtime, schema da mantenere e costo di serializzazione. | **Adottata.** |

**Pipeline vincolante:**

```text
OSM source (file / provider)
  → OSM parser
  → normalized geographic model (WGS84)
  → road graph / topology
  → coordinate transformation, clipping, scale
  → ETS2-independent map model
  → ETS2 adapter / exporter
  → Map Editor validation
```

| Livello | Responsabilità | Dipendenze vietate |
| --- | --- | --- |
| Sorgente | Restituire uno snapshot standard con provenienza e stato di acquisizione. | Geometrie native, TruckLib, asset ETS2. |
| Parser | Decodificare XML/PBF in record del progetto; copiare i dati necessari dagli oggetti streaming. | HTTP, criteri dei prefab, trasformazioni di gioco. |
| Modello geografico normalizzato | ID/riferimenti sorgente, coordinate WGS84, attributi normalizzati e anomalie. | Tipi del parser conservati oltre la lettura; oggetti .NET. |
| Grafo stradale | Adiacenze, direzione, tratti, anelli, archi paralleli e provenienza. | Inferenza di connessioni da intersezioni geometriche; identificatori di asset. |
| Trasformazione | Proiezione metrica, ritaglio senza false connessioni e scala. | Settori, UID nativi, road look o prefab. |
| Modello mappa neutro | Geometrie in metri della scena, nodi, tratti, connessioni richieste e semantica stradale. | Formato 907, nomi di settori, classi TruckLib, identificatori di asset SCS. |
| Adapter ETS2 | Validare il target, applicare mapping e vincoli, risolvere asset, adattare curve/raccordi e serializzare. | Parsing OSM o download; modifica silenziosa del modello sorgente. |
| Validazione | Confrontare risultato e contratto; produrre evidenze automatiche e, separatamente, collaudo editor. | Dichiarare effettuata una verifica manuale che non è stata eseguita. |

Il grafo completo resta disponibile prima del ritaglio; i terminali al confine entrano nella vista ritagliata senza cambiare l'adiacenza sorgente. L'ordine fisico delle operazioni può essere ottimizzato solo preservando questi risultati e la provenienza.

**Contratto adottato:** `network.json` conserva il grafo geografico; **`map-model.json`** è l'IR neutra che attraversa il confine di processo. Il contratto ha versione di schema esplicita, ID serializzati come stringhe, numeri finiti, unità/assi dichiarati e riferimenti di provenienza. Una strada usa categorie semantiche, direzione e corsie normalizzate; non contiene token `road_look`, percorsi `.ppd`, UID o dettagli di settori. Le connessioni richieste sono dati espliciti, non dedotte dall'adapter per prossimità.

Il profilo ETS2 e il mapping sono un secondo input separato dell'adapter. Il risultato restituisce artefatti nativi e una corrispondenza fra ID neutri, elementi nativi, trasformazioni e diagnostiche, da integrare in `report.json`. Lo stato di esportazione non modifica l'IR originale. Un futuro exporter potrà consumare la stessa versione dell'IR, senza importare parser OSM o codice ETS2 nel core.

L'orchestratore invoca l'eseguibile C# con argomenti strutturati e percorsi di file, senza shell. Nessun server HTTP, servizio residente o callback per ogni nodo. Sono obbligatori negoziazione della versione di schema, validazione su entrambi i lati, limite di durata, gestione del processo figlio, log su stderr e pubblicazione atomica solo dopo validazione. Un crash o schema incompatibile dà `failed` e non un risultato parziale.

**Runtime adottato:** .NET 10 LTS per l'adapter; Microsoft ne documenta il supporto fino a novembre 2028. SDK e pacchetti vengono fissati durante POC-ENV. Si parte con eseguibile framework-dependent e prerequisito runtime esplicito; un bundle self-contained è un miglioramento distributivo successivo. [Supporto .NET](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).

**Rischi/PoC:** doppio ambiente, precisione JSON, identità degli archi paralleli e divergenze fra validatori. POC-ENV deve dimostrare lo scambio senza perdita e l'isolamento degli errori. La pipeline è una decisione architetturale confermata; il funzionamento combinato non è ancora provato.

### 7.6 DT-04 — Raccordi e geometrie supportati

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| Solo strade indipendenti o catene | PoC iniziale più piccolo. | Non soddisfa la rete minima con intersezioni richiesta. | Utile come primo test, non come MVP completo. |
| Catalogo ristretto di prefab del gioco base | Riusa connettori e geometrie già esistenti. | Richiede verifica di varianti, corsie, orientamento e spazio disponibile. | **Adottato per T/4-vie.** |
| Junction/prefab procedurali | Maggiore fedeltà alle forme OSM. | Nuovi asset e logiche complesse oltre il perimetro. | Rinviato. |
| Rotatorie automatiche nel primo profilo | Copertura urbana più ampia. | Numero di uscite, raggi, corsie e innesti ampliano molto i casi da gestire. | Rinviate, anche se geometricamente semplici. |

**Campo minimo adottato:**

| Caso | Supporto richiesto |
| --- | --- |
| Rettifilo e vicolo cieco | Elementi road nativi; terminali reali distinti dai tagli bbox. |
| Curva e continuità di grado 2 | Curva adattata entro tolleranza, connessione effettiva, nessuna inversione del senso normalizzato. |
| Catene a senso unico | Supportate dove il catalogo stradale rappresenta le corsie richieste; nessuna conversione implicita in doppio senso. |
| T a raso, 3 bracci | Obbligatoria per strade a singola carreggiata, bidirezionali, una corsia per direzione, guida a destra, asset compatibili. |
| Incrocio a raso, 4 bracci | Stesso profilo della T; geometria quasi ortogonale nei limiti sotto indicati. |
| T/4-vie con bracci a senso unico, corsie asimmetriche o spartitraffico | Fuori dal primo catalogo dei raccordi; riconosciute e rendicontate. |
| Rotatorie, mini-roundabout e junction complesse | Topologia conservata nel grafo; esportazione automatica del raccordo rinviata. |
| Sovrappassi, tunnel, svincoli multilivello | Riconosciuti ma non ricostruiti nel profilo planare. |

La guida a destra è una condizione esplicita di `ets2-1.60-native-v1`, non una deduzione automatica dalla lingua o dalle coordinate. Mappe con altra convenzione richiedono un profilo futuro; la CLI deve rendere visibile la convenzione adottata.

**Soglie progettuali iniziali, non limiti documentati del motore:** sui tratti ordinari, fuori dalle regioni di adattamento ai raccordi, scostamento planimetrico massimo **1,0 m della scena** rispetto all'asse sorgente trasformato; nelle sole regioni di approccio ai raccordi, dichiarate nel report, massimo **2,0 m**. Le soglie si misurano come distanza di Hausdorff simmetrica fra le curve corrispondenti, con calcolo o campionamento dotato di margine d'errore massimo **1 cm**, incluso nel confronto con la soglia. La differenza fra tangente dell'approccio alla porta e direzione richiesta dalla porta è al massimo **10°**, tenendo conto del verso di connessione. Per T e 4-vie si ricerca un allineamento rigido del prefab, senza deformarlo; la connettività non ammette tolleranze topologiche.

Il taglio degli approcci all'ingombro del prefab è ammesso e rendicontato. Il limite laterale si misura sugli approcci esterni al prefab; la geometria interna dell'incrocio è rappresentata dall'asset, non dall'intersezione puntiforme OSM. Le regioni di raccordo non possono sovrapporsi e i tratti residui devono rispettare i limiti del modello nativo verificato. Non si inventa un valore universale di lunghezza minima ETS2.

Default semantici iniziali: strada bidirezionale senza `lanes` → due corsie totali; strada a senso unico senza `lanes` → una corsia, oppure due per `motorway`/`motorway_link`. Il fallback deve essere visibile nel report. Un tag esplicito incompatibile non viene sostituito col default. Le classi principali possono riusare un tipo stradale generico dichiarato; il MVP non promette l'aspetto reale di ogni categoria.

**Confermato vs PoC:** la documentazione TruckLib mostra strade concatenabili e un esempio di T con prefab. Non attesta che i token del sample siano disponibili senza DLC, né dimostra tutti i raccordi previsti. [Elementi stradali](https://sk-zk.github.io/trucklib/master/docs/TruckLib.ScsMap/polyline-items.html), [prefab](https://sk-zk.github.io/trucklib/master/docs/TruckLib.ScsMap/prefabs.html), [sample upstream](https://sk-zk.github.io/trucklib/master/docs/Samples/02-prefabs.html).

POC-JUNCTION deve produrre il catalogo con identificatori, varianti, porte, dipendenze e impronta dei dati, provando le soglie. Questi dati restano volutamente non inventati nel PRD. Se non esistono asset base idonei o il collegamento non persiste, questa parte del MVP è bloccata sperimentalmente: non è autorizzato declassificarla a raccordi manuali.

### 7.7 DT-05 — Stack, dipendenze e qualità

| Area | Alternative considerate: vantaggi e svantaggi | Scelta adottata |
| --- | --- | --- |
| Python | 3.14: più recente con wheel già pubblicate; combinazione da collaudare. 3.13: conservativa, senza vantaggio dimostrato per le dipendenze richieste. 3.12: fase di sicurezza. Prerelease/free-threaded: ulteriore matrice. | **CPython 3.14.7 standard GIL**, una minor iniziale; niente PyPy, free-threaded o funzionalità sperimentali. |
| Dependency management | uv: gestione interprete, ambiente e lock nello stesso flusso. pip/venv/pip-tools: strumenti separati. Poetry: valida alternativa, nessun beneficio specifico qui. | **uv 0.12.7**, metadati `pyproject.toml`, lock versionato e gruppi distinti per runtime e sviluppo. |
| Parser | pyosmium: streaming XML/PBF e controllo dei dati. Pyrosm: orientato a PBF/GeoDataFrame. OSMnx: impone più dipendenze e politiche da controllare. Parser proprio: manutenzione elevata. | **osmium 4.3.1** (progetto pyosmium), per entrambi i formati. |
| CRS/geometria | pyproj+Shapely: ruoli separati. GDAL/GeoPandas: più formati e tabelle non richiesti. Codice numerico proprio: meno dipendenze, più rischio. | **pyproj 3.7.2 + Shapely 2.1.2**; nessun GeoPandas/GDAL esplicito. |
| Grafo | Modello tipizzato e adiacenze: controllo e dipendenze minime. NetworkX MultiDiGraph: algoritmi pronti, struttura generica aggiuntiva. | **Dataclass, ID stabili, indici di adiacenza** con archi paralleli e orientamento; NetworkX non necessario nel MVP. |
| CLI | argparse: standard library, sufficiente per `build`; validazione esplicita. Typer: aiuto più ricco e type hint, dipendenze ulteriori. | **argparse**, senza trasferire i suoi tipi nel dominio. |
| HTTP | HTTPX: streaming e timeout configurabili. urllib: nessuna dipendenza aggiuntiva, più gestione manuale. | **HTTPX 0.28.1 sincrono** nel solo provider remoto; nessun framework asincrono o prerelease 1.0. |
| Logging | logging: standard e integrato. structlog: più funzionalità per eventi strutturati, non necessarie qui. | **logging standard su stderr**; report JSON separato come artefatto di prodotto. |
| Test | pytest: fixture e parametrizzazione; unittest: meno dipendenze ma più verboso; Hypothesis: casi limite e invarianti. | **pytest 9.x + Hypothesis 6.x**, solo sviluppo. |
| Lint/format/tipi | Ruff+mypy: responsabilità separate. Black/Flake8/isort: più strumenti sovrapposti. Pyright: alternativa valida, nessun bisogno di un secondo checker. | **Ruff 0.16.x** per lint/format e **mypy 2.x strict** sul codice proprio. |

**Evidenze e versioni:** Python 3.14.7 è una release ufficiale del 5 agosto 2026. Le wheel standard CPython 3.14 di osmium, pyproj e Shapely risultano pubblicate per Windows, macOS e Linux; la loro disponibilità non prova l'installazione combinata. [Python 3.14.7](https://www.python.org/downloads/release/python-3147/), [ciclo Python](https://devguide.python.org/versions/), [osmium](https://pypi.org/project/osmium/4.3.1/#files), [pyproj](https://pypi.org/project/pyproj/3.7.2/#files), [Shapely](https://pypi.org/project/shapely/2.1.2/#files).

Il parser scelto supporta XML/PBF ma gli oggetti esposti durante lo streaming hanno durata limitata: il modello deve copiarne i dati, non trattenerli. È prevista una lettura per raccogliere way/riferimenti e una seconda per risolvere i nodi se necessaria, così l'ordine del file non diventa un requisito nascosto. [Formati pyosmium](https://docs.osmcode.org/pyosmium/latest/user_manual/07-Input-Formats-And-Other-Sources/), [oggetti streaming](https://docs.osmcode.org/pyosmium/latest/user_manual/01-First-Steps/).

**Politica di versionamento adottata:** fissare le versioni esatte sopra indicate per i PoC pertinenti e per il prodotto, incluse [uv 0.12.7](https://pypi.org/project/uv/0.12.7/) e [HTTPX 0.28.1](https://pypi.org/project/httpx/0.28.1/); fissare patch SDK .NET e dipendenze transitive nel bootstrap. Le famiglie degli strumenti di sviluppo sono scelte, mentre le patch saranno registrate nel lock. Il lock isolato di PoC-002 copre soltanto il suo sottoinsieme geografico e non risolve il lock completo del prodotto, inclusi osmium e HTTPX. Nessuna dipendenza `latest`, nessun aggiornamento automatico durante `build`; aggiornamenti intenzionali dopo i controlli. Il lock universale non sostituisce una matrice di test. [Layout uv](https://docs.astral.sh/uv/concepts/projects/layout/), [lock e sincronizzazione](https://docs.astral.sh/uv/concepts/projects/sync/), [versionamento Ruff](https://docs.astral.sh/ruff/versioning/).

**Piattaforme adottate:** Windows 11 x64, Ubuntu 24.04 x64 e macOS 14+ ARM64 per core, CLI e contratto dell'adapter. Il collaudo completo Map Editor è obbligatorio solo su Windows 11 x64; nessuna equivalenza editor su Linux/macOS è dichiarata. La generazione nativa senza editor sugli altri sistemi deve essere verificata come parte della matrice; eventuali blocchi di packaging non possono essere nascosti. Mac Intel e altre architetture sono fuori dalla matrice iniziale. Il minimo macOS ARM64 riflette le wheel pyproj scelte, non un limite generale del progetto.

**Controlli di qualità da introdurre:** test pytest/Hypothesis di parsing, invarianti topologiche, proiezione, errori e contratti; Ruff check e format check; mypy strict; compilazione C# con nullable abilitato e avvisi trattati come errori sul codice proprio; test del confine adapter e fixture native in G0. Non è necessario scegliere ora un ulteriore framework di unit test C#: il contratto del processo deve essere esercitato dalla suite di integrazione.

Non sono ancora comandi della suite del prodotto. La futura documentazione dovrà riportare i comandi reali associati ai manifest creati nell'implementazione; i comandi degli spike restano evidenze isolate. Le soppressioni di typing verso librerie native devono essere locali e motivate, senza disabilitazioni globali. [pytest](https://docs.pytest.org/en/stable/), [Hypothesis](https://hypothesis.readthedocs.io/en/latest/), [Ruff](https://docs.astral.sh/ruff/), [mypy](https://mypy.readthedocs.io/en/stable/).

Logging e CLI devono includere codici diagnostici, riepiloghi e percorsi; i log non sostituiscono il report. La forma `--bbox=<west,south,east,north>` evita ambiguità con valori occidentali negativi ed è quella da documentare. HTTPX distingue timeout di connessione/lettura da un limite totale: il budget complessivo va applicato dall'orchestratore. [argparse](https://docs.python.org/3.14/library/argparse.html), [logging](https://docs.python.org/3.14/library/logging.html), [HTTPX: timeout](https://www.python-httpx.org/advanced/timeouts/), [HTTPX: streaming](https://www.python-httpx.org/quickstart/#streaming-responses).

**Rischi/PoC:** dipendenze native, risoluzione delle versioni, precisione e typing ai confini. POC-ENV deve provare installazione senza compilazione locale delle librerie geografiche nelle piattaforme obbligatorie, XML/PBF equivalenti, trasformazione e scambio JSON. Nessuna installazione viene eseguita in questo aggiornamento del PRD.

### 7.8 DT-06 — Provider e acquisizione OSM

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| `.osm` locale | Ispezionabile, fixture semplici, offline. | Più voluminoso, XML non fidato. | **Obbligatorio.** |
| `.osm.pbf` locale | Compatto e adatto a estratti riutilizzabili. | Parser binario, decompressione e riferimenti da validare. | **Obbligatorio**, coperto dallo stesso parser. |
| Overpass configurabile | Selezione per area/highway senza database locale. | Policy, disponibilità e risposte incomplete del servizio. | **Primo provider remoto.** |
| Downloader di estratti Geofabrik | Snapshot regionali ripetibili. | Download spesso molto più grande dell'area MVP; gestione preestrazione. | Estratti ammessi tramite file locale; nessun downloader dedicato. |
| OSM API principale o database planet | API generale oppure pieno controllo dei dati. | Nessun vantaggio per questo flusso o costo operativo eccessivo. | Non adottati per l'acquisizione MVP. |

Geofabrik pubblica effettivamente estratti OSM: se superano i limiti, l'utente deve prepararne una porzione prima di usare il tool. [Geofabrik Download Server](https://download.geofabrik.de/).

**Scelta adottata:** `--input` è sempre offline; `--bbox` senza `--input` usa Overpass. L'endpoint deve essere esplicito tramite `--overpass-url` o variabile `OSM2ETS2_OVERPASS_URL`, con precedenza al flag. Non esiste fallback hardcoded. In assenza di endpoint il comando online termina con `invalid_input` e spiega la configurazione richiesta. Gli esempi originari presuppongono l'endpoint configurato; non si cambia il loro significato.

Un endpoint pubblico, ad esempio quello documentato da Overpass, è una scelta dell'utente soggetta alla policy dell'istanza, non un backend garantito dal progetto. Uso occasionale su comando esplicito, nessun polling o CI contro istanze pubbliche. Nessuna rotazione di server per aggirare limiti. [Overpass: uso delle risorse comuni](https://dev.overpass-api.de/overpass-doc/en/preface/commons.html).

**Contratto del provider:** riceve bbox, budget e configurazione della fonte; restituisce snapshot XML/PBF standard, hash, endpoint/provider, momento di acquisizione, timestamp OSM quando disponibile, versione della query, bbox richiesta e ambito di copertura. Il parser riceve un file, non una risposta HTTP o oggetti specifici di Overpass. Questo confine consente un futuro provider diverso senza cambiare modelli o exporter.

**Strategia Overpass adottata:** selezionare direttamente le way con tag `highway` che intersecano la bbox; includere tutti i nodi referenziati; produrre XML con ID, riferimenti e tag; ritagliare solo localmente. Non usare selezione iniziale dei soli nodi interni, né limitare l'output geometrico o la ricorsione alla bbox.

**Evidenza che chiude il dubbio precedente:** il manuale dell'autore di Overpass documenta che la selezione diretta delle way include anche gli attraversamenti senza nodi interni. Documenta inoltre come recuperare i nodi referenziati. Non serve inventare una garanzia basata su un buffer arbitrario. [Overpass: geometrie complete](https://dev.overpass-api.de/overpass-doc/en/full_data/osm_types.html). La bbox usata per limitare l'output può invece omettere coordinate: non è il formato dello snapshot da conservare. [Overpass: bounding box](https://dev.overpass-api.de/overpass-doc/en/full_data/bbox.html).

La query include tutte le classi highway per rendicontare quelle escluse. Non scarica ricorsivamente relazioni o dati non stradali. Il report deve segnalarli come **non acquisiti**, non come assenti o pari a zero. L'interpretazione di restrizioni di svolta rimane fuori scope.

**Completezza:** il tool valida tutti i riferimenti necessari e la strategia del provider; non certifica che OSM rappresenti ogni strada reale. Per un file locale arbitrario, l'assenza di intere way non è deducibile dal contenuto: la copertura resta dichiarata dalla fonte o `unknown`. Questo non impedisce la conversione di uno snapshot strutturalmente valido, ma impedisce di presentarlo come censimento completo dell'area. Per Overpass, un errore di recupero o una risposta parziale invalida la build.

**Rischi/PoC:** differenze dell'istanza, XML formalmente leggibile con messaggio di errore e snapshot troncati. POC-OSM deve validare la strategia sull'endpoint scelto; timeout, quota e integrità non sono dimostrati dalla sola documentazione. Tutti i budget adottati sono in DT-08.

### 7.9 DT-07 — Coordinate, origine, scala e precisione

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| Moltiplicazione diretta di gradi o Web Mercator | Formula semplice e comune nelle mappe web. | Gradi non metrici o distorsione dipendente dalla latitudine; base inadatta a distanze geometriche locali uniformi. | Non adottata. |
| UTM della zona dell'area | CRS metrico standard, strumenti diffusi. | Selezione della zona, confini di zona e fattore di scala da gestire. | Valida alternativa, non necessaria per il MVP locale. |
| AEQD ellissoidale centrata sull'area | Origine naturale locale, direzioni e distanze dal centro, nessuna scelta di zona UTM. | CRS specifico per ogni progetto; non equivale a conservare esattamente tutte le distanze fra coppie di punti. | **Adottata.** |
| ECEF → ENU topocentrico | Base utile a geometrie 3D e quote future. | Richiede assunzioni verticali non necessarie al MVP planare. | Rinviata con terrain/elevation. |

PROJ documenta AEQD con forma ellissoidale, origine configurabile e coordinate proiettate. UTM e topocentrico restano alternative reali, non capacità già adottate. [PROJ: AEQD](https://proj.org/en/stable/operations/projections/aeqd.html), [UTM](https://proj.org/en/stable/operations/projections/utm.html), [topocentrico](https://proj.org/en/stable/operations/conversions/topocentric.html).

**Contratto numerico adottato:**

1. Coordinate sorgente **EPSG:4326/WGS84**, input sempre in ordine longitudine/latitudine tramite `always_xy=True`; non si confonde l'ordine EPSG con quello della CLI.
2. Se c'è bbox, origine `lon0=(west+east)/2`, `lat0=(south+north)/2`, uguale online e offline. Senza bbox, usare il centro della bbox delle way candidate, prima delle esclusioni ETS2. In assenza di candidate si produce `empty` senza inventare un'origine.
3. CRS metrico locale **AEQD ellissoidale WGS84**, centro `(lon0,lat0)`, falsi est/nord zero e unità metri. Definizione completa WKT2/PROJJSON e versioni PROJ/pyproj nel manifest; nessun codice EPSG inventato per il CRS locale.
4. Il modello mappa usa **E, N, H**: est, nord e quota di riferimento in metri della scena. Per il piano MVP `H=0` significa quota convenzionale, non altitudine reale. La scala `s` si applica una volta: `E=s·e`, `N=s·n`; il default è `s=1`.
5. Fra nodi consecutivi si adotta l'interpolazione lineare nel piano longitudine/latitudine della sorgente. Il ritaglio avviene su questi segmenti contro la bbox WGS84 **prima** della proiezione metrica, nella fase di trasformazione. I tratti risultanti vengono densificati prima/durante la proiezione quanto serve a limitare lo scostamento dalla curva proiettata a **1 cm metrico prima dello scaling**. Non si proiettano soltanto estremi lontani per poi unirli con una retta, né si sostituisce la bbox con quella dei quattro angoli proiettati. I punti sintetici conservano la way, il segmento sorgente e il parametro di interpolazione; non creano connessioni per sola coincidenza. Segmenti che attraversano l'antimeridiano sono fuori dal dominio del profilo e devono essere diagnosticati, non interpretati come linee attraverso tutto il mondo.
6. Core, geometrie, scaling e IR usano **float64**; ID e topologia non dipendono da uguaglianze approssimate. JSON rifiuta NaN/infinito e non arrotonda le coordinate per finalità di presentazione. Dopo la corrispondenza degli assi, soltanto l'adapter converte le coordinate scena nel `Vector3` float32 richiesto da TruckLib e poi nella rappresentazione Q256 descritta sotto. Input, output ed errore di ciascuno stadio devono restare distinti.
7. Le trasformazioni non richiedono download di griglie: rete PROJ disabilitata; errori o coordinate non finite danno errore, non fallback silenziosi.

Il comportamento di `always_xy` e il controllo degli errori sono documentati da [pyproj Transformer](https://pyproj4.github.io/pyproj/stable/api/transformer.html).

**Assunzione ETS2 ancora aperta:** l'aritmetica dell'adapter
`X=E`, `Y=H`, `Z=-N`, con origine nativa zero, è stata verificata
automaticamente, ma la sua correttezza geografica e visuale nel Map Editor non
lo è. Il target Windows deve confermare o respingere segni, direzioni e
orientamento con fixture asimmetriche e distanze note. La prova Q256 non
dimostra la semantica degli assi. Non si tratta di una trasformazione verso la
mappa base `europe`; la convenzione resta confinata all'adapter.

#### Revisione DT-07 del 2 settembre 2026 — precisione Q256 dell'adapter selezionato

**Requisito precedente e causa della revisione.** PoC-002 v1 è `FAIL` sotto il
criterio originale congelato «errore aggiunto dalla conversione numerica nativa
massimo 0,001 m della scena». Il massimo osservato è
**0,004277268693810707 m**. Il risultato storico e tutte le sue misure restano
in [poc-002-results.md](poc-002-results.md); non vengono rivalutati con i nuovi
criteri. La
[RCA Q256](../spikes/poc-002-coordinate-geometry/evidence/native-q256-rca.md)
ha dimostrato che TruckLib 0.5.1 serializza
`TruckLib.ScsMap.Node.Position` con
`(int)(Position.<axis> * 256f)` e rilegge dividendo per `256f`. Per input
float32 finiti e nel dominio MVP, il cast tronca verso zero. Pacchetto,
SourceLink e tag risolvono al commit upstream
`bd745344fc52d3b2d70ce9ac7c88d61b99934805`, registrato nella RCA. Il `FAIL`
ha quindi causato questa revisione; non viene fatto scomparire dalla revisione.
Per le esecuzioni successive, il singolo criterio combinato è sostituito dai
criteri per stadio sotto elencati; per PoC-002 v1 resta il criterio storico con
cui il run è stato giudicato.

**Ambito dell'evidenza.** La regola è provata esclusivamente per
**TruckLib 0.5.1** e **`TruckLib.ScsMap.Node.Position`** nel percorso esercitato.
Non dimostra la rappresentazione di ogni coordinata ETS2, di altri campi,
writer o versioni, né il comportamento interno del Map Editor. È comunque un
vincolo applicabile all'architettura MVP perché DT-02 seleziona esattamente
questo adapter e questa versione.

| Alternativa | Valutazione | Esito |
| --- | --- | --- |
| Conservare il requisito generale di 1 mm cambiando writer o rappresentazione | Richiederebbe riaprire DT-02 e ripetere le prove native. Non esiste evidenza che un altro writer o percorso nativo eviti Q256. | Non scelta per il MVP; resta investigabile solo con nuova evidenza. |
| Allineare preventivamente la geometria alla griglia Q256 | Renderebbe esatto il writer rispetto a un input già quantizzato, ma anticiperebbe una modifica geometrica esplicita e non renderebbe vero il confronto originale con il float64 neutro. | Non scelta. |
| Modellare la quantizzazione Q256 come stadio deterministico esplicito | Corrisponde all'implementazione selezionata, conserva neutro il modello E/N/H float64 e offre un oracolo intero esatto invece di una tolleranza arbitraria. | **Adottata per il MVP.** |

**Pipeline numerica vincolante dopo la revisione:**

```text
WGS84
  → AEQD float64
  → geometria e discretizzazione float64
  → scaling float64
  → modello neutro E/N/H float64
  → mapping degli assi e Vector3 float32
  → serializzazione deterministica Q256 di Node.Position
  → persistenza ETS2 Map Editor, verificata separatamente
```

**Criteri di precisione adottati per il futuro rerun:**

| Stadio | Criterio obbligatorio |
| --- | --- |
| A. WGS84 ↔ AEQD | Errore geodetico massimo **0,001 m** sui controlli indipendenti. |
| B. Discretizzazione proiettata | Scostamento massimo **0,01 m prima dello scaling**. |
| C. float64 → float32 nell'adapter | Errore euclideo 3D aggiunto massimo **0,001 m della scena**, misurato prima di Q256. |
| D. Q256 TruckLib 0.5.1 | Per ogni componente float32 finita `f_a` di X, Y e Z, `expected_q_a = trunc_toward_zero(f_a * 256f)`. L'`Int32` serializzato e il codice ricostruito dal readback devono coincidere **esattamente** con `expected_q_a`; `expected_native_a = expected_q_a / 256f`. Non si usa una tolleranza floating sostitutiva. |
| E. Geometria nativa dei rettifili | Distanza di Hausdorff simmetrica massima **1,0 m della scena**, come in DT-04, misurata indipendentemente dai controlli numerici per stadio. |

La scala resta uniforme e applicata una sola volta in float64 prima del confine
float32/Q256. I rapporti fra `s=1` e `s=0,1` si valutano nello stadio scena
float64; la quantizzazione successiva non può essere reinterpretata come scala
non uniforme né assorbita nel suo budget.

Con `Δ = 1/256 m = 0,00390625 m`, la perdita deterministica del solo stadio
Q256 rispetto all'input float32 ha i seguenti estremi superiori:

| Componente della perdita Q256 | Limite matematico |
| --- | ---: |
| Per asse | `< 1/256 m` |
| Piano orizzontale X/Z | `< sqrt(2)/256 m` (`< 0,005524271728019903 m`) |
| Spazio 3D | `< sqrt(3)/256 m` (`< 0,0067658234670659265 m`) |

Questi limiti descrivono la perdita della rappresentazione selezionata; non
sono una tolleranza discrezionale da sommare agli altri budget o da usare per
accettare un codice errato.

**Persistenza Map Editor separata.** Prima dell'editor si congelano identità
dei nodi e codici Q256 X/Y/Z attesi ed effettivi. Dopo
**Map → Recompute map → Save → chiusura completa → riapertura**, per ogni nodo
deve valere `q_after = q_before = q_expected` componente per componente. Ogni
delta intero diverso da zero fallisce il criterio di persistenza e richiede
indagine; non è concesso un secondo intervallo Q256 a ogni salvataggio. Il
readback TruckLib è diagnostico e non sostituisce il ciclo editor. Nessuna
misura post-editor è ancora disponibile.

**Conseguenze della decisione.** Il report deve conservare il valore float32,
il codice Q256 atteso/effettivo e la perdita rappresentativa per ogni asse o
una riconciliazione completa equivalente. Un cambio di TruckLib, writer o
rappresentazione riapre DT-02/DT-07 e richiede nuova evidenza. PoC-002 doveva
essere rieseguito integralmente con questi criteri: la parte automatica del
rerun del 3 settembre 2026 è `PASS`, ma il ciclo Windows resta in attesa. Il
gate PoC-002 non è superato e PoC-003 resta bloccato.

**Scala adottata:** valore finito `s>0`, con default `1`; nessuna promessa che ogni scala sia esportabile. L'estensione nativa deve rispettare DT-08 e i vincoli dei modelli devono essere controllati dopo scaling. Strade/prefab non vengono rimpiccioliti come asset; una riduzione eccessiva può produrre casi non supportati. `NormalScale`/`CityScale` o altri metadati del gioco non sono usati al posto della trasformazione delle coordinate; i loro valori coerenti con il progetto autonomo restano un dettaglio del PoC nativo.

**Aree grandi:** rifiutare aree oltre DT-08 prima di costruire il modello completo, indipendentemente dalla scala scelta. Niente elaborazione automatica per tile, cambio di CRS o gestione dell'antimeridiano nel MVP. Future aree estese richiederanno strategia di partizionamento e continuità; non basta ridurre la scala.

### 7.10 DT-08 — Limiti operativi e gestione dei dati

| Alternativa | Vantaggi | Svantaggi e rischi | Esito |
| --- | --- | --- | --- |
| Solo limite di area | Semplice da comunicare. | Non limita densità OSM, file sovradimensionati o rettangoli lunghissimi. | Insufficiente. |
| Solo limite di file | Semplice da controllare prima del parsing. | Compressione PBF e complessità geometrica non sono proporzionali ai byte. | Insufficiente. |
| Area + diagonale + byte + conteggi | Controlli progressivi e dimensioni del lavoro prevedibili. | Più soglie da testare; non garantisce da solo tempi o memoria. | **Adottato.** |
| Adattamento automatico/tiling senza limiti | Maggiore copertura. | Complessità e tempi non controllati per questo MVP. | Rinviato. |

**Profilo di ammissione `mvp-small-v1`: valori adottati come politica prudenziale, non capacità già misurata.** MiB indica 1.048.576 byte.

| Misura | Limite iniziale e criterio |
| --- | --- |
| Area da generare | **25 km²**, area della bbox richiesta oppure della bbox delle geometrie candidate se manca il filtro; misura geodetica su WGS84. |
| Diagonale | **10 km**, massima distanza geodetica fra gli angoli della stessa bbox. Entrambi i vincoli devono essere rispettati. |
| Dominio geografico | Bbox senza antimeridiano, latitudini comprese fra **−80° e +80°**, coordinate finite; nessun supporto polare nel MVP. |
| Estensione dopo scaling | Tutti i punti del modello e dell'output nativo entro **10.000 m** dall'origine nel piano; oltre il limite la scala è non ammissibile. |
| File locale XML/PBF | **256 MiB** sul disco; sorgenti regionali più grandi devono essere preestratte esternamente. |
| Nodi OSM osservati | **1.000.000 per passaggio di parsing**, conteggio degli elementi letti, senza sommare artificialmente passaggi ripetuti. |
| Way osservate | **100.000 per passaggio**, incluse quelle escluse dalla selezione. |
| Relazioni osservate | **50.000 per passaggio** per file che le contengono; non implica che il provider remoto le acquisisca. |
| Nodi distinti delle way candidate | **100.000**, compresi i riferimenti esterni necessari a completare le geometrie. |
| Way candidate | **20.000 prima del ritaglio**, secondo la tabella §4.2. |
| Riferimenti a nodi | **10.000 per singola way**, **1.000.000 complessivi nelle candidate**, per limitare ripetizioni/complessità non visibili nei nodi distinti. |
| Tratti del grafo ritagliato | **50.000** dopo suddivisione, prima di eventuale espansione in elementi nativi. |
| Elementi nativi generati | **100.000**; il serializer non può espandere una geometria senza un budget finito. |
| Esecuzione locale | **600 s** dalla fine dell'acquisizione alla pubblicazione; terminazione controllata di elaborazione/adapter, editor manuale escluso. Nessuno SLA implicito entro quel tempo. |

I vincoli di scala e area sono indipendenti: un'area fuori limite non diventa ammessa riducendola nella scena. I riferimenti esterni necessari alla bbox consumano budget anche se non saranno esportati. I file piccoli ma malformati non sono considerati sicuri solo perché rispettano il limite di byte.

**Budget remoti adottati:**

| Misura | Limite |
| --- | --- |
| Concorrenza | **1 richiesta attiva** per acquisizione. |
| Tentativi | **3 complessivi**, solo per errori temporanei; mai per query invalide o input non supportato. |
| Risorse richieste a Overpass | Timeout server **60 s**, memoria richiesta **128 MiB**; il server può rifiutare e non offre una prenotazione garantita. |
| Connessione / lettura HTTP | **10 s** connessione; **75 s** massimo di inattività in lettura. |
| Durata singolo tentativo | **90 s totali**, oltre ai timeout di inattività; la durata totale va controllata esplicitamente. |
| Acquisizione complessiva | **360 s**, inclusi backoff e `Retry-After`; se l'attesa richiesta eccede il budget, terminare senza aggirarla. |
| Risposta | **64 MiB dopo decompressione HTTP**, controllati durante lo streaming, non solo tramite `Content-Length`. |

**Esiti adottati:** superamento delle soglie dimensionali, di conteggio o dominio, bbox/scala/profilo invalidi o dati locali strutturalmente incompleti → `invalid_input`; scadenze temporali, compreso il tetto locale di 600 s, superamento dei byte della risposta remota, rifiuto di risorse del servizio, risposta remota incompleta, fallimento dell'adapter o errore del formato nativo → `failed`. Un superamento non produce una rete troncata né `partial`. Elementi validi ma fuori dal supporto dichiarato possono produrre `partial`; nessuna strada esportabile da dati validi produce `empty`. La classificazione si applica prima dei contatori di successo e mantiene §6.3.

La tabella highway di §4.2 è **adottata**, senza estendere ora la copertura. Alias direzionali accettati: `yes/true/1`, `no/false/0`, `-1`; valori reversibili, alternati e condizionali non sono trasformati in direzioni certe. Le implicazioni motorway/roundabout restano subordinate al valore esplicito. [OSM: oneway](https://wiki.openstreetmap.org/wiki/Key:oneway).

**Rischi/PoC:** queste soglie non provano il picco RAM. POC-LIMITS deve misurarlo e verificare anche limiti sui blocchi decompressi e sui tag del singolo oggetto forniti dal parser nativo, senza inventare garanzie delle librerie. Se i tetti non risultano sostenibili, si abbassano con una nuova revisione del profilo e test; non si disabilitano per far superare la dimostrazione. Non è previsto un flag `ignore-limits`.

### 7.11 Vincoli trasversali, verifica ed estensioni

**Sicurezza e integrità:** input non fidati, nessuna risoluzione di entità XML esterne, nessun accesso di rete dal parser o da PROJ. Tag e nomi OSM non diventano percorsi o comandi. Il core non invoca una shell per il processo C#. File nativi prodotti solo in area temporanea isolata, cancellazione limitata a quella build e nessuna sovrascrittura di input, archivi ETS2 o mappe rifinite. Credenziali del provider non devono essere copiate in report o log condivisibili.

**Licenze:** mantenere distinta la licenza del codice dalla provenienza OSM e dai diritti sugli asset. Inserire attribuzione OSM, link alla licenza e metadati della fonte. Non assumere che ogni output sia un Produced Work esente da altri obblighi sui dati; le modalità di distribuzione vanno valutate sul risultato effettivo. [OSM: copyright e licenza](https://www.openstreetmap.org/copyright), [OSMF: attribuzione](https://osmfoundation.org/wiki/Licence/Attribution_Guidelines).

Le dipendenze scelte sono progetti open-source; l'inventario di distribuzione deve includere librerie native e avvisi transitivi, non soltanto i wrapper Python. TruckLib dichiara GPL v2; Shapely distribuisce anche GEOS con proprie condizioni. Nessun asset proprietario ETS2 entra nel repository, nelle fixture pubbliche o nel pacchetto distribuibile. Gli asset del PoC sono letti dall'installazione dell'utente e identificati nel verbale, senza copiarli nella specifica.

**Matrice minima delle prove:**

| Area | Casi da coprire |
| --- | --- |
| Input/CLI | XML/PBF equivalenti, ID grandi, ordine diverso, input troncato, nodo mancante, bbox occidentale negativa, percorsi con spazi, endpoint non configurato. |
| Topologia | T/X con nodo condiviso, attraversamento senza nodo comune, anelli, archi paralleli, componenti separate e nodi distinti coincidenti. |
| Direzione/verticalità | `oneway=-1`, eccezioni alle implicazioni, layer differenti su estremità connesse, ponte/tunnel non appiattito, rotatoria conservata ma non dichiarata esportata. |
| Confini/coordinate | Way lunga con estremi esterni, entrata/uscita, tangenza al bordo, clipping WGS84 e densificazione della curva proiettata, controllo est/nord e distanze indipendenti, replay asimmetrico. |
| Modello/adapter | IR priva di token nativi, schema sconosciuto, numeri non finiti, errori del processo, input invariato, corrispondenza degli ID e dei collegamenti; budget float64 → float32; codici Q256 esatti su X/Y/Z separatamente per positivi, negativi, zero e adiacenze ai bordi, alle scale 1 e 0,1; limiti teorici riportati. |
| Geometria/raccordi | T e 4-vie entro e fuori profilo; curve oltre tolleranza, asset mancanti, sovrapposizione di raccordi, deformazioni non ammesse. |
| Risorse/report | Ogni soglia, decompressione, timeout, conversione parziale in area, clipping fuori area senza falso `partial`, conteggi `not acquired`, output atomico. |
| Editor | Elementi editabili, connessioni native dopo ricalcolo/salvataggio/riapertura, log, fixture sintetica e area reale; assi/orientamento visuali; uguaglianza esatta dei codici Q256 X/Y/Z pre/post-editor per ogni nodo identificato. |

Non è richiesto un browser per il collaudo della CLI. Se manca un ambiente ETS2 idoneo, il controllo manuale resta non eseguito; la documentazione e un parser binario non lo sostituiscono.

**Estensibilità:** edifici/landuse, elevazione, junction avanzate, segnaletica, POI, città, aree estese, aggiornamenti incrementali e packaging completo restano milestone successive. La scelta vincolante è mantenere provenienza, schema dell'IR e confini sostituibili; non introdurre ora database, framework plugin generici, API web o infrastrutture distribuite.

## 8. Metriche di successo e condizioni di consegna

Il criterio principale è: **su una piccola area OSM reale, un comando di conversione produce una rete stradale riconoscibile e connessa per la parte supportata, apribile, modificabile, salvabile e riapribile nel Map Editor senza ricostruzione manuale della geometria e dei raccordi dichiarati convertiti.**

| Misura | Condizione di successo |
| --- | --- |
| Compatibilità | G0 superato e ripetuto sul profilo/versione distribuiti. |
| Percorso utente | US-012 completata su snapshot reale fissato e documentato; modalità bbox verificata separatamente. |
| Topologia supportata | Nessuna adiacenza sorgente supportata persa e nessuna adiacenza spuria introdotta; controllo sia sul grafo sia sull'output nativo. |
| Geometria | Punti di controllo, orientamento e scala corretti; budget geografico, discretizzazione e float32 rispettati separatamente; codici Q256 esatti secondo DT-07; scostamenti entro DT-04, senza rettifiche manuali delle parti dichiarate riuscite. |
| Copertura | Ogni way candidata ha un esito riconciliabile; nessuna omissione o conversione parziale non dichiarata. |
| Riproducibilità | Stessi dati/configurazione/versioni producono lo stesso contenuto semantico verificato. |
| Integrità | Input e progetti esistenti restano invariati, anche nei casi di errore coperti. |
| Verifica | Ogni criterio di accettazione ha evidenza automatica o manuale esplicita; i controlli non eseguiti non sono considerati superati. |

Il campione di accettazione deve contenere un sottografo con catena curva, T e incrocio semplice a quattro bracci interamente supportati, senza raccordi irrisolti in quel sottografo. Le altre parti dell'area possono risultare `partial` se motivate. Non è consentito ridefinire dopo il collaudo il sottoinsieme supportato per nascondere errori dell'esportatore.

Durata, memoria, numero di elementi e quota di rete esportata saranno misurati sul campione, riportando macchina e versioni. Non sono definiti KPI commerciali o promesse quantitative di prestazione non supportati da misure. Questi valori servono a verificare la sostenibilità dei limiti già adottati in DT-08 e a motivarne eventuali revisioni esplicite.

## 9. Chiusura delle decisioni e verifiche ancora aperte

Le decisioni **DT-01–DT-08 sono chiuse come scelte progettuali**, con DT-07
revisionata il 2 settembre 2026: versione target, output, architettura,
perimetro dei raccordi, stack, provider, coordinate e limiti non sono
alternative lasciate all'implementatore. Gli spike aggiunti non costituiscono
implementazione del prodotto e G0 resta da completare.

| Esecuzione | Criteri | Stato | Effetto sul gate |
| --- | --- | --- | --- |
| PoC-001 | Baseline nativa minimale congelata | `PASSED` | Consente di preparare PoC-002 sulla stessa baseline. |
| PoC-002 v1 | Criteri originali congelati prima del run del 1 settembre 2026 | **`FAIL`** | Risultato storico preservato; ha causato la revisione DT-07. |
| PoC-002 revised rerun | Modello di precisione revisionato in §7.9 | **`AWAITING_MANUAL_VALIDATION`** (`PASS` automatico) | Il gate PoC-002 non è superato finché manca il ciclo Windows. |
| PoC-003 / PoC-004 | Gate successivi | **`NOT_EXECUTED`** | Restano bloccati dal mancato `PASS` del rerun PoC-002. |

| Elemento residuo | Natura | Evidenza che lo chiude |
| --- | --- | --- |
| Build completa ETS2 1.60.x e catalogo base | Dato sperimentale, non versione da inventare | POC-ETS2 con versione installata, profilo, asset e log; nessuna promessa su tutte le patch. |
| Set minimo di file nativi e metadati del progetto | Dettaglio di formato non sufficientemente dimostrato | Progetto generato da TruckLib 0.5.1 apribile, ricomputabile e persistente dopo riapertura. |
| Semantica degli assi e orientamento | Assunzione ancora aperta; l'aritmetica `X=E, Y=H, Z=-N` ha superato il rerun automatico ma non prova il significato geografico visuale | Ciclo Windows PoC-002 completo sulle fixture asimmetriche. |
| Persistenza Q256 nel Map Editor | Nessun risultato post-editor disponibile; la RCA non descrive il comportamento interno dell'editor | Uguaglianza esatta dei codici `q_after = q_before = q_expected` dopo recompute/save/chiusura/riapertura. |
| Identificatori/varianti T e 4-vie, compatibilità road/prefab | Spike tecnico necessario | POC-JUNCTION con soli asset base e rispetto dei limiti DT-04. Nessun token del sample upstream è assunto valido. |
| Lock completo, patch SDK e dipendenze transitive | Chiusura riproducibile dell'ambiente scelto | POC-ENV sulle piattaforme adottate; le versioni dirette già scelte, compresa uv, restano quelle di DT-05. |
| Integrazione Overpass e comportamento dell'istanza scelta | Verifica di un contratto documentato | POC-OSM con attraversamenti, errori, completezza e replay; nessuna garanzia di disponibilità del servizio pubblico. |
| Sostenibilità delle soglie numeriche | Misura operativa, non KPI inventato | POC-LIMITS con picco RAM, tempi, contatori, macchina e superamento controllato delle soglie. |
| Snapshot reale di dimostrazione | Preparazione del collaudo | Scegliere una piccola area di guida a destra contenente il sottografo richiesto da §8, fissando bbox, hash, data, grafo atteso e casi fuori scope prima del collaudo. Nessuna acquisizione effettuata ora. |

Nessuna di queste verifiche può essere marcata come superata sulla base della sola ricerca documentale. Un eventuale fallimento di formato, disponibilità dei prefab o compatibilità richiede una correzione o una revisione esplicita della decisione; non autorizza ad allentare il criterio principale di successo.

### 9.1 Tracciabilità delle decisioni

| Decisione | Requisiti e storie interessati |
| --- | --- |
| DT-01 | FR-25, FR-31, FR-39; US-008, US-012. |
| DT-02 | FR-27, FR-28, FR-30, FR-35; US-008, US-011. |
| DT-03 | FR-9, FR-18, FR-33, FR-38; US-005, US-006, US-008, US-011. |
| DT-04 | FR-12, FR-15, FR-21–FR-25, FR-28–FR-29; US-007, US-008, US-009. |
| DT-05 | FR-1, FR-4, FR-18, FR-32, FR-36, FR-39; tutte le storie per qualità e ambiente. |
| DT-06 | FR-1, FR-3–FR-6, FR-13, FR-16, FR-37; US-001–US-004, US-011. |
| DT-07 | FR-3, FR-13, FR-18–FR-22, FR-27, FR-30–FR-31, FR-36, FR-38; US-005, US-006, US-008, US-011. |
| DT-08 | FR-2, FR-6–FR-8, FR-12, FR-17, FR-22, FR-34–FR-35; US-001–US-005, US-010–US-012. |

## 10. Impatto sul repository

I percorsi esistenti probabilmente interessati dalla futura implementazione sono:

| Percorso verificato | Impatto atteso |
| --- | --- |
| `README.md` | Installazione, CLI, prerequisiti, esempio reale, compatibilità e limiti del MVP. |
| `.gitignore` | Eventuale adattamento allo stack adottato e agli artefatti generati; il file iniziale non costituisce evidenza di uno stack già implementato. |
| `LICENSE` | Riferimento da preservare per la licenza del progetto; nessuna modifica prevista da questo PRD. |
| `tasks/prd-osm2ets2-mvp.md` | Specifica del perimetro e riferimento per le verifiche di consegna. |

Il codice, le fixture, i test, i manifest e la CI del **prodotto** dovranno
essere introdotti durante l'implementazione; gli artefatti già presenti sotto
`spikes/` restano esperimenti isolati e non ne definiscono la collocazione. Non
sono necessari migrazioni, API web, autenticazione o database per soddisfare
il perimetro attuale.

La revisione del 2 settembre 2026 modifica la decisione DT-07 e la relativa
documentazione PoC-002. Il rerun automatico del 3 settembre applica la
decisione senza cambiarla e senza modificare codice di prodotto o evidenze
numeriche storiche. Non acquisisce dati OSM e non esegue il Map Editor.
