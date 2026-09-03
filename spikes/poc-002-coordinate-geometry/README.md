# PoC-002 — Coordinate and Geometry Validation

**PoC-002 v1, criteri originali congelati: `FAIL`.** Tutti i controlli
geografici automatici sono riusciti, ma il ciclo nativo TruckLib
`Save` → `Open` ha aggiunto un errore massimo di
**0,004277268693810707 m**, superiore alla soglia obbligatoria v1 di
**0,001 m della scena**. Il ciclo Map Editor non è stato eseguito perché un
criterio automatico aveva già determinato il fallimento.

**PoC-002 revised rerun, criteri DT-07 revisionati: validazione automatica
`PASS`; stato `AWAITING_MANUAL_VALIDATION`.** Il ciclo Windows non è stato
eseguito, quindi il gate PoC-002 non è superato e PoC-003 resta bloccato. Il
rerun non riclassifica il run v1.

Questo è uno spike isolato. Non contiene parsing OSM, osmium, HTTP/Overpass,
topologia condivisa, intersezioni, prefab, curve generali o CLI di prodotto.
Le otto Road native sono rettifili indipendenti e riusano soltanto API e asset
minimali dimostrati da PoC-001.

## Pipeline esercitata

```text
WGS84 (lon, lat)
  → AEQD locale WGS84, origine deterministica, falsi E/N nulli
  → e/n metrici float64
  → E=s·e, N=s·n, H=0
  → JSON neutro E/N/H
  → adapter C# X=E, Y=H, Z=-N
  → Vector3 float32
  → Q256 di TruckLib.ScsMap.Node.Position
  → TruckLib 0.5.1, formato 907
```

Python non applica la conversione ETS2. L'adapter C# non conosce WGS84,
proiezioni o clipping. Il JSON è deliberatamente limitato alle sei mappe e alle
otto Road necessarie all'esperimento; non è il futuro IR di prodotto.

## Struttura

```text
spikes/poc-002-coordinate-geometry/
├── fixtures/                  # input e riferimento indipendente congelati
├── reference/                 # generatore standard-library-only dei fixture
├── tests/                     # 32 test Python focalizzati, v1 + rerun
├── csharp/                    # adapter sperimentale .NET 10 / TruckLib 0.5.1
├── evidence/                  # fonti upstream, RCA e riepilogo rerun
├── manual-validation/         # checklist v1 e rerun, entrambe separate
├── revised-rerun-spec.md      # snapshot pre-run dei criteri revisionati
├── poc002_geometry.py         # proiezione, misure e JSON neutro
├── run_automatic.py           # riproduzione del manifest Python v1
└── run_revised_automatic.py   # preflight e manifest Python del rerun
```

`.python/`, `.venv/`, `csharp/bin/`, `csharp/obj/`, `output/` e le directory di
run manuali sono ignorati. Nessun asset proprietario SCS viene copiato nel
repository.

## Ambiente esatto

- CPython 3.14.7, build standard GIL, binary64;
- uv 0.12.7 eseguito isolatamente tramite `uvx`;
- pyproj 3.7.2 con PROJ 9.5.1;
- Shapely 2.1.2 con GEOS 3.13.1;
- .NET SDK 10.0.400, runtime 10.0.11;
- TruckLib 0.5.1 esatto, formato dichiarato e osservato 907;
- `PROJ_NETWORK=OFF` e `pyproj.network.set_network_enabled(False)`.

L'installazione iniziale degli artefatti richiede accesso alle rispettive fonti.
Dopo il popolamento della cache, la verifica dell'ambiente Python è stata
ripetuta offline:

```bash
cd spikes/poc-002-coordinate-geometry
UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
UV_TOOL_DIR=/private/tmp/osm2ets2-poc002-uv-tools \
uvx --offline --from uv==0.12.7 uv python install 3.14.7 \
  --install-dir .python --no-bin

UV_CACHE_DIR=/private/tmp/osm2ets2-poc002-uv-cache \
UV_TOOL_DIR=/private/tmp/osm2ets2-poc002-uv-tools \
uvx --offline --from uv==0.12.7 uv sync --locked --offline \
  --python .python/cpython-3.14.7-macos-aarch64-none/bin/python
```

## Fixture congelate e riferimento indipendente

Il generatore usa soltanto la libreria standard: Vincenty diretto/inverso su
WGS84, integrazione geodetica ellissoidale dei bordi delle bbox e clipping
analitico Liang–Barsky in longitudine/latitudine. Non usa pyproj, PROJ,
Shapely, GeographicLib o il codice sotto test.

| File | SHA-256 |
| --- | --- |
| `fixtures/frozen-fixtures.json` | `3df7f774af4b7a9e6b420871e0fc9a3115c3673964f22dea405e46a53ff43f4b` |
| `fixtures/independent-reference.json` | `3ed376bcda2f8819cd5dd461f569d641e2ea19787bfc7219a9c9c7b67e166a9c` |
| `reference/freeze_reference.py` | `17691ebcb230385a5a575d2032b45f4dda422032abe3a2d219bfb4a9ac395517` |

Una rigenerazione separata è risultata byte-per-byte identica. I dati coprono
origine esplicita e derivata, cinque controlli asimmetrici, scale 1 e 0,1,
offset 0,001/0,01/0,1 m applicati a Road da 100 m, due bbox di bordo, raggio
nativo al 98,88% e un segmento attraversante con entrambi gli estremi esterni.

## Rerun revisionato automatico

Il 3 settembre 2026 è stata eseguita integralmente la
[`specifica congelata`](revised-rerun-spec.md) con ID run
`poc-002-q256-rerun-v2-20260903T171732Z`. Tutti i 13 controlli Python e i
32 test sono riusciti. Due processi nativi distinti hanno prodotto ciascuno 6
mappe, 8 Road, 16 nodi, 48 componenti endpoint e 45 sonde dirette; i manifest
semantici risultano identici byte per byte.

| Misura revisionata | Massimo/esito | Criterio | Stato |
| --- | ---: | ---: | --- |
| Round-trip WGS84/AEQD | 0,000000001578416 m | ≤ 0,001 m | `PASS` |
| Discretizzazione inclusa convergenza | 0,001618855053559 m | ≤ 0,01 m pre-scaling | `PASS` |
| Errore rapporto scala | 1,38778×10⁻¹⁷ | budget congelato | `PASS` |
| float64 → float32 | 0,000379305097538 m | ≤ 0,001 m scena | `PASS` |
| Codici endpoint Q256 | 48/48 per generazione | uguaglianza intera esatta | `PASS` |
| Sonde Q256 X/Y/Z | 45/45 per generazione | uguaglianza intera esatta | `PASS` |
| Perdita Q256 per asse, sonde incluse | 0,003906190395355 m | < 1/256 m | `PASS` |
| Perdita Q256 X/Z | 0,004269108137925 m | < √2/256 m | `PASS` |
| Hausdorff rettifili | 0,004277268693811 m | ≤ 1,0 m | `PASS` |
| Raggio planare | 9.888,000282196546 m | ≤ 10.000 m | `PASS` |

L'aggregato ha `comparisonValidation: PASS`, nessun failure e stato
`AWAITING_MANUAL_VALIDATION`. L'aritmetica `X=E, Y=H, Z=-N` è verificata, ma
la sua semantica geografica visuale e la stabilità Q256 dopo editor restano
aperte. Il [verbale del rerun](../../tasks/poc-002-revised-rerun-results.md), il
[riepilogo machine-readable](evidence/revised-rerun-automatic-validation.json)
e la [checklist Windows](manual-validation/revised-rerun-checklist.md)
conservano risultati, hash e procedura esatta.

## Esecuzione automatica v1 (storica)

```bash
cd spikes/poc-002-coordinate-geometry
PROJ_NETWORK=OFF .venv/bin/python -m unittest discover -s tests -v
PROJ_NETWORK=OFF .venv/bin/python run_automatic.py

cd csharp
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
dotnet format --verify-no-changes --no-restore
dotnet run --configuration Release --no-build -- \
  ../output/run-automatic/neutral-model.json \
  ../output/run-automatic/<fresh-native-run>
dotnet run --configuration Release --no-build -- \
  --quantizer-rca \
  ../output/run-automatic/native-final/adapter-validation.json \
  ../evidence/native-q256-validation.json
```

Il comando di generazione con `<fresh-native-run>` termina intenzionalmente con
codice `2` dopo avere scritto `adapter-validation.json`, perché la misura
nativa fallisce. Non aumentare la soglia e non trattare tale codice come un
problema di esecuzione. Il successivo comando `--quantizer-rca` analizza il
manifest esistente e sonde `Node` in memoria; non rigenera le mappe.

Il run scelto ha prodotto sei `.mbd`, trenta file di settore
`.aux/.base/.data/.desc/.snd` e un manifest. Tutte le mappe sono formato 907;
contengono in totale otto Road, sedici nodi terminali privati e nessuna
connessione condivisa.

## Risultati essenziali v1 (storici)

| Misura | Risultato | Limite | Esito |
| --- | ---: | ---: | --- |
| Round-trip WGS84 massimo | 0,000000001578416 m | 0,001 m | `PASS` |
| Discretizzazione proiettata massima | 0,001618855054 m | 0,01 m | `PASS` |
| Errore rapporto di scala massimo | 1,3878×10⁻¹⁷ | budget numerico | `PASS` |
| `double` → `Vector3` | 0,000379305098 m | 0,001 m | `PASS` |
| TruckLib `Save/Open` complessivo | 0,004277268694 m | 0,001 m | **`FAIL`** |
| Hausdorff rettifili | 0,004277268694 m | 1,0 m | `PASS` |
| Raggio planare massimo | 9888,000282197 m | 10.000 m | `PASS` |

Il readback ha collocato ogni componente osservata su una griglia esatta di
`1/256 m = 0,00390625 m`. La
[`RCA Q256`](evidence/native-q256-rca.md), svolta il 2 settembre 2026, ha poi
confermato nel sorgente corrispondente al pacchetto TruckLib 0.5.1 e con 45
sonde dirette che `Node.Serialize` applica, indipendentemente a X/Y/Z,
`trunc(float32_axis*256)/256`. Tutte le 48 componenti degli endpoint già
generati coincidono esattamente con la regola. La conclusione non viene estesa
al formato ETS2 in generale, al Map Editor o ad altre versioni TruckLib.

Il limite teorico per asse del troncamento è `< 0,00390625 m`; anche un
ipotetico nearest avrebbe un limite per asse di `0,001953125 m`. La garanzia
generale v1 di 1 mm non è quindi realizzabile per coordinate arbitrarie in
questa rappresentazione. Alla data della RCA, un modello
componente-per-componente Q256 e un criterio separato di assenza di drift
post-editor erano proposti ma non applicati. La successiva
[revisione DT-07](../../tasks/prd-osm2ets2-mvp.md) ha adottato quel modello per
un nuovo rerun, specificato in
[`revised-rerun-spec.md`](revised-rerun-spec.md). La sua parte automatica è
stata eseguita il 3 settembre 2026 ed è ora
`AWAITING_MANUAL_VALIDATION`; PoC-002 v1 resta `FAIL`.

Nel run v1 l'aritmetica dell'adapter applica correttamente
`X=E, Y=H, Z=-N`, mentre il criterio numerico allora congelato fallisce. Nel
rerun revisionato l'aritmetica e il modello Q256 passano; l'orientamento
semantico nel Map Editor resta comunque non verificato.

Il verbale completo è in
[`tasks/poc-002-results.md`](../../tasks/poc-002-results.md). Le fonti upstream
verificate sono in
[`evidence/upstream-verification.md`](evidence/upstream-verification.md). La
causa della perdita di precisione e il report machine-readable sono in
[`evidence/native-q256-rca.md`](evidence/native-q256-rca.md) e
[`evidence/native-q256-validation.json`](evidence/native-q256-validation.json).
La procedura Windows predisposta, ma non eseguita, è in
[`manual-validation/checklist.md`](manual-validation/checklist.md).

Il verbale distinto del rerun revisionato è in
[`tasks/poc-002-revised-rerun-results.md`](../../tasks/poc-002-revised-rerun-results.md);
la sua checklist ancora da eseguire è
[`manual-validation/revised-rerun-checklist.md`](manual-validation/revised-rerun-checklist.md).
