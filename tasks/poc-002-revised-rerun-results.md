# PoC-002 — Risultati del rerun revisionato Q256

**PoC-002 v1, criteri originali congelati: `FAIL`.**

**Rerun revisionato, validazione automatica: `PASS`.**

**Stato corrente del rerun: `AWAITING_MANUAL_VALIDATION`.**

**Gate PoC-002: non superato; PoC-003 e PoC-004 restano bloccati e non sono
stati avviati.**

Data esecuzione automatica: **3 settembre 2026**

ID criteri: `poc-002-q256-rerun-v2`

ID run: `poc-002-q256-rerun-v2-20260903T171732Z`

Questo verbale applica senza reinterpretazioni la
[specifica congelata](../spikes/poc-002-coordinate-geometry/revised-rerun-spec.md)
e DT-07 corrente. Non sostituisce né riclassifica il
[verbale v1](poc-002-results.md): il `FAIL` originale resta il risultato
corretto rispetto alla soglia allora congelata di 0,001 m per l'intera
conversione nativa.

## Perimetro e input congelati

Il rerun ha esercitato soltanto PoC-002: fixture sintetiche WGS84, AEQD locale,
clipping/densificazione, scaling, JSON neutro E/N/H, mapping aritmetico
`X=E, Y=H, Z=-N`, `Vector3` float32, Q256 di
`TruckLib.ScsMap.Node.Position` e rettifili nativi isolati. Non sono stati
introdotti OSM, osmium, Overpass/HTTP, CLI, topologia condivisa, intersezioni,
prefab, curve generali o codice MVP di produzione.

Il manifest pre-run ha convalidato nove file congelati e l'ambiente prima di
importare l'implementazione Python:

| Input | SHA-256 verificato |
| --- | --- |
| PRD canonico pre-run | `8729f28d18c09b636b027207a619ca5d1db4062eb4f04b749c2f58c663f77f44` |
| Piano spike canonico pre-run | `bdb0b35048c3e2fa3057b000f1054977feb21be4cc54ca55671bf1c1072cdc2c` |
| Specifica rerun congelata | `d2d594ea582f91c0f4669d087b845c3b54d098e23341bebe08efa936625d8894` |
| Fixture geografiche | `3df7f774af4b7a9e6b420871e0fc9a3115c3673964f22dea405e46a53ff43f4b` |
| Riferimento indipendente | `3ed376bcda2f8819cd5dd461f569d641e2ea19787bfc7219a9c9c7b67e166a9c` |
| Generatore del riferimento | `17691ebcb230385a5a575d2032b45f4dda422032abe3a2d219bfb4a9ac395517` |

Il manifest pre-run ha SHA-256
`76b15b6d370fc62f97c942dd1421366fdab0eaf7a7193f8b3912431484c9906f`;
il JSON neutro v2 ha SHA-256
`cf41d2d620372d238c10ce3f7b6323517f45cb345afb459bc04c8c1767d01651`.
Il manifest Python lega esplicitamente entrambi gli hash e l'ID run.

## Ambiente effettivo

| Componente | Valore osservato |
| --- | --- |
| Host automatico | macOS 26.6.2 ARM64 |
| uv | 0.12.7 (`61291a8ca`, aarch64-apple-darwin) |
| Python | CPython 3.14.7, standard GIL, binary64 |
| pyproj / PROJ | 3.7.2 / 9.5.1 |
| Shapely / GEOS | 2.1.2 / 3.13.1 |
| Rete PROJ | `OFF` sia nell'ambiente sia nell'API pyproj |
| .NET | SDK 10.0.400; runtime effettivo 10.0.11; `net10.0` |
| TruckLib | pacchetto 0.5.1; assembly 0.5.1.0; commit informativo `bd745344fc52d3b2d70ce9ac7c88d61b99934805` |
| Formato nativo | 907 |

Nessuna versione richiesta è stata sostituita. L'eseguibile uv globale 0.11.28
non è stato usato.

## Comandi di esecuzione e verifica

Dalla directory `spikes/poc-002-coordinate-geometry/`:

```bash
PROJ_NETWORK=OFF UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
  /private/tmp/osm2ets2-poc002-uv-cache/archive-v0/D5leuEBhzZmVFj5a/bin/uv \
  sync --locked --offline

PROJ_NETWORK=OFF UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
  /private/tmp/osm2ets2-poc002-uv-cache/archive-v0/D5leuEBhzZmVFj5a/bin/uv \
  run --offline --frozen --no-sync python -m unittest discover -s tests -v

PROJ_NETWORK=OFF UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
  /private/tmp/osm2ets2-poc002-uv-cache/archive-v0/D5leuEBhzZmVFj5a/bin/uv \
  run --offline --frozen --no-sync python run_revised_automatic.py \
  --run-id poc-002-q256-rerun-v2-20260903T171732Z \
  --uv-executable /private/tmp/osm2ets2-poc002-uv-cache/archive-v0/D5leuEBhzZmVFj5a/bin/uv \
  --dotnet-executable /usr/local/share/dotnet/dotnet
```

Dalla sottodirectory `csharp/`:

```bash
/usr/local/share/dotnet/dotnet restore --locked-mode
/usr/local/share/dotnet/dotnet build --configuration Release --no-restore
/usr/local/share/dotnet/dotnet run --configuration Release --no-build -- --self-test
/usr/local/share/dotnet/dotnet format --verify-no-changes --no-restore

/usr/local/share/dotnet/dotnet run --configuration Release --no-build -- \
  --revised-rerun generation-a \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/python/neutral-model.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-a

/usr/local/share/dotnet/dotnet run --configuration Release --no-build -- \
  --revised-rerun generation-b \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/python/neutral-model.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-b

/usr/local/share/dotnet/dotnet run --configuration Release --no-build -- \
  --compare-revised-generations \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/pre-run-input-manifest.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/python/python-validation.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/python/neutral-model.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-a/adapter-validation-v2.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-a/semantic-validation.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-b/adapter-validation-v2.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/native-generation-b/semantic-validation.json \
  ../output/revised-rerun/poc-002-q256-rerun-v2-20260903T171732Z/automatic-validation.json
```

Esiti dell'harness: **32/32 test Python**, build Release con **0 warning e 0
errori**, self-test C# `PASS` incluse 45 sonde `Node`, formattazione `PASS`.

## Risultati geografici e geometrici

Tutti i 13 controlli Python sono `PASS`.

| Misura | Massimo osservato | Criterio | Esito |
| --- | ---: | ---: | --- |
| Round-trip WGS84/AEQD | 0,000000001578416 m | ≤ 0,001 m | `PASS` |
| Differenza radiale dal riferimento Vincenty | 0,000000002197226 m | ≤ 0,001 m | `PASS` |
| Differenza azimut indipendente | 6,90136×10⁻¹⁰° | ≤ 10⁻⁹° sperimentale | `PASS` |
| Discretizzazione, convergenza inclusa | 0,001618855053559 m | ≤ 0,01 m pre-scaling | `PASS` |
| Errore rapporto scala 1/0,1 | 1,38778×10⁻¹⁷ | budget congelato | `PASS` |
| float64 → float32, 3D | 0,000379305097538 m | ≤ 0,001 m scena | `PASS` |
| Hausdorff rettifili nativi | 0,004277268693811 m | ≤ 1,0 m scena | `PASS` |
| Raggio planare nativo | 9.888,000282196546 m | ≤ 10.000 m | `PASS` |

Le origini restano esattamente `(12,4924; 41,8902)` dalla bbox e
`(12,6; 41,8)` dall'estensione candidata senza bbox. Le bbox di area e
diagonale raggiungono rispettivamente il 97,9999945% e il 97,5140937% del
vincolo esercitato, con l'altro limite rispettato. Il punto geografico della
fixture nativa è al 98,88% del raggio prima della conversione; il massimo
nativo letto è ancora sotto 10 km.

Il segmento attraversante è stato ritagliato in WGS84 prima della proiezione.
Entrambi gli estremi sono sintetici e conservano way, indice e parametri
`t=0,12748819240787224` e `t=0,8725118075921278`; differenze dal riferimento
indipendente: zero. La sola corda degli estremi devierebbe di 1,657693115506 m;
la geometria densificata usa 33 punti e misura 0,001618855054 m, convergenza
inclusa. I tre offset 0,001/0,01/0,1 m restano traslazioni di Road da 100 m.

## Q256, mapping e output nativo

Ogni generazione contiene 6 mappe, 8 Road isolate, 16 nodi terminali privati e
36 file nativi: 6 `.mbd` formato 907 e 6 gruppi
`.aux/.base/.data/.desc/.snd`. Non sono state create connessioni condivise.

Per ogni componente è stata verificata l'uguaglianza intera:

```text
expected_q = trunc_toward_zero(float32_axis * 256f)
expected_q = written_q = readback_q
```

| Asse | Endpoint esatti | Perdita massima osservata |
| --- | ---: | ---: |
| X | 16/16 | 0,003189086914063 m |
| Y | 16/16 | 0 m |
| Z | 16/16 | 0,002838134765625 m |
| Totale | **48/48** | 0,003189086914063 m per asse |

In ciascuna generazione sono inoltre riuscite **45/45 sonde dirette**: 15 per
ognuno di X/Y/Z, con valori negativi, zero, positivi, piccoli offset e float
immediatamente sotto/sul/sopra i bordi Q256 di entrambi i segni. La perdita
massima delle sonde è 0,003906190395355 m.

| Limite del solo stadio Q256 | Teorico | Massimo osservato | Esito |
| --- | ---: | ---: | --- |
| Passo Δ | 0,00390625 m | esatto | `PASS` |
| Per asse | < 0,00390625 m | 0,003906190395355 m incluse sonde | `PASS` |
| Euclideo X/Z | < 0,005524271728020 m | 0,004269108137925 m | `PASS` |
| Euclideo 3D | < 0,006765823467066 m | 0,004269108137925 m | `PASS` |

Il precedente valore complessivo v1 di 0,004277268693811 m è ancora misurato e
non viene nascosto: nel modello revisionato è correttamente classificato come
somma risultante di stadi distinti. Il float64→float32 passa il proprio budget,
i codici Q256 coincidono esattamente e la perdita Q256 resta nei limiti
matematici deterministici. Nessuna soglia è stata aumentata.

L'aritmetica dell'adapter `X=E, Y=H, Z=-N` è `PASS`. Questo non conferma la
semantica geografica visuale degli assi: essa resta in attesa del Map Editor.

## Riproducibilità e catena di evidenza

Le generazioni `generation-a` e `generation-b` sono state eseguite in due
processi e directory nuove e distinte. I rispettivi report adapter hanno hash
diversi perché conservano identità di generazione, root e UID casuali; i
manifest semantici normalizzati hanno entrambi SHA-256
`5b3e211bb9aaedbedf7713140bf49a61af010f05f33f6edcb38587a67de003cb`
e sono identici byte per byte.

L'aggregato verifica identità e scale esatte delle fixture, hash
pre-run/Python/neutro/adapter/semantica, runtime e TruckLib effettivi, 48 codici
endpoint e 45 sonde per generazione. Ha SHA-256
`92fad2485734242539f51dc2b700fd1c269abee980bdec0fcd0d920f3369f9e1`,
`comparisonValidation: PASS`, nessun failure e
`rerunState: AWAITING_MANUAL_VALIDATION`.

Il riepilogo machine-readable versionato è
[`revised-rerun-automatic-validation.json`](../spikes/poc-002-coordinate-geometry/evidence/revised-rerun-automatic-validation.json).
Gli output completi restano nella directory ignorata
`spikes/poc-002-coordinate-geometry/output/revised-rerun/<run-id>/` (80 file,
circa 692 KiB) e non includono asset proprietari SCS.

## Preservazione del risultato v1

Dopo il rerun, gli hash del verbale v1, della checklist v1, della RCA, del
report RCA, del manifest Python v1, del JSON neutro v1 e del report adapter v1
coincidono ancora con quelli registrati prima dell'esecuzione. In particolare:

| Evidenza storica | SHA-256 invariato |
| --- | --- |
| `tasks/poc-002-results.md` | `af103e22564f023f8fb1d059666c96a14bf5eafdce2a23c29a79e10c652e44a1` |
| `manual-validation/checklist.md` | `9049c2501aa68c263b569420fd565ed0f3792685f6b57efb458eaf20555479f5` |
| `evidence/native-q256-rca.md` | `b6494bdd94a922e0c2cf621c7a485cdb42e609951f4585db0695b0d368e07965` |
| `evidence/native-q256-validation.json` | `e64ab191fc03d90248e01c7b377caee1abf6104d8e5718c7d80ce73e77ebd137` |
| `output/run-automatic/neutral-model.json` | `169c6b77226ca9d3d5d6f79a25b10d70b76ddb2d6613248d857ac33027c0e33e` |
| `output/run-automatic/native-final/adapter-validation.json` | `d0f412c2c9ffc9d5404ffe0cbb0a4825e6ad78b90ada1bf3b30e4c5610ec0b1f` |

La specifica congelata resta anch'essa invariata e continua a descriversi come
`NOT_EXECUTED`, perché è l'istantanea pre-run; questo verbale separato registra
l'esecuzione.

## Gate manuale ancora richiesto

La procedura vincolante è nella
[checklist revisionata](../spikes/poc-002-coordinate-geometry/manual-validation/revised-rerun-checklist.md).
In sintesi, su Windows 11 x64 / ETS2 1.60.1.7 occorre conservare immutabile la
generazione A, lavorare su una copia, aprire e ispezionare ogni mappa, eseguire
**Map → Recompute map**, salvare senza riparazioni, chiudere completamente
editor e gioco, riaprire e verificare di nuovo. Dopo il rientro degli output si
deve confrontare per identità di nodo e componente
`q_after = q_before = q_expected`; qualunque delta intero fallisce la
persistenza. Separatamente, il verbale deve confermare o respingere la
semantica visuale `X=E, Y=H, Z=-N`.

Una schermata non prova la precisione e il readback TruckLib non prova che il
ciclo editor sia avvenuto. Fino al completamento di entrambe le verifiche lo
stato resta **`AWAITING_MANUAL_VALIDATION`**, non `PASS`.
