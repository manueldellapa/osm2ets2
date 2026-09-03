# PoC-002 — Specifica congelata del rerun revisionato Q256

**ID criteri:** `poc-002-q256-rerun-v2`

**Data congelamento:** 2 settembre 2026

**Stato esecuzione:** `NOT_EXECUTED`

**Stato gate PoC-002:** non superato

Questa specifica prepara un futuro rerun completo di PoC-002 dopo la revisione
DT-07. Non è un risultato di test e non rivaluta l'esecuzione originale:
**PoC-002 v1 resta `FAIL`** sotto il criterio nativo congelato di 0,001 m. Il
rerun qui definito non è stato avviato e non autorizza PoC-003.

## Autorità, baseline e ambito della decisione

La fonte canonica è DT-07 in
[`tasks/prd-osm2ets2-mvp.md`](../../tasks/prd-osm2ets2-mvp.md); il piano del
gate è in
[`tasks/spikes-osm2ets2-mvp.md`](../../tasks/spikes-osm2ets2-mvp.md). Il
verbale v1 e la RCA restano rispettivamente
[`tasks/poc-002-results.md`](../../tasks/poc-002-results.md) e
[`evidence/native-q256-rca.md`](evidence/native-q256-rca.md).

Baseline obbligatoria invariata:

- ETS2 1.60.x stabile, build sperimentale 1.60.1.7, Map Editor su Windows 11
  x64;
- TruckLib 0.5.1 esatto, commit
  `bd745344fc52d3b2d70ce9ac7c88d61b99934805`, formato nativo 907;
- .NET SDK 10.0.400/runtime 10.0.11 per l'ambiente già risolto;
- CPython 3.14.7 standard GIL, uv 0.12.7, pyproj 3.7.2,
  Shapely 2.1.2, rete PROJ disabilitata.

La regola Q256 è provata per **TruckLib 0.5.1** e
**`TruckLib.ScsMap.Node.Position`** con valori float32 finiti e nel dominio del
PoC. Non è assunta per altri campi ETS2, altre versioni o writer, né come
descrizione interna del Map Editor. Un cambio della baseline richiede una nuova
decisione e nuove prove, non una sostituzione silenziosa.

## Input congelati da riusare

Il rerun deve riusare senza modifica le classi e i valori geografici v1:

| File | SHA-256 v1 congelato |
| --- | --- |
| `fixtures/frozen-fixtures.json` | `3df7f774af4b7a9e6b420871e0fc9a3115c3673964f22dea405e46a53ff43f4b` |
| `fixtures/independent-reference.json` | `3ed376bcda2f8819cd5dd461f569d641e2ea19787bfc7219a9c9c7b67e166a9c` |
| `reference/freeze_reference.py` | `17691ebcb230385a5a575d2032b45f4dda422032abe3a2d219bfb4a9ac395517` |

La copertura obbligatoria comprende:

- origine O esplicita e origine deterministica derivata senza bbox, scelta
  prima delle esclusioni successive;
- controlli asimmetrici est, nord, ovest, sud e obliquo;
- scale `1` e `0.1`;
- offset pre-scaling `0.001`, `0.01` e `0.1 m` come traslazioni di Road valide,
  mai come Road millimetriche;
- bbox prossima al limite di 25 km² e bbox allungata prossima alla diagonale di
  10 km, con i vincoli indipendenti ancora rispettati;
- punto scalato interno e vicino al raggio nativo di 10.000 m;
- segmento WGS84 con estremi esterni che attraversa la bbox, clipping prima
  della proiezione, provenienza/parametro degli estremi sintetici e
  densificazione misurata;
- serializzazione JSON deterministica e rifiuto di numeri non finiti.

Prima di eseguire il rerun si devono registrare in una directory nuova: hash
correnti di PRD/piano/specifica, hash degli input, ambiente, ID run, valori
attesi e metodo di misura. Non si sovrascrivono `output/run-automatic/`, il
manifest `native-final`, la RCA o qualunque evidenza v1.

## Pipeline da misurare

```text
WGS84 lon/lat
  → AEQD WGS84 float64
  → clipping/densificazione e geometria float64
  → E=s·e, N=s·n, H=0 in float64
  → JSON neutro E/N/H float64
  → mapping adapter X=E, Y=H, Z=-N
  → Vector3 float32
  → Q256 di TruckLib.ScsMap.Node.Position
  → ETS2 Map Editor persistence
```

Ogni manifest deve conservare input, output, errore e unità di ciascuno stadio.
Non si sommano massimi provenienti da punti diversi e non si assorbe una perdita
nella tolleranza dello stadio successivo.

## Criteri automatici obbligatori

1. **Ordine e riferimento indipendente.** Verificare longitudine/latitudine,
   origine, segni cardinali, punto obliquo, distanze geodetiche e azimut contro
   i valori congelati. AEQD conserva le distanze geodetiche dal centro; non si
   richiede uguaglianza arbitraria fra punti non centrali.
2. **Round-trip WGS84/AEQD.** Errore geodetico massimo `<= 0.001 m`.
3. **Discretizzazione proiettata.** Deviazione massima `<= 0.01 m` prima dello
   scaling, compreso il controllo di convergenza sul segmento attraversante.
4. **Origine, clipping e dominio.** Origini esplicita/derivata deterministiche;
   clipping WGS84 prima della proiezione; provenienza sintetica intatta;
   area, diagonale e raggio misurati separatamente; coordinate finite.
5. **Scala.** Applicazione esattamente una volta e uniforme. I rapporti fra
   `s=1` e `s=0.1` si confrontano nello stadio E/N/H float64 entro il budget
   numerico già congelato, prima di float32/Q256.
6. **float64 → float32.** Dopo il mapping degli assi e prima di Q256, l'errore
   euclideo 3D aggiunto per punto deve essere `<= 0.001 m` della scena.
7. **Codice Q256 esatto.** Per ogni componente float32 `f_a` effettivamente
   passata a `Node.Position`, con `a ∈ {X,Y,Z}`:

   ```text
   expected_q_a = trunc_toward_zero(f_a * 256f)
   expected_native_a = expected_q_a / 256f
   ```

   L'`Int32` serializzato e il codice ricostruito dal readback devono essere
   esattamente uguali a `expected_q_a`. Non è ammessa una tolleranza floating
   in sostituzione dell'uguaglianza intera.
8. **Caratterizzazione per asse.** Sonde isolate su X, Y e Z devono coprire
   valori positivi, negativi, zero, `±0.001`, `±0.01`, `±0.1` e i float
   immediatamente sotto/sul/sopra bordi Q256 positivi e negativi. Gli altri
   assi restano zero. Le sonde Y non introducono elevazione nelle Road del PoC:
   verificano soltanto il serializer.
9. **Griglia e limiti teorici.** Ogni readback deve avere un codice Q256 intero
   e il report deve pubblicare i massimi osservati insieme ai limiti:

   | Misura del solo stadio Q256 | Limite |
   | --- | ---: |
   | Passo `Δ` | `1/256 m = 0.00390625 m` |
   | Errore per asse rispetto a float32 | `< 1/256 m` |
   | Errore euclideo X/Z | `< sqrt(2)/256 m = 0.005524271728019903 m` |
   | Errore euclideo 3D | `< sqrt(3)/256 m = 0.0067658234670659265 m` |

   Questi limiti sono conseguenze della rappresentazione deterministica, non
   una franchigia aggiuntiva per codici errati.
10. **Rettifili nativi.** Le Road restano indipendenti, senza topologia
    condivisa. La Hausdorff simmetrica rispetto alla geometria scena float64 è
    `<= 1.0 m`; verso e identità degli estremi devono restare integri e non è
    ammessa riparazione manuale.
11. **Confine neutro.** Il JSON resta ETS2-independent, deterministico, finito
    e con E/N/H float64; TruckLib, X/Y/Z e codici Q256 restano nell'adapter e
    nei suoi risultati diagnostici.

L'aritmetica `X=E, Y=H, Z=-N` deve essere controllata automaticamente, ma un
successo automatico non conferma il suo significato geografico nel Map Editor.

## Output ed evidenze automatiche richiesti

Un run nuovo deve produrre almeno:

- manifest Python con ambiente, CRS completo, origini, forward/inverse,
  clipping, provenienza, scale, limiti di dominio e massimi per stadio;
- JSON neutro e relativo hash;
- manifest adapter con, per ogni nodo/asse, E/N/H float64 pertinente, X/Y/Z
  float64 mappato, bit/valore float32, `expected_q`, codice scritto, codice
  riletto, coordinata Q256 e perdita rappresentativa;
- riepilogo distinto dei massimi float64→float32, Q256, Hausdorff e raggio;
- inventario dei file nativi e degli identificatori, senza assumere hash/UID
  binari deterministici;
- esito per ogni criterio e stato complessivo automatico.

Se un solo criterio automatico fallisce, lo stato del rerun è `FAIL` e non si
usa il ciclo editor per compensarlo. Se il metodo non risolve un criterio
obbligatorio, lo stato è `BLOCKED`.

## Gate Windows Map Editor

Solo dopo il superamento di tutti i controlli automatici, lo stato può essere
`AWAITING_MANUAL_VALIDATION`. Per ogni mappa nativa del rerun:

1. conservare immutabile l'output pre-editor e congelare identità dei nodi e
   codici Q256 `q_expected`/`q_before` per X, Y e Z;
2. copiare una working copy nel profilo Windows isolato e aprirla nel Map
   Editor della baseline;
3. ispezionare Road, nodi, posizioni, verso, segni, scala e orientamento;
4. eseguire **Map → Recompute map**;
5. salvare senza spostare o riparare manualmente elementi;
6. chiudere completamente editor e gioco;
7. riaprire la stessa mappa salvata e ripetere l'ispezione;
8. conservare output/log/screenshot dopo editor e svolgere il readback
   numerico diagnostico;
9. confrontare, per identità di nodo e componente,
   `q_after = q_before = q_expected`.

Qualunque delta intero Q256 diverso da zero è deriva aggiuntiva: deve essere
registrato e investigato e impedisce il superamento di questo criterio. Non si
concede un nuovo intervallo `Δ` dopo il salvataggio. Una screenshot supporta
l'orientamento ma non prova la stabilità numerica; il readback TruckLib non
prova che il ciclo editor sia stato davvero completato.

Il verbale Windows deve inoltre confermare o respingere separatamente la
semantica geografica visuale di `X=E, Y=H, Z=-N`. Fino ad allora la
corrispondenza resta un'ipotesi semantica, anche se l'aritmetica è corretta.

## Regole di stato e chiusura

| Condizione | Stato rerun | Conseguenza |
| --- | --- | --- |
| Criterio automatico fallito | `FAIL` | Fermare; PoC-003 resta bloccato. |
| Misura/ambiente obbligatorio indisponibile | `BLOCKED` | Nessun `PASS`; documentare il blocco. |
| Automatico completo, ciclo Windows mancante | `AWAITING_MANUAL_VALIDATION` | Gate non superato; PoC-003 resta bloccato. |
| Tutti i criteri automatici e manuali superati | `PASS` | Solo allora il gate PoC-002 può sbloccare PoC-003; non lo avvia automaticamente. |

Il futuro verbale deve riportare accanto al nuovo esito:
`PoC-002 v1: FAIL (storico)` e non deve sovrascriverne misure, hash o evidenze.
