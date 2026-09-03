# PoC-002 — Coordinate and Geometry Validation: risultati

**Stato finale PoC-002 v1 (criteri originali congelati): `FAIL`**

**Stato PoC-002 revised rerun (criteri DT-07 revisionati): `NOT_EXECUTED`**

**Stato del gate PoC-002: non superato; PoC-003 resta bloccato.**

**Data esecuzione automatica: 1 settembre 2026**

**Data RCA precisione nativa: 2 settembre 2026**

La pipeline geografica e il confine neutro hanno superato tutti i controlli
automatici. Il percorso nativo non soddisfa però il criterio obbligatorio di
precisione: l'errore massimo fra le coordinate E/N/H scalate e il readback dopo
TruckLib `Save` → `Open` è
**0,004277268693810707 m**, contro il limite invariato di **0,001 m della
scena**. Il run dimostra pertanto un fallimento, non un blocco di misura.

Il ciclo Windows Map Editor non è stato eseguito: non può compensare un criterio
automatico già fallito. PoC-003 e PoC-004 non sono stati avviati.

## Verifiche preliminari e baseline

Prima di ogni modifica:

- il branch era `main` e `git status` era pulito;
- `HEAD` era esattamente
  `2117b848300644e6876cb301368bdf774c9c436d`
  (`2117b84 test: complete PoC-001 manual validation`);
- `tasks/poc-001-results.md` e lo spike PoC-001 dichiaravano `PASSED`, con due
  cicli completi Map Editor su Windows 11 x64 / ETS2 1.60.1.7;
- PRD, piano degli spike, verbale PoC-001, README, implementazione ed evidenze
  PoC-001 pertinenti sono stati letti integralmente.

La baseline canonica congelata prima dell'esperimento è:

| Oggetto | Versione/hash |
| --- | --- |
| Commit | `2117b848300644e6876cb301368bdf774c9c436d` |
| `tasks/prd-osm2ets2-mvp.md` | `c5e8d6f1a51a8980a042e53b40bf49ee1dc0dc6c8c9d1521a5659e50432e1e97` |
| `tasks/spikes-osm2ets2-mvp.md` prima dell'aggiornamento di stato | `25ef624812d0661f87991d290a950b552c49f776d4aced67b50368fc61ec7571` |
| ETS2 target | 1.60.x stabile; build validata 1.60.1.7 |
| Map Editor finale target | Windows 11 x64 |
| TruckLib | 0.5.1 esatto |
| .NET | SDK 10.0.400 / runtime 10.0.11 |
| Formato nativo | 907 |

Durante l'esecuzione v1 il PRD e PoC-001 non sono stati modificati;
l'aggiornamento del piano riguardava soltanto lo stato osservato di PoC-002.
La successiva riconciliazione architetturale modifica prospetticamente DT-07 e
la specifica del rerun, senza cambiare questi hash o alcuna evidenza v1.

## Perimetro eseguito

È stato creato soltanto
`spikes/poc-002-coordinate-geometry/`, indipendente dal codice di produzione.
Comprende fixture sintetiche, riferimento indipendente, trasformazione Python,
JSON neutro E/N/H, adapter C# minimale e Road TruckLib isolate.

Non sono stati introdotti osmium, OSM, Overpass, HTTP, CLI, grafo stradale,
intersezioni, prefabs, curve generali, topologia condivisa o implementazione
dell'MVP. Gli asset nativi riusati sono esclusivamente quelli già verificati da
PoC-001: tipo `ger1`, look `ger_1`, variante `broken_de`, edge `ger_sh_15`.

## Ambiente automatico riprodotto

| Componente | Valore osservato |
| --- | --- |
| Host | macOS 26.6.2 ARM64 |
| uv | 0.12.7 (`61291a8ca`, aarch64-apple-darwin) |
| Python | CPython 3.14.7 standard GIL; `Py_GIL_DISABLED=0` |
| Numeri Python | IEEE-754 binary64, mantissa 53 bit |
| pyproj / PROJ | 3.7.2 / 9.5.1 |
| Shapely / GEOS | 2.1.2 / 3.13.1 |
| Dipendenze transitive | certifi 2026.7.22, NumPy 2.5.2 |
| Rete PROJ | `PROJ_NETWORK=OFF`; API pyproj `False` |
| .NET | SDK 10.0.400, runtime 10.0.11, net10.0 |
| TruckLib assembly | 0.5.1.0, commit `bd745344fc52d3b2d70ce9ac7c88d61b99934805` |

L'`uv` globale disponibile era 0.11.28 e non è stato usato come sostituto.
`uv` 0.12.7 è stato eseguito in modo isolato con `uvx`; CPython 3.14.7 è
installato sotto `.python/` e la `.venv` registra `uv = 0.12.7`. Nessuna
versione richiesta è stata sostituita. La lista installata non contiene
osmium.

## Fonti e natura dei fatti

- **Canonico repository:** scope, soglie, origine, clipping, scala, baseline e
  gate provengono dal PRD e dalla sezione PoC-002 del piano.
- **Upstream:** comportamento `always_xy`, AEQD, rete PROJ, WGS84, API
  TruckLib/Vector3 e ciclo Map Editor sono verificati in
  [`upstream-verification.md`](../spikes/poc-002-coordinate-geometry/evidence/upstream-verification.md).
- **Riferimento indipendente:** Vincenty diretto/inverso WGS84, integrazione
  ellissoidale dei bordi e Liang–Barsky sono implementati con la sola libreria
  standard in `reference/freeze_reference.py`.
- **Esperimento:** coordinate, errori, formato 907, asset riletti, UIDs e
  allineamento alla griglia 1/256 m vengono dai manifest dei run.
- **RCA successiva delimitata:** il sorgente corrispondente al pacchetto
  TruckLib 0.5.1 e le sonde dirette confermano il quantizzatore Q256 per
  `Node.Position` nel writer esercitato. Non viene presentato come specifica
  generale di ogni coordinata ETS2, comportamento del Map Editor o garanzia di
  altre versioni TruckLib.

Le fonti indipendenti non effettuano un semplice forward/inverse pyproj contro
se stesso. AEQD viene confrontata con distanze e azimut Vincenty congelati
soltanto radialmente rispetto al centro, senza pretendere uguaglianza esatta di
distanze arbitrarie fra punti non centrali.

## Fixture congelate

| File | SHA-256 |
| --- | --- |
| `fixtures/frozen-fixtures.json` | `3df7f774af4b7a9e6b420871e0fc9a3115c3673964f22dea405e46a53ff43f4b` |
| `fixtures/independent-reference.json` | `3ed376bcda2f8819cd5dd461f569d641e2ea19787bfc7219a9c9c7b67e166a9c` |
| `reference/freeze_reference.py` | `17691ebcb230385a5a575d2032b45f4dda422032abe3a2d219bfb4a9ac395517` |

La rigenerazione finale in una directory temporanea nuova è risultata
byte-per-byte identica per entrambi i JSON.

Origine esplicita O: **(12,4924° E, 41,8902° N)**. L'origine derivata senza
bbox è **(12,6°, 41,8°)** e viene scelta prima delle esclusioni successive;
ricalcolarla sulle sole geometrie conservate la sposterebbe, controllo negativo
che il test rileva.

| Controllo | Distanza geodetica congelata | Azimut iniziale | E AEQD osservata | N AEQD osservata |
| --- | ---: | ---: | ---: | ---: |
| east | 123,456000003 m | 90,000000000° | 123,456000002 m | ≈0 m |
| north | 234,567000001 m | 359,999999999° | ≈0 m | 234,567000000 m |
| west | 345,677999997 m | 270,000000000° | -345,677999994 m | ≈0 m |
| south | 456,788999999 m | 180,000000000° | ≈0 m | -456,788999999 m |
| oblique | 321,122999999 m | 37,125000000° | 193,815694746 m | 256,038000301 m |

Le scale 1,0 e 0,1 sono applicate e confrontate su tutti e cinque i controlli.
I tre offset 0,001/0,01/0,1 m traslano soltanto l'asse E di Road da 100 m; non
sono Road millimetriche.

| Fixture di bordo | Area | Rapporto area | Diagonale massima | Rapporto diagonale |
| --- | ---: | ---: | ---: | ---: |
| area | 24.499.998,62507248 m² | 97,9999945% | 7.000,713874557 m | 70,0071% |
| allungata | 9.699.998,861572266 m² | 38,7999954% | 9.751,409374650 m | 97,5141% |

Il punto nativo di precisione raggiunge **9.887,999999938 m** dopo scala
2,06, cioè il **98,88%** del raggio; entrambi gli estremi della Road da 206 m
restano nella bbox allungata ammessa e la sua origine resta O.

Il segmento attraversante ha entrambi gli estremi esterni. Il clipping WGS84
produce `t=0,12748819240787224` e `t=0,8725118075921278`, con way sorgente,
indice di segmento, parametro e flag sintetico preservati. La retta fra i soli
estremi proiettati devierebbe di **1,657693115506 m**, quindi la fixture rileva
realmente l'omissione della densificazione.

## Risultati Python e confine neutro

`21/21` test Python sono riusciti. Il manifest riporta
`automaticStatus: PASS`.

| Criterio automatico | Massimo osservato | Soglia | Esito |
| --- | ---: | ---: | --- |
| Round-trip geografico | 0,000000001578416 m | 0,001 m | `PASS` |
| Differenza radiale da Vincenty | 0,000000002197226 m | 0,001 m | `PASS` |
| Differenza azimut indipendente | 6,90136×10⁻¹⁰° | 10⁻⁹° sperimentale | `PASS` |
| Discretizzazione, inclusa convergenza | 0,001618855054 m | 0,01 m | `PASS` |
| Errore rapporto scala | 1,38778×10⁻¹⁷ | budget numerico | `PASS` |
| Differenza area da riferimento | 0,000019434839 m² | diagnostica | misurata |
| Differenza diagonale da riferimento | 0,000000058069 m | diagnostica | misurata |

La densificazione usa 33 punti e un target costruttivo di 0,005 m; la misura
indipendente con 2.048 campioni per intervallo è 0,001618855054 m e la
differenza rispetto alla misura a 512 campioni è zero alla precisione
registrata.

La definizione completa AEQD, WKT2:2019, PROJJSON, definizioni dei transformer,
coordinate forward/inverse e versioni sono nel manifest. Il JSON neutro ha hash
`169c6b77226ca9d3d5d6f79a25b10d70b76ddb2d6613248d857ac33027c0e33e` e
contiene esclusivamente E/N/H in metri della scena, scala già applicata e
metadati sperimentali minimi. Non contiene `NormalScale` o `CityScale`.

## Risultati C# e output nativo

L'adapter convalida schema, assi, unità, numeri finiti e mapping, quindi applica
soltanto `X=E, Y=H, Z=-N` e la conversione esplicita a `Vector3`. Il self-test e
la build Release sono riusciti con zero warning e zero errori.

Il run `native-final` comprende:

- 6 mappe e 8 Road rettilinee isolate;
- 16 nodi terminali privati, UID non nulli e distinti, riferimenti integri;
- 6 file `.mbd` formato 907;
- 30 file di settore: sei gruppi `.aux/.base/.data/.desc/.snd`;
- asset/tokens PoC-001 riletti senza variazioni;
- 1 manifest con inventario di path relativi, byte e SHA-256.

| Misura nativa | Massimo | Soglia | Esito |
| --- | ---: | ---: | --- |
| `double` E/N/H → `Vector3` | 0,000379305098 m | 0,001 m | `PASS` |
| TruckLib `Save/Open`, complessivo | **0,004277268694 m** | 0,001 m | **`FAIL`** |
| Contributo fra float input e readback | 0,004269108138 m | diagnostica | misurato |
| Hausdorff simmetrica rettifili | 0,004277268694 m | 1,0 m | `PASS` |
| Errore angolare rettifili | 0,001379302176° | diagnostica | nessuna inversione |
| Raggio planare massimo | 9.888,000282197 m | 10.000 m | `PASS` |

Ogni coordinata riletta cade esattamente sulla griglia candidata 1/256 m. Le
traslazioni mostrano direttamente `0,001 → 0`, `0,01 → 0,0078125` e
`0,1 → 0,09765625`; il residuo massimo dalla griglia è zero. Il massimo
complessivo appartiene alla Road obliqua a scala 1, non soltanto al caso vicino
al raggio.

## RCA della quantizzazione nativa

La successiva analisi mirata è documentata integralmente in
[`native-q256-rca.md`](../spikes/poc-002-coordinate-geometry/evidence/native-q256-rca.md);
il report machine-readable è
[`native-q256-validation.json`](../spikes/poc-002-coordinate-geometry/evidence/native-q256-validation.json),
SHA-256
`e64ab191fc03d90248e01c7b377caee1abf6104d8e5718c7d80ce73e77ebd137`.
L'analisi non ha rieseguito la generazione delle mappe.

Il `.nuspec`, il SourceLink dell'assembly e il tag upstream identificano
esattamente TruckLib 0.5.1 al commit
`bd745344fc52d3b2d70ce9ac7c88d61b99934805`. In quel sorgente,
`Node.Serialize` scrive ciascuna componente `Vector3` con:

```text
(int)(Position.<axis> * 256f)
```

`Node.Deserialize` legge l'`Int32` signed e divide per `256f`.
`Map.WriteNodes`/`ReadNodes` invocano direttamente quei metodi. Per valori
float32 finiti e nel dominio del PoC, il cast C# tronca verso zero; la regola
confermata, indipendente per X/Y/Z, è:

```text
expected_native_axis = trunc_toward_zero(float32_scene_axis * 256) / 256
```

Quarantacinque sonde dirette (15 valori su ciascuno dei tre assi), comprendenti
zero, segni opposti e i float immediatamente sotto/sopra i bordi ±1 m, hanno
dato 45/45 corrispondenze con il troncamento, 27/45 con `floor` e 27/45 con
nearest-even. Sul manifest già esistente, tutte le 48 componenti dei 16
endpoint sono esattamente Q256 e coincidono con la formula, con residuo zero.

I limiti matematici del solo stadio Q256, rispetto all'input float32, sono:

| Regola | Per asse | X/Z euclideo | 3D euclideo |
| --- | ---: | ---: | ---: |
| troncamento osservato | `< 0,00390625 m` | `< 0,005524271728019903 m` | `< 0,0067658234670659265 m` |
| nearest ipotetico | `≤ 0,001953125 m` | `≤ 0,0027621358640099515 m` | `≤ 0,0033829117335329633 m` |

Per l'endpoint obliquo peggiore, l'input float32
`(193,81568908691406; 0; -256,0379943847656)` diventa
`(193,8125; 0; -256,03515625)`. Gli errori X/Z sono rispettivamente
`-0,0031890869140625 m` e `+0,002838134765625 m`, entrambi verso zero; la norma
è `0,004269108137924589 m`, sotto il limite X/Z del troncamento. L'errore
complessivo `0,004277268693810707 m` include anche il distinto passaggio
float64 → float32 (`0,000008186956489898853 m` per quell'endpoint). Il valore
osservato è quindi coerente con Q256 truncation e supera perfino il limite 3D
dell'ipotetico nearest.

La soglia generale DT-07 di 0,001 m non è matematicamente garantibile per
coordinate arbitrarie con questo writer: il troncamento su un solo asse può
avvicinarsi a 0,00390625 m. Anche nearest avrebbe un massimo per asse di
0,001953125 m. Valori scelti sulla griglia possono essere esatti, come nel
fixture PoC-001, ma non provano la proprietà generale.

Al momento della RCA questa conclusione non modificava DT-07. La proposta,
allora ancora da decidere, era
separare float64 → float32 dal fixed-point e richiedere per quest'ultimo
uguaglianza componente-per-componente al codice atteso
`trunc(float32_axis*256)`, dichiarando il bound `<1/256 m` e i limiti geometrici
derivati. La persistenza post-editor sarebbe un criterio ulteriore: dopo
recompute/save/chiusura/riapertura ogni codice Q256 dovrebbe restare uguale al
valore pre-editor atteso. Poiché Q256 è idempotente nel dominio esercitato, non
si propone un secondo budget di quantizzazione per ogni save. Tutto ciò è
registrato qui come **proposto, non applicato alla data della RCA**.

## Decisione architetturale successiva al verbale v1

Il 2 settembre 2026 la revisione canonica
[DT-07](prd-osm2ets2-mvp.md) ha adottato prospetticamente la terza delle
alternative analizzate: **modellare la quantizzazione Q256 come stadio
deterministico esplicito dell'adapter TruckLib 0.5.1**. Le alternative non
selezionate sono mantenere il requisito generale di 1 mm cambiando
writer/rappresentazione, senza evidenza che un altro percorso eviti Q256, e
pre-allineare la geometria alla griglia, spostando però la modifica geometrica
a monte.

La decisione mantiene separati round-trip WGS84/AEQD, discretizzazione,
scaling/neutral E/N/H float64, conversione `Vector3` float32, codici Q256 e
persistenza Map Editor. Per ciascun asse finito di `Node.Position`, il futuro
rerun richiede uguaglianza intera esatta con
`trunc_toward_zero(float32_axis * 256f)`; dopo il ciclo editor richiede lo
stesso codice prima e dopo, senza un secondo budget Q256.

Questa decisione **non si applica retroattivamente al run v1**: il suo `FAIL`,
le misure e gli hash congelati restano invariati. Definisce un nuovo rerun
completo, attualmente `NOT_EXECUTED`, nella
[`specifica congelata`](../spikes/poc-002-coordinate-geometry/revised-rerun-spec.md).
La regola resta delimitata a TruckLib 0.5.1
`TruckLib.ScsMap.Node.Position`; semantica visuale degli assi e persistenza
post-editor non sono ancora dimostrate.

I criteri del rerun sono congelati separatamente dai riferimenti storici v1:

| Documento revisionato | SHA-256 al congelamento |
| --- | --- |
| `tasks/prd-osm2ets2-mvp.md` | `8729f28d18c09b636b027207a619ca5d1db4062eb4f04b749c2f58c663f77f44` |
| `tasks/spikes-osm2ets2-mvp.md` | `bdb0b35048c3e2fa3057b000f1054977feb21be4cc54ca55671bf1c1072cdc2c` |
| `spikes/poc-002-coordinate-geometry/revised-rerun-spec.md` | `d2d594ea582f91c0f4669d087b845c3b54d098e23341bebe08efa936625d8894` |

Il manifest scelto ha SHA-256
`d0f412c2c9ffc9d5404ffe0cbb0a4825e6ad78b90ada1bf3b30e4c5610ec0b1f`.
Gli output nativi sono ignorati e riproducibili; UID e hash binari non sono
assunti deterministici. Una seconda generazione in una directory temporanea
nuova ha riprodotto esattamente i cinque massimi numerici e lo stesso `FAIL`.

## Criteri e stato delle ipotesi

| Criterio canonico v1 | Esito | Nota |
| --- | --- | --- |
| Riferimento indipendente | `PASS` | origine, direzioni, distanze e azimut concordano |
| Andata/ritorno geografico ≤0,001 m | `PASS` | massimo 1,578 nm |
| Discretizzazione ≤0,01 m pre-scala | `PASS` | massimo 1,619 mm incluso il controllo di convergenza |
| Scala unica e uniforme | `PASS` | tutti i controlli, nessun metadata scale |
| Origine e dominio | `PASS` | area, diagonale e raggio separati; valori finiti |
| Mapping aritmetico C# | `PASS` | `X=E, Y=H, Z=-N` applicato senza doppia scala |
| Precisione numerica nativa ≤0,001 m | **`FAIL`** | massimo 4,277 mm già prima dell'editor |
| Geometria nativa rettilinea ≤1,0 m | `PASS` | Hausdorff 4,277 mm |
| Orientamento semantico Map Editor | `NOT_EXECUTED` | non necessario per determinare il `FAIL` |
| Readback numerico post-editor | `NOT_EXECUTED` | non può riparare il fallimento pre-editor |

L'ipotesi va distinta in due parti:

- **confermata automaticamente come operazione dell'adapter:** C# applica
  esattamente `X=E, Y=H, Z=-N`, conserva il verso numerico e non introduce
  rotazione o riflessione inattesa nel readback TruckLib;
- **respinta sotto il profilo v1:** il medesimo percorso non rispettava la
  precisione nativa obbligatoria di 0,001 m;
- **non confermata nel significato geografico visuale:** il segno del nord e
  l'orientamento nel Map Editor richiederebbero il ciclo Windows, non eseguito.

PoC-001 non viene smentito. Aveva dimostrato fattibilità e persistenza di una
Road con coordinate intere esattamente rappresentabili; non aveva affermato
precisione sub-griglia per coordinate arbitrarie o semantica geografica degli
assi.

## Causa, conseguenze e condizione per riprendere

La riproduzione minima è una Road traslata con E iniziale 0,01 m: il readback
inizia a 0,0078125 m, oltre il budget. Il percorso `Vector3` da solo rimane nel
budget; la RCA attribuisce il superamento al troncamento Q256 esplicito di
`Node.Serialize`, confermato sia dal sorgente esatto sia dai byte prodotti.

Alla chiusura di v1 il risultato richiedeva un riesame esplicito di
rappresentazione e DT-07 oppure del writer/percorso nativo. Non era autorizzato
aumentare la soglia, cambiare TruckLib/versione/formato, allineare manualmente i
fixture alla griglia o modificare retroattivamente PoC-001. La decisione
documentata è ora quella riportata sopra; prima di un eventuale avanzamento
serve comunque un nuovo run completo sulla soluzione scelta. PoC-003 resta
fermo.

## Comandi di esecuzione e verifica

I comandi effettivamente usati per il run finale e per la successiva RCA,
dalle rispettive directory, sono:

```bash
UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
UV_TOOL_DIR=/private/tmp/osm2ets2-poc002-uv-tools \
uvx --from uv==0.12.7 uv --version

UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
UV_TOOL_DIR=/private/tmp/osm2ets2-poc002-uv-tools \
uvx --offline --from uv==0.12.7 uv python install 3.14.7 \
  --install-dir .python --no-bin

UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
UV_TOOL_DIR=/private/tmp/osm2ets2-poc002-uv-tools \
uvx --offline --from uv==0.12.7 uv sync --locked --offline \
  --python .python/cpython-3.14.7-macos-aarch64-none/bin/python

.venv/bin/python reference/freeze_reference.py \
  --repo-root <REPO_ROOT> \
  --output-dir /private/tmp/osm2ets2-poc002-fixture-recheck.CfA14k
cmp -s fixtures/frozen-fixtures.json \
  /private/tmp/osm2ets2-poc002-fixture-recheck.CfA14k/frozen-fixtures.json
cmp -s fixtures/independent-reference.json \
  /private/tmp/osm2ets2-poc002-fixture-recheck.CfA14k/independent-reference.json

PROJ_NETWORK=OFF .venv/bin/python -m unittest discover -s tests -v
PROJ_NETWORK=OFF .venv/bin/python run_automatic.py

cd csharp
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
dotnet format --verify-no-changes --no-restore
dotnet run --configuration Release --no-build -- \
  ../output/run-automatic/neutral-model.json \
  ../output/run-automatic/native-final

dotnet run --configuration Release --no-build -- \
  ../output/run-automatic/neutral-model.json \
  /private/tmp/osm2ets2-poc002-native-recheck.Alw05g

dotnet run --configuration Release --no-build -- \
  --quantizer-rca \
  ../output/run-automatic/native-final/adapter-validation.json \
  ../evidence/native-q256-validation.json
```

I due comandi di generazione nativa ritornano `2` per il criterio numerico
fallito, dopo avere scritto tutti i file e il manifest. Il formatter ha
richiesto un retry fuori dal sandbox per creare la propria pipe IPC locale; il
retry è terminato con codice 0. Un primo accesso isolato a uv e il restore
iniziale hanno richiesto rete per fonti e metadata; le verifiche finali di
ambiente e restore hanno poi usato cache/lock e sono riuscite. Il comando
`--quantizer-rca` è diagnostico e ritorna `0` con `Q256_RCA_PASSED`: ha letto il
manifest esistente e serializzato singoli `Node` in memoria, senza chiamare
`Map.Save` e senza rieseguire il PoC. Il suo successo non cambia lo stato
PoC-002 v1 `FAIL` sotto i criteri originali congelati.

## Validazione Windows v1 ancora predisposta

La checklist completa è in
[`manual-validation/checklist.md`](../spikes/poc-002-coordinate-geometry/manual-validation/checklist.md).
Richiede per tutte le sei mappe: copia immutabile dell'output, apertura,
ispezione di verso/posizioni, **Map → Recompute map**, salvataggio, chiusura
completa, riapertura, nuova verifica e readback numerico post-editor. Screenshot
e lettura TruckLib restano evidenze di supporto, non sostituti del ciclo.

Poiché il run automatico v1 è `FAIL`, questa attività resta diagnostica e non è
un gate ancora aperto verso `PASS`. Il futuro rerun usa invece la
[`specifica revisionata`](../spikes/poc-002-coordinate-geometry/revised-rerun-spec.md),
che richiede stabilità esatta dei codici Q256 ed è `NOT_EXECUTED`.
