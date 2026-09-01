# osm2ets2 MVP — Piano dei technical spike

Data: **31 agosto 2026**. Stato: **pianificato; nessun PoC eseguito**.

Fonte canonica: [PRD dell'MVP](prd-osm2ets2-mvp.md), in particolare §7, decisioni DT-01–DT-08, gate G0 e verifiche residue di §9. Questo documento scompone alcune verifiche del PRD in esperimenti; non modifica requisiti, soglie o decisioni adottate e non costituisce una dimostrazione di fattibilità.

La consegna attuale è esclusivamente questo piano. Non prevede codice applicativo o di PoC, installazioni, download OSM, apertura del gioco o esecuzione degli esperimenti. Gli input, i programmi temporanei e le evidenze descritti sotto sono artefatti da preparare in una successiva attività autorizzata, non file già esistenti.

## 1. Ordine, dipendenze e rischio

| Ordine | Spike | Assunzione principale | Rischio se falsa | Gate precedente | Stato |
| --- | --- | --- | --- | --- | --- |
| 1 | PoC-001 — ETS2 Native Output Feasibility | TruckLib produce una mappa nativa utilizzabile e persistente nel Map Editor target. | Invalida il percorso di output dell'intero progetto. | Nessuno. | `NOT_EXECUTED` |
| 2 | PoC-002 — Coordinate and Geometry Validation | Proiezione, scala e conversione nativa rispettano orientamento e precisione richiesti. | Richiede revisione della trasformazione, del profilo o dei limiti. | PoC-001 `PASS`. | `NOT_EXECUTED` |
| 3 | PoC-003 — Simple Road Topology | Strade, catene, T e quattro vie hanno connessioni native automatiche e persistenti. | Invalida il supporto minimo ai raccordi e può richiedere un diverso exporter. | PoC-001 e PoC-002 `PASS`. | `NOT_EXECUTED` |
| 4 | PoC-004 — Minimal End-to-End OSM Conversion | Una piccola rete reale attraversa i livelli separati senza perdere geometria, semantica o adiacenze. | Richiede revisione dei contratti interni, della normalizzazione o del mapping. | PoC-001, PoC-002 e PoC-003 `PASS`. | `NOT_EXECUTED` |

La sequenza elimina prima il rischio di formato, poi rende affidabili le misure necessarie a valutare i raccordi, infine introduce OSM e il confine Python/C#. La fattibilità delle intersezioni resta un rischio architetturale elevato: PoC-002 deve restare limitato alle fixture indicate, senza diventare sviluppo del motore geografico completo.

Gli esperimenti sono indipendenti negli input e nei risultati osservabili: PoC-001 non richiede Python; PoC-002 usa punti sintetici, non OSM; PoC-003 usa topologie sintetiche, non il parser; PoC-004 integra soltanto capacità già dimostrate. L'indipendenza non elimina i gate. Si riusano fixture e procedure collaudate, senza costruire anticipatamente framework o componenti di produzione.

## 2. Regole comuni di esecuzione e decisione

### 2.1 Stati del gate

| Stato | Significato | Conseguenza |
| --- | --- | --- |
| `NOT_EXECUTED` | Piano non ancora eseguito. | Nessuna assunzione validata. |
| `PASS` | Tutti i criteri obbligatori superati, con evidenze riproducibili sulla baseline registrata. | Rimuove il blocco tecnico al PoC successivo; non avvia automaticamente attività. |
| `FAIL` | Una prova completata contraddice un criterio obbligatorio o l'assunzione verificata. | Fermare la sequenza; diagnosticare, correggere o riesaminare la decisione. |
| `BLOCKED` | Mancano ambiente, asset accessibili, riferimento attendibile o strumenti di misura sufficienti a concludere. | Fermare la sequenza; registrare cosa manca. Non dedurre che il formato sia impossibile. |

Non esistono `PASS` condizionati o ottenuti con soli file generati. Una prova numerica non sostituisce il collaudo editor; una schermata non sostituisce la misura numerica. Il verbale può riportare controlli parzialmente riusciti, ma il gate resta non superato finché manca un criterio obbligatorio. Questi stati appartengono agli spike e non sostituiscono gli esiti CLI `success`, `partial`, `empty`, `invalid_input`, `failed` del PRD.

In caso di fallimento, il verbale deve separare difetto dell'esperimento, difetto correggibile dell'adapter, incompatibilità della libreria e ipotesi architetturale smentita. Una correzione locale può portare alla ripetizione dello stesso PoC; se occorre cambiare versione, formato, schema, profilo, tolleranze o supporto obbligatorio, documentare alternative e conseguenze e revisionare esplicitamente la decisione prima di proseguire. Non sono ammessi fallback silenziosi, collegamenti manuali o riduzioni retroattive del campione dichiarato supportato.

**PoC-001 è il gate obbligatorio dell'intero progetto.** Se `.mbd` e settori prodotti da TruckLib non sono concretamente utilizzabili nella build ETS2 target, non avviare PoC-002–004 né lo sviluppo dell'MVP sulla base di un output alternativo non verificato.

### 2.2 Baseline e isolamento

- Ereditare DT-01/DT-02: **ETS2 1.60.x stabile**, profilo `ets2-1.60-native-v1`, **Windows 11 x64**, gioco base senza DLC obbligatori, **TruckLib 0.5.1** e **C#/.NET 10**, processo framework-dependent. Registrare build completa del gioco, versione del formato effettivamente osservata, patch SDK/runtime, versioni transitive e impronta del catalogo usato. Non inventare patch, token o file richiesti.
- Introdurre Python soltanto da PoC-002, nelle parti necessarie. Ereditare DT-05: **CPython 3.14.7 standard GIL**, **uv 0.12.7**, **pyproj 3.7.2**, **Shapely 2.1.2**; **osmium 4.3.1** soltanto per PoC-004. Non occorre attivare HTTPX, Overpass, CLI finale o l'intera infrastruttura di sviluppo per queste prove.
- Usare directory nuove e isolate per ciascuna esecuzione, senza scrivere su mappe personali o output rifiniti. Conservare una copia immutata dei file generati prima di usare l'editor. Non distribuire asset del gioco nei risultati pubblici.
- Congelare input, risultati attesi e criteri prima dell'esecuzione. Registrare revisione e hash del PRD e del piano usati, senza modificare la fonte canonica durante una prova.
- Un cambio di build ETS2, TruckLib o catalogo installato del gioco invalida l'applicabilità delle prove native precedenti; un cambio di trasformazione richiede almeno PoC-002 e i successivi; un cambio del sottoinsieme di raccordi approvati richiede PoC-003 e PoC-004; un cambio di contratto JSON richiede PoC-004. Documentare dipendenze e prove da ripetere, senza trasferire automaticamente un `PASS` a una nuova combinazione.

### 2.3 Evidenze e ciclo editor comune

Ogni verbale dovrà contenere:

1. ID del PoC e dell'esecuzione, data, operatore, macchina, stato del gate e baseline completa.
2. Input e configurazioni con hash; valori attesi, provenienza dei riferimenti e metodo di misura.
3. Comandi effettivamente usati e procedura editor ripetibile; non riportare comandi ipotetici come già verificati.
4. Inventario dei file generati e delle copie dopo il salvataggio: percorsi relativi, dimensioni, hash, ruolo osservato e riferimenti fra elementi.
5. Risultati per criterio, misure con unità e incertezza, log del programma e del gioco/editor, immagini contestualizzate. Separare controlli automatici, manuali e non eseguiti.
6. Differenze semantiche prima/dopo editor, avvisi motivati, errori, limiti osservati e decisione finale. Timestamp, ordine fisico o hash diversi non dimostrano da soli una differenza semantica.
7. In caso di `FAIL` o `BLOCKED`: riproduzione minima, causa nota o ignota, decisioni del PRD interessate, alternative e condizione necessaria per riprendere.

Il ciclo editor obbligatorio è: **aprire l'output generato → ispezionare elementi e riferimenti → Map → Recompute map → salvare → chiudere completamente l'editor → riaprire la mappa salvata → ripetere i controlli**. Registrare collocazione dei file, modalità di caricamento e log reali. La lettura di ritorno con TruckLib è diagnostica, non sostitutiva del ciclo.

Non ricreare strade, cambiare asset o collegare manualmente nodi per far superare il campione. Un'eventuale prova di modifica delle proprietà avviene su una copia separata, identificata come tale, per dimostrare l'editabilità. La ricomputazione prevista dal PRD è ammessa; le riparazioni manuali no. Errori di formato, asset mancanti, connessioni perse o geometrie alterate oltre soglia impediscono `PASS`; eventuali avvisi innocui richiedono classificazione e motivazione.

## 3. PoC-001 — ETS2 Native Output Feasibility

### Obiettivo

Dimostrare che un programma C# minimale con TruckLib 0.5.1 può generare una mappa autonoma con una sola strada rettilinea nativa, utilizzabile nella build target attraverso l'intero ciclo editor. Determinare il set di file sufficiente e le dipendenze effettive, prima di introdurre qualsiasi dato geografico.

### Assunzione verificata e collegamenti al PRD

**Assunzione ancora aperta:** il percorso `.mbd` + settori adottato è concretamente scrivibile con TruckLib e accettato dal Map Editor, con identificatori, riferimenti, asset e metadati coerenti.

Riferimenti: [PRD](prd-osm2ets2-mvp.md), **DT-01 (§7.3), DT-02 (§7.4)**, sottoinsieme runtime di **DT-05 (§7.7)**; **FR-25, FR-27, FR-30, FR-31, FR-35**; **US-008 e US-011** nelle sole parti native. Copre l'inizio di **POC-ETS2** e una parte di **POC-ENV** (§7.2); chiude i dubbi su build/catalogo e file/metadati di §9 soltanto se dimostrati. Assi e unità osservati qui restano da validare geograficamente in PoC-002.

### Prerequisiti

- Accesso a Windows 11 x64 e a un'installazione legittima di ETS2 1.60.x stabile con Map Editor utilizzabile e catalogo del gioco base ispezionabile.
- Ambiente C#/.NET 10 con TruckLib 0.5.1 disponibile e fissato nella futura esecuzione; nessun ambiente Python richiesto.
- Directory di prova nuove, modalità di accesso ai log e possibilità di conservare output prima/dopo il ciclo editor.

La sola indisponibilità della build o dell'ambiente dà `BLOCKED`, non prova un'incompatibilità.

### Scope

Una mappa, un elemento stradale rettilineo con i nodi e i riferimenti realmente richiesti, asset base e metadati strettamente necessari. Rilevare identificatore/nome della mappa, ID degli elementi, regole di unicità e collegamento, settorizzazione, definizioni/look stradali, dipendenze, quota di riferimento, assi nativi, orientamento e valori di default rilevanti.

### Fuori scope

OSM, Python, Overpass, proiezioni, scala geografica, JSON intermedio di prodotto, intersezioni, prefab creati appositamente, CLI finale, packaging della mod e mappa giocabile completa. Non introdurre prefab se il rettifilo non li richiede; se emergono dipendenze inattese, documentarle.

### Input

- Specifica sintetica di due estremi distinti, per esempio `A=(100,0,100)` e `B=(200,0,100)` in coordinate native candidate, senza attribuire preventivamente alle unità un significato geografico.
- Definizione stradale del gioco base da individuare e registrare; nessun token upstream assunto valido senza verifica.
- Nome/identificatori e configurazione minima da fissare durante la preparazione; versioni e catalogo della baseline.

Le coordinate sono una proposta di fixture, non un risultato già accettato dal motore. Una loro eventuale modifica per un vincolo nativo osservato va motivata e registrata prima di ripetere il test.

### Procedura

1. Registrare ambiente, build, pacchetto, catalogo e accesso all'editor. Identificare un modello/look stradale base adatto; annotare dipendenze e motivi per cui non richiede DLC.
2. Preparare il più piccolo programma sperimentale C# che crei la mappa e il rettifilo, senza parser o infrastruttura dell'MVP. Esplicitare proprietà necessarie e proprietà lasciate ai default, senza attribuire significati non verificati a metadati proprietari.
3. Generare in una directory vuota. Inventariare `.mbd`, cartella settori e tutti gli altri file effettivamente prodotti; verificare unicità degli ID e integrità dei riferimenti. Conservare il set completo del serializer.
4. Eseguire il ciclo editor comune. Controllare che esista una strada nativa selezionabile, che i due estremi e la geometria corrispondano alla fixture e che il salvataggio non la elimini o la sostituisca con un oggetto estraneo.
5. Registrare assi esposti dall'editor, quota, orientamento/tangenti e metadati prima/dopo il ricalcolo, inclusi eventuali parametri di scala del progetto. Distinguere valori necessari, default osservati e significato non accertato. Se necessario, ripetere con il rettifilo ruotato in una **mappa separata contenente sempre una sola strada**, per distinguere X e Z; niente coordinate geografiche.
6. Confrontare struttura, riferimenti e geometria prima/dopo editor. Distinguere file prodotti da TruckLib, file creati/modificati dall'editor e dipendenze lette dal gioco. Descrivere il **set osservato sufficiente**; marcare come ignota la necessità individuale non verificata di un file. Non dedurre una lista minima dalle sole estensioni e non eliminare file del serializer per supposizione.
7. Rigenerare da zero in una seconda directory, usando la procedura registrata, e ripetere il ciclo editor. Scrivere l'esito e la procedura di apertura riproducibile, includendo eventuali limiti di TruckLib.

### Output atteso

Due esecuzioni ripetibili della stessa mappa minima, con `.mbd`, settori e accessori osservati; inventario prima/dopo, tabella degli identificatori e riferimenti, asset/road definitions, metadati e default rilevanti, assi/orientamenti osservati, istruzioni di caricamento, log e verbale. I valori proprietari non accertati restano esplicitamente ignoti.

### Criteri di successo

- Generazione programmatica riuscita e ripetibile sulla combinazione fissata, senza asset mancanti o DLC obbligatori.
- Entrambe le esecuzioni superano apertura, ricomputazione, salvataggio, chiusura e riapertura; la strada rimane nativa ed editabile, con estremi e riferimenti integri.
- Nessuna costruzione o riparazione manuale della strada è necessaria.
- File sufficienti, collocazione, identificatori, dipendenze e metadati usati sono registrati abbastanza precisamente da ripetere la prova; limiti non accertati non vengono presentati come specifica del formato.

### Criteri di fallimento

Output non caricabile, strada assente/non editabile, asset richiesti non disponibili nel gioco base verificato, riferimenti invalidi, errore di ricomputazione/salvataggio o perdita dopo riapertura. È fallimento anche il funzionamento ottenuto soltanto ricreando manualmente la strada o usando una diversa baseline non dichiarata. Misure o passaggi editor mancanti lasciano il gate `BLOCKED`, non `PASS`.

### Evidenze da raccogliere

Pacchetto comune di §2.3, più inventario completo, mappa degli ID, proprietà native osservabili, elenco delle dipendenze base e confronto dei due cicli. Le schermate devono mostrare selezione e presenza della strada dopo riapertura; i log devono permettere di distinguere problemi del progetto da avvisi dell'ambiente.

### Rischi

Compatibilità dichiarata dalla libreria non sufficiente, formato o metadati incompleti, dipendenze implicite da asset, differenze fra patch, default della libreria e salvataggio editor, pulizia della directory settori da parte del writer. L'isolamento protegge i progetti esistenti, ma non elimina il rischio di incompatibilità.

### Impatto architetturale in caso di fallimento

**Stop dell'intero percorso.** Prima di procedere, documentare quale parte di DT-01/DT-02 è smentita e confrontare almeno le alternative pertinenti già citate nel PRD:

| Alternativa da investigare, non approvata | Possibile beneficio | Rischio / nuova prova necessaria |
| --- | --- | --- |
| Correzione circoscritta dell'adapter o di TruckLib | Può conservare il formato e la separazione adottati. | Comprensione del difetto, manutenzione della correzione e ripetizione integrale di PoC-001. |
| Selezione `.sbd` su mappa ospite | Può offrire un percorso di importazione diverso. | Non dimostra la generazione autonoma richiesta; restano writer, origine, asset e connessioni da provare. |
| Mid-format SCS / Conversion Tools | Percorso con strumenti SCS da approfondire. | Il contratto IR → strade native non è dimostrato; serve uno spike distinto. |
| Altro writer o diversa baseline esplicitamente revisionata | Può rimuovere un'incompatibilità specifica. | Nuovo costo di formato, versionamento e compatibilità; nessuna riuscita presunta. |

JSON, immagini o geometrie esportate in altri formati non sostituiscono il gate nativo.

## 4. PoC-002 — Coordinate and Geometry Validation

### Obiettivo

Validare con pochi punti e segmenti controllati il percorso **WGS84 → AEQD locale → metri locali → scala → coordinate ETS2 → Map Editor**, separando accuratezza numerica, orientamento e adattamento alla geometria nativa.

### Assunzione verificata e collegamenti al PRD

**Assunzioni ancora aperte:** origine e proiezione conservano le proprietà metriche richieste; la scala è applicata una sola volta; la corrispondenza candidata `X=E, Y=H, Z=-N` e la precisione nativa sono compatibili con il profilo.

Riferimenti: [PRD](prd-osm2ets2-mvp.md), **DT-07 (§7.9)**, confini di **DT-03**, tolleranze di **DT-04** e dominio di **DT-08**; **FR-3, FR-13, FR-18–FR-22, FR-31, FR-38**; **US-006** e parte geometrica di **US-008**. Copre la porzione coordinate di **POC-ETS2**, parte di **POC-ENV** e il dubbio su assi/unità/precisione di §9; non è **POC-LIMITS**.

### Prerequisiti

PoC-001 `PASS`, stessa baseline nativa, strada minima e procedura editor ripetibili. Ambiente geografico minimo di DT-05 disponibile nella futura esecuzione, rete PROJ disabilitata. Metodo di riferimento indipendente e strumenti capaci di misurare gli errori richiesti, incluse le coordinate native dopo salvataggio. La sola precisione visualizzata dall'interfaccia non è sufficiente.

### Scope

Punti geografici congelati, piccoli segmenti sintetici, origine, assi/segni, unità, rotazioni, scala, clipping e precisione. Riutilizzare solo le primitive native dimostrate da PoC-001; la rappresentazione completa di curve e raccordi è rinviata a PoC-003.

### Fuori scope

OSM e relativo parser, Overpass, grafo stradale generale, intersezioni, terreno, quote reali, allineamento alla mappa base `europe`, tiling e misure di carico. Non sviluppare la CLI finale o un sistema generale di importazione.

### Input

Preparare una tabella WGS84 in ordine **longitudine, latitudine**, con valori attesi, fonte e metodo indipendenti dalla trasformazione sotto test. Nessuna coordinata o misura attesa è dichiarata già verificata in questo piano.

| Fixture limitata | Scopo |
| --- | --- |
| Origine O e cinque controlli asimmetrici a est, nord, ovest, sud e obliqui | Distanze radiali diverse, per esempio dell'ordine di 100–500 m, per rilevare scambio di assi, riflessione, segno e rotazione. |
| Stessi controlli a scala `1` e `0.1` | Verificare scala unica e uniforme, mantenendo segmenti ammissibili per il modello nativo. |
| Offset di 0,001 m, 0,01 m e 0,1 m vicino all'origine, prima dello scaling | Verificare arrotondamenti e cancellazione numerica; usare estremi traslati di strade di lunghezza già accettata, non strade lunghe un millimetro. |
| Quattro angoli e un controllo interno di una bbox prossima a 25 km² | Verificare il bordo dell'area mantenendo diagonale ≤10 km. |
| Quattro angoli e un controllo interno di una bbox allungata prossima a 10 km di diagonale | Verificare questo limite separatamente, mantenendo area ≤25 km². |
| Una variante scalata con punto nativo prossimo, ma interno, al raggio di 10.000 m | Misurare precisione nativa lontano dall'origine; la bbox geografica deve restare ammessa. |
| Un segmento WGS84 con entrambi gli estremi esterni che attraversa la bbox | Verificare clipping prima della proiezione e discretizzazione della curva proiettata. |

Calcolare area e diagonale geodetiche prima di accettare le fixture di bordo; approssimazioni dimensionali non sono prove di ammissibilità. Per rendere misurabile la copertura, scegliere ciascuna fixture di bordo fra il **95% e il 99% del vincolo studiato**, mantenendo gli altri entro limite: è una regola di selezione dell'esperimento, non una modifica di DT-08. Le fixture geografiche restano nel dominio DT-08, senza antimeridiano e con latitudini fra −80° e +80°. Una disposizione specchiata con longitudine o latitudine negativa può essere usata come controllo numerico aggiuntivo, senza ampliare la mappa.

### Procedura

1. Congelare i punti con riferimento indipendente, bbox, origine attesa e distanze/azimut attesi. AEQD conserva le distanze geodetiche dal centro: non imporre erroneamente uguaglianza esatta per tutte le distanze fra punti esterni. Specificare per ogni confronto se la distanza è geodetica, proiettata o di scena.
2. Verificare separatamente origine dal centro della bbox esplicita e origine dall'estensione delle geometrie candidate quando manca bbox. Le esclusioni successive del mapping non devono spostare l'origine.
3. Trasformare con AEQD ellissoidale WGS84, falsi est/nord nulli, `always_xy=True`, float64 e rete PROJ disabilitata. Registrare definizione completa del CRS, versioni, coordinate locali e ritorno geografico.
4. Per il segmento attraversante, ritagliare i segmenti lineari in longitudine/latitudine prima della proiezione, registrare estremi sintetici e parametro sorgente e densificare entro il budget DT-07. Non usare soltanto la retta fra gli estremi proiettati.
5. Applicare `E=s·e`, `N=s·n`, `H=0` una sola volta. Confrontare lunghezze e rapporti a scala `1` e `0.1`; verificare che metadati come `NormalScale`/`CityScale` non siano usati in sostituzione della trasformazione geometrica.
6. Inviare coordinate neutre all'esperimento C# e applicare nell'adapter l'ipotesi `X=E, Y=H, Z=-N`. Generare pochi rettifili separati nelle direzioni di controllo, senza introdurre nodi condivisi o intersezioni. Verificare verso e rotazioni anche con un segmento obliquo.
7. Eseguire il ciclo editor sui casi nativi, misurando posizioni e orientamenti prima e dopo il salvataggio. Usare lettura numerica dei dati salvati o altra misura verificabile con risoluzione adeguata; documentare limiti e dipendenza del lettore dal writer.
8. Confrontare ogni fase con il riferimento e il budget pertinente. Ripetere i controlli di origine e bordo; registrare separatamente errore di proiezione/ritorno, discretizzazione, scala e conversione nativa. Non sommare o confondere questi errori con l'accuratezza del rilievo OSM.

### Output atteso

Tabella dei punti a ogni stadio, definizione AEQD, origine e scale, convenzione degli assi dimostrata o smentita, procedura di misura, errori e incertezze, piccolo output nativo per le fixture e verbale del ciclo editor. Il segmento ritagliato include la provenienza degli estremi sintetici. Non è richiesto uno schema completo dell'IR di prodotto.

### Criteri di successo

| Controllo | Soglia o risultato richiesto |
| --- | --- |
| Riferimento indipendente | Origine, direzioni e distanze concordano con valori congelati; differenze numeriche e distorsione prevista dalla proiezione sono esplicite. Un ritorno con la propria inversa da solo non basta. |
| Andata/ritorno geografico | Errore massimo **0,001 m**, DT-07. |
| Discretizzazione proiettata | Scostamento massimo **0,01 m prima dello scaling**, DT-07. |
| Conversione numerica nativa | Errore aggiunto massimo **0,001 m della scena**, anche dopo il ciclo editor. |
| Scala, assi e orientamento | Rapporti coerenti con `s` entro i budget numerici; nessuna applicazione doppia, riflessione o rotazione inattesa. Conferma sperimentale della convenzione candidata. |
| Geometria nativa dei rettifili | Scostamento entro **1,0 m della scena** secondo la metrica DT-04, distinto dall'errore numerico; nessuna correzione manuale. |
| Origine e dominio | Origine deterministica; punti finiti; area, diagonale e raggio nativo rispettati e misurati separatamente. |

Per ogni disuguaglianza, l'incertezza della misura deve rientrare nel budget e non essere usata per aumentarlo. L'orientamento deve essere riconoscibile nell'editor e verificato numericamente; una schermata non prova precisione millimetrica.

### Criteri di fallimento

Assi/segni/unità diversi dall'ipotesi, scala applicata due volte, origine instabile, attraversamento perso, errore superiore al budget, coordinate non finite o alterazioni dopo salvataggio. Se il metodo di misura non può distinguere il rispetto della soglia, l'esito è `BLOCKED`. Un caso deliberatamente fuori dominio è un controllo negativo, non un fallimento della trasformazione se viene rifiutato correttamente.

### Evidenze da raccogliere

Pacchetto comune, tabella WGS84/AEQD/scena/nativo prima e dopo editor, fonte dei punti attesi, distanze/azimut, parametri CRS, errori massimi e metodo di stima, immagini orientate e provenienza del clipping. Conservare dati numerici non arrotondati per presentazione.

### Rischi

Ordine latitudine/longitudine, confusione fra nord e asse Z, precisione del formato o del lettore, riferimenti circolari, distorsione AEQD scambiata per errore, scala che rende troppo piccoli i segmenti o eccessiva l'estensione. Non esiste nel PRD una lunghezza minima ETS2 universale da assumere.

### Impatto architetturale in caso di fallimento

Una diversa convenzione nativa richiede revisione esplicita della corrispondenza nel solo adapter, lasciando E/N/H nel modello neutro, e ripetizione del PoC. Errori di origine, clipping o proiezione riaprono DT-07 nella trasformazione. Precisione nativa incompatibile richiede riesame di rappresentazione, origine o limiti DT-07/DT-08; non aumentare tacitamente le tolleranze. Perdita al salvataggio può riaprire PoC-001. Nessun avvio di PoC-003 finché il gate non è superato.

## 5. PoC-003 — Simple Road Topology

### Obiettivo

Determinare come rappresentare e collegare automaticamente i cinque casi minimi nell'output ETS2, provando connessioni native persistenti e compatibilità road/prefab entro le tolleranze del PRD.

### Assunzione verificata e collegamenti al PRD

**Assunzione ancora aperta:** il gioco base e TruckLib permettono rettifili, curve, continuità, T e quattro vie semplici senza creare asset o richiedere collegamenti manuali.

Riferimenti: [PRD](prd-osm2ets2-mvp.md), **DT-04 (§7.6)**, **DT-01/DT-02** per baseline e formato, **DT-07** per coordinate già validate; **FR-21–FR-23, FR-25, FR-28, FR-29**; **US-008 e US-009** nelle parti esercitate. Copre **POC-JUNCTION**, curve/catene residue di **POC-ETS2** e identificatori/varianti/porte di §9. Non certifica tutte le categorie stradali né l'intero catalogo di mapping.

### Prerequisiti

PoC-001 e PoC-002 `PASS`, baseline invariata, misure geometriche disponibili. Accesso al catalogo base per identificare prefab T e quattro vie, relativi connettori e road definitions. La verifica dell'esistenza di asset idonei è parte dell'ipotesi, non una compatibilità data per acquisita.

### Scope

Cinque fixture sintetiche, preferibilmente in mappe separate per isolare i guasti:

| Caso | Risultato da dimostrare |
| --- | --- |
| Rettifilo | Road nativa e terminali integri; controllo positivo ereditato da PoC-001. |
| Curva | Forma e tangenti rappresentabili entro soglia, senza inversioni del verso. |
| Catena di almeno tre segmenti, con parte curva | Continuità di grado 2 mediante collegamenti nativi, non semplice coincidenza degli estremi. |
| T a tre bracci | Tutti i bracci collegati alle porte corrette del raccordo, con persistenza. |
| Quattro vie semplice | Tutti e quattro i bracci collegati, senza omissioni, inversioni o adiacenze spurie. |

T e quattro vie sono a raso, singola carreggiata, bidirezionali con una corsia per direzione, guida a destra e geometria compatibile con il catalogo DT-04. Usare etichette e lunghezze dei bracci asimmetriche per rilevare scambi di porte e rotazioni. Determinare se ciascun caso richiede prefab, nodi speciali, snapping programmatico o altre regole; non presupporre la risposta.

### Fuori scope

OSM, parser e proiezione da ricostruire, roundabout e mini-roundabout, svincoli multilivello, ponti/tunnel, generazione di prefab, semafori/traffico, junction con bracci a senso unico, corsie asimmetriche o spartitraffico. Il supporto alle catene a senso unico del PRD resta una verifica separata se non viene esercitato qui; non è rimosso dal perimetro MVP.

### Input

Geometrie locali sintetiche e tabella delle adiacenze attese per le cinque fixture, congelate prima dell'export; convenzione nativa validata; asset base da individuare. Per ogni prefab registrare token, variante, porte, sistemi locali, ingombro, dipendenze e compatibilità con il tipo di strada. I token effettivi non sono inventati in questo piano.

### Procedura

1. Congelare geometrie e collegamenti attesi; riusare il rettifilo come controllo dell'ambiente. Introdurre nell'esperimento C# soltanto i tipi nativi necessari alla fixture successiva.
2. Provare curva e catena. Rilevare tangenti, nodi condivisi/riferimenti richiesti, limiti osservati di lunghezza e curvatura. Non attribuire un limite universale al motore sulla base di un solo modello.
3. Individuare un prefab T e uno a quattro vie del gioco base e controllarne dipendenze e porte. Un asset presente in un esempio upstream non costituisce prova di disponibilità nella baseline.
4. Per ciascuna junction, scegliere una posa rigida del prefab e associare ogni braccio a una porta. Registrare nodi speciali, snapping e regole effettivamente necessari. Effettuare automaticamente gli eventuali tagli/adattamenti degli approcci consentiti; non deformare il prefab.
5. Generare ogni fixture e rilevare la corrispondenza **braccio atteso → porta → strada/nodo nativo**. Verificare riferimenti e direzioni prima dell'editor.
6. Eseguire il ciclo editor su tutte e cinque le fixture. Ricontrollare ogni collegamento dopo la riapertura, oltre alla geometria. Un numero uguale di componenti o una sovrapposizione visiva non provano l'identità delle adiacenze.
7. Misurare curve e approcci con il metodo DT-04. Delimitare le regioni di adattamento prima di valutare il risultato; riportare ingombro del prefab, tagli, tratti residui e disallineamento delle tangenti.
8. Aggiungere controlli negativi circoscritti: un braccio visivamente coincidente ma non collegato deve essere rilevato dalla verifica; un token indisponibile non deve apparire valido. Una posa fuori soglia o con approcci sovrapposti deve essere riconosciuta come non ammissibile, senza aumentare le tolleranze.

### Output atteso

Cinque campioni nativi, tabella di rappresentazione per caso, catalogo minimo T/quattro vie verificato con porte e dipendenze, regole necessarie di collegamento, misure di adattamento e verbali prima/dopo editor. Registrare anche ciò che TruckLib non espone o non gestisce correttamente, senza trasformarlo in una promessa di supporto.

### Criteri di successo

- **Tutte e cinque** le fixture superano il ciclo editor; T e quattro vie sono entrambe obbligatorie.
- Zero connessioni attese perse e zero connessioni spurie; corrispondenza dimostrata per ogni porta/braccio. Nessuna tolleranza topologica e nessun collegamento manuale.
- Asset del gioco base sufficienti, senza prefab creati ad hoc o DLC obbligatori.
- Distanza di Hausdorff simmetrica fra curve corrispondenti **≤1,0 m della scena** sui tratti ordinari e **≤2,0 m** nei soli approcci dichiarati; errore di misura/campionamento **≤0,01 m**, incluso nel confronto con la soglia.
- Differenza fra tangente dell'approccio e direzione richiesta dalla porta **≤10°**, tenendo conto del verso di connessione. Prefab posato rigidamente; regioni di approccio non sovrapposte; tratti residui compatibili con i limiti osservati del modello nativo.
- La misura degli approcci riguarda le parti esterne all'ingombro del prefab. La geometria interna è quella dell'asset, non la riproduzione puntiforme del nodo sorgente.
- I controlli negativi rilevano il difetto intenzionale e non vengono contati come conversioni riuscite.

### Criteri di fallimento

Uno dei cinque casi obbligatori non rappresentabile, assenza accertata di prefab base idonei, collegamento automatico impossibile, perdita dopo salvataggio, adiacenze spurie, superamento delle soglie o necessità di ricostruzione manuale. Il successo della T non compensa il fallimento delle quattro vie. Il rifiuto corretto di un controllo fuori profilo non è fallimento; un caso obbligatorio fallito non può essere riclassificato retroattivamente come fuori profilo.

### Evidenze da raccogliere

Pacchetto comune, scheda degli asset, diagramma/tabella delle adiacenze attese e native, matrice braccio/porta, coordinate e tangenti, regioni di approccio, misure Hausdorff con margine, riferimenti prima/dopo editor, immagini delle cinque fixture e risultati negativi. Distinguere regole confermate nell'esperimento da generalizzazioni ancora da provare.

### Rischi

Prefab non compatibili con road definitions, porte orientate diversamente dall'atteso, rigidità dell'ingombro, spazio insufficiente dopo scaling, curve native poco fedeli, limiti della libreria sui prefab e falsi positivi di connessione visiva. La disponibilità di un asset non ne dimostra l'utilizzabilità automatica.

### Impatto architetturale in caso di fallimento

Riaprire DT-04 e, se necessario, DT-02: correggere il collegamento nell'adapter, investigare asset base alternativi o rivalutare il writer con una nuova prova. Creare prefab procedurali o ridurre l'MVP a strade scollegate cambia esplicitamente il perimetro e non è una soluzione approvata. Se la persistenza nativa generale è smentita, riaprire PoC-001. PoC-004 resta fermo.

## 6. PoC-004 — Minimal End-to-End OSM Conversion

### Obiettivo

Dimostrare, su una porzione reale estremamente piccola, la pipeline **`.osm` → parsing → normalizzazione → road graph → trasformazione → JSON indipendente da ETS2 → processo C# → TruckLib → `.mbd` + settori → Map Editor**. Verificare che la separazione dei livelli funzioni concretamente, senza costruire l'MVP completo.

### Assunzione verificata e collegamenti al PRD

**Assunzioni ancora aperte:** il parser e il modello normalizzato preservano le informazioni necessarie; il contratto neutro è sufficiente all'adapter; il mapping già dimostrato su fixture sintetiche rappresenta anche una piccola rete reale senza perdere adiacenze.

Riferimenti: [PRD](prd-osm2ets2-mvp.md), **DT-03 (§7.5)**, **DT-02/DT-04/DT-05/DT-06/DT-07**, entro **DT-08**. Requisiti principali: **FR-4, FR-6, FR-8–FR-11, FR-17–FR-23, FR-25, FR-27–FR-31, FR-33, FR-35–FR-39**. Storie: **US-002, US-004–US-012**, esclusivamente nei casi esercitati. È integrazione parziale di **POC-ENV**, riuso delle evidenze **POC-ETS2/POC-JUNCTION** e anticipazione di una parte di **US-012/FR-39**; non chiude G0 né il collaudo finale di §8.

### Prerequisiti

PoC-001, PoC-002 e PoC-003 `PASS`, con baseline ancora applicabile. Ambiente Python minimo DT-05, compreso osmium, e processo C# separato disponibili nella futura esecuzione. Catalogo raccordi e procedure di misura già validati. Snapshot `.osm` reale completo per le way candidate, con provenienza e topologia attesa controllabili.

### Scope

Un solo snapshot XML locale, poche strade supportate, una catena con curva e almeno una T o una semplice quattro vie nel profilo DT-04. Come budget della fixture, scegliere preferibilmente **≤0,25 km², ≤20 way candidate e ≤500 nodi riferiti**. Sono obiettivi organizzativi dello spike, non nuovi limiti di prodotto; restano validi tutti i limiti DT-08.

Selezione delle highway presenti nel campione, risoluzione dei riferimenti, grafo con provenienza e adiacenze, trasformazione già validata, JSON versionato neutro, mapping separato e generazione nativa. Solo l'orchestrazione temporanea necessaria a collegare le fasi; nessuna CLI finale.

### Fuori scope

PBF ed equivalenza XML/PBF, acquisizione remota/Overpass, copertura di tutte le categorie OSM, restrizioni per il traffico, roundabout, raccordi complessi, terrain, edifici, packaging, prestazioni ai limiti, matrice multipiattaforma completa, robustezza generale della CLI e infrastruttura CI. Non rendere il prototipo un'implementazione di produzione.

### Input

- Un file `.osm` reale scelto e congelato **prima** della conversione, con bbox/estensione, provenienza, data se disponibile, hash, attribuzione OSM, classi, ID e conteggi. Non inventare ora località, ID o misure.
- Un riferimento indipendente ottenuto dall'ispezione dello snapshot: sequenze delle way, nodi condivisi, terminali, adiacenze, versi e componenti; elenco delle strade dichiarate supportate e degli eventuali oggetti esclusi con motivo.
- Origine e scala ammissibili già verificate, profilo/versioni e mapping separato basato sugli asset di PoC-003.
- Copie derivate distinte per i controlli negativi; l'originale reale resta immutato.

La preparazione dello snapshot è separata dall'esecuzione offline: questo spike non valida il provider usato per ottenerlo. Non modificare la sorgente reale per fabbricare il raccordo desiderato e non restringere il campione dopo il test per nascondere errori.

### Procedura

1. Ispezionare e congelare lo snapshot e il grafo atteso. Accertare che il sottografo da convertire rientri nel catalogo dimostrato; classificare eventuali esclusioni prima dell'esecuzione.
2. Eseguire offline il parsing XML. Copiare i dati degli oggetti streaming nel modello geografico; risolvere i riferimenti senza assumere un ordine favorevole del file. Conservare ID, geometrie e tag necessari, senza usare la sola coincidenza spaziale per costruire connessioni.
3. Normalizzare e costruire il grafo: suddividere le way ai nodi condivisi necessari, preservare punti di forma e versi presenti nel campione e confrontare adiacenze/componenti con il riferimento indipendente. Conservare il risultato in `network.json` secondo il ruolo definito dal PRD.
4. Applicare la trasformazione validata e produrre `map-model.json`: schema versionato, ID stringa, numeri finiti, E/N/H in metri della scena, geometria, connessioni, semantica stradale e provenienza. Nessun token SCS, settore, ID nativo, `.ppd` o tipo TruckLib nel modello neutro. Non trasformare il JSON in una copia del formato ETS2.
5. Congelare il JSON e passarlo come file a un processo C# separato, insieme al profilo/mapping separato. Il processo C# non deve rileggere `.osm`; verificare che possa riesportare dal JSON già prodotto senza eseguire il parser Python. Registrare le corrispondenze native nel report, senza riscrivere il modello neutro con dettagli ETS2.
6. Generare il set nativo determinato da PoC-001 e riconciliare way, tratti, raccordi e riferimenti nativi. Eseguire il ciclo editor e verificare ogni adiacenza attesa del sottografo supportato, geometria, orientamento e approcci entro i budget già validati.
7. Ripetere la conversione in una nuova destinazione con stessi dati, configurazioni e versioni. Confrontare il contenuto semantico degli intermedi e dei risultati nativi; esplicitare l'esclusione di timestamp, durate e percorsi. Verificare che l'hash dell'input sia invariato.
8. Su una copia separata, eliminare un nodo necessario a una way candidata: il parsing/controllo di integrità deve rilevare input incompleto e non consegnare una mappa pronta. Su una copia del JSON, impostare uno schema sconosciuto: il processo C# deve rifiutarlo chiaramente senza un export dichiarato riuscito. Non serve implementare in questo spike l'intera tassonomia degli errori della CLI.

### Output atteso

Snapshot originale conservato, grafo atteso e `network.json`, `map-model.json` neutro, configurazione del profilo separata, corrispondenza sorgente/nativo, `.mbd` con settori/accessori, inventario, report sperimentale e istruzioni editor realmente usate. Due risultati confrontabili semanticamente e verbali dei controlli negativi. Questi artefatti non costituiscono ancora la consegna completa di §6.2 del PRD.

### Criteri di successo

- Tutte le strade e connessioni del sottografo preregistrato come supportato attraversano la pipeline; **zero adiacenze perse e zero adiacenze spurie**, nel grafo e dopo riapertura nativa.
- Curva/catena e almeno un raccordo semplice sono riconoscibili, editabili e connessi senza ricostruzione manuale; geometrie, precisione e approcci rispettano DT-04/DT-07.
- Ogni elemento candidato ha una corrispondenza o un'esclusione motivata; conteggi per way, tratti e oggetti nativi riconciliabili, senza pretendere che i numeri di nodi OSM e nativi coincidano.
- Confine Python/C# esercitato mediante JSON versionato e neutro; parser e trasformazione non dipendono da TruckLib o da token del catalogo. Riesportazione dal solo JSON riuscita con profilo separato.
- Due conversioni semanticamente equivalenti; input immutato, provenienza e attribuzione presenti, nessuna dipendenza da download durante l'esecuzione.
- Controlli negativi rifiutati nel confine pertinente, con diagnostica e senza output falsamente pronto. I difetti del convertitore non sono conteggiati come `partial`.

### Criteri di fallimento

Perdita di riferimenti o geometria, adiacenze errate, necessità di leggere OSM nell'adapter, dettagli nativi obbligatori nell'IR, omissioni non rendicontate, divergenza semantica fra ripetizioni, input alterato, mappa non persistente oppure controlli negativi presentati come riusciti. La mancanza di uno snapshot verificabile o dell'ambiente dà `BLOCKED`; non autorizza a sostituire l'area reale con una fixture sintetica e dichiarare il gate superato.

### Evidenze da raccogliere

Pacchetto comune, provenienza e hash OSM, grafo atteso indipendente, intermedi di ogni fase, verifica della neutralità JSON, input/output del processo C#, tabella sorgente/tratto/porta/elemento nativo, misure e immagini dopo riapertura, confronto delle ripetizioni e diagnostiche negative. Durata e conteggi possono essere registrati, ma non sono benchmark dei limiti MVP.

### Rischi

Oggetti streaming trattenuti oltre la loro validità, ordine XML assunto implicitamente, differenze tra dati reali e fixture, perdita di informazione nel JSON, normalizzazione che cambia la topologia, mapping troppo ristretto, omissioni mascherate da conteggi aggregati e non determinismo. Un unico campione prova la fattibilità di quel percorso, non la copertura generale.

### Impatto architetturale in caso di fallimento

Perdita tra parsing e grafo richiede revisione della normalizzazione; informazione insufficiente al confine JSON riapre DT-03; dati reali entro il profilo ma non rappresentabili richiedono riesame del mapping/DT-04, senza introdurre dettagli ETS2 nel core come scorciatoia. Persistenza nativa smentita riapre PoC-001; misure incoerenti riaprono PoC-002; raccordi supportati persi riaprono PoC-003. Non estendere l'area o avviare l'MVP completo prima della diagnosi.

## 7. Copertura del PRD e verifiche che restano aperte

I nuovi ID non rinominano né cancellano i gate canonici. Il completamento dei quattro spike produce evidenze da collegare a G0, non una dichiarazione automatica di MVP utilizzabile.

| Gate/requisito canonico | Evidenza pianificata qui | Residuo da mantenere aperto |
| --- | --- | --- |
| POC-ETS2 | PoC-001: formato/rettifilo/ciclo editor; PoC-002: coordinate; PoC-003: curve/catene. | Consolidare i risultati sulla stessa build, catalogo e profilo effettivamente distribuiti; ripetere dopo modifiche pertinenti. |
| POC-JUNCTION | PoC-003: T e quattro vie con asset base e soglie DT-04. | Nessuna generalizzazione a varianti o classi non provate; revalidare il catalogo finale. |
| POC-ENV | Ambiente C# minimo, trasformazione, XML e contratto JSON, incluso schema sconosciuto. | Equivalenza XML/PBF, lock completo e matrice Windows/Linux/macOS, installazione senza compilazione locale delle librerie geografiche, timeout/crash dell'adapter e resto dei controlli ambientali. |
| POC-OSM | Nessuna verifica remota; PoC-004 usa soltanto un file XML locale. | Overpass configurabile, casi di attraversamento del provider, completezza/errori e replay offline richiesti dal PRD. |
| POC-LIMITS | PoC-002 misura pochi controlli vicino ai limiti geometrici. | Carichi rappresentativi, memoria/tempi, densità, input compressi e superamento controllato di ogni limite DT-08. Pochi punti al bordo non dimostrano sostenibilità operativa. |
| Mapping e direzionalità completi | Solo asset, categorie e versi realmente presenti nelle fixture. | Catene a senso unico, altri casi `oneway`, carreggiate distinte, fallback e configurazioni non esercitate; nessuna rimozione dei requisiti FR-12/FR-23/FR-24. |
| US-012 / §8, collaudo finale | PoC-004 dimostra integrazione reale con almeno un raccordo. | Il campione finale deve contenere nello stesso sottografo supportato **catena curva, T e quattro vie**, oltre a percorso CLI e modalità bbox verificata separatamente. Un solo raccordo non chiude questo requisito. |
| Prodotto completo | Fattibilità del percorso e confini architetturali. | CLI, artefatti completi, tassonomia degli esiti, protezione/pubblicazione dell'output, robustezza e prove richieste dal resto del PRD. |

Questi residui non sono ulteriori PoC da eseguire implicitamente con questa consegna. Andranno pianificati dopo le evidenze dei quattro gate, mantenendo il PRD come fonte canonica.

## 8. Criterio di chiusura della sequenza

La sequenza potrà essere dichiarata completata soltanto con quattro verbali `PASS` applicabili alla stessa baseline, evidenze accessibili e un elenco esplicito delle verifiche residue. Eventuali assunzioni smentite devono avere una decisione revisionata e nuove prove, non una nota che ne ignora l'impatto.

**Stato alla consegna del documento: PoC-001, PoC-002, PoC-003 e PoC-004 tutti `NOT_EXECUTED`. Nessuna compatibilità sperimentale dichiarata, nessun codice o dipendenza introdotti.**
