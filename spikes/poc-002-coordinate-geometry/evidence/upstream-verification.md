# PoC-002 — Verifica documentazione upstream

Verifica eseguita il **1 settembre 2026**, prima dell'implementazione sotto test.
Le fonti sono separate dalle decisioni canoniche del repository, dalle misure
dell'esperimento e dalle inferenze.

## Fatti canonici del repository

- Il contratto numerico, l'origine, il clipping WGS84, la densificazione, la
  scala e i budget derivano da `tasks/prd-osm2ets2-mvp.md`, DT-07/DT-08.
- Scope, fixture, criteri e stato del gate derivano esclusivamente dalla sezione
  PoC-002 di `tasks/spikes-osm2ets2-mvp.md`.
- PoC-001 ha dimostrato soltanto una Road rettilinea minimale con TruckLib
  0.5.1, formato 907, su ETS2 1.60.1.7. Non ha dimostrato la convenzione
  geografica degli assi o la precisione di coordinate arbitrarie.

## Fatti verificati nelle fonti upstream

### pyproj e PROJ

- [`Transformer` di pyproj 3.7.2](https://pyproj4.github.io/pyproj/3.7.2/api/transformer.html)
  documenta che l'ordine degli assi di autorità può differire; `always_xy=True`
  mantiene longitudine/latitudine e easting/northing nell'API. `errcheck=True`
  rende gli errori espliciti invece di accettare valori infiniti.
- [PROJ AEQD](https://proj.org/en/stable/operations/projections/aeqd.html)
  documenta forma ellissoidale, centro `lat_0`/`lon_0`, falsi est/nord
  `x_0`/`y_0` e unità metriche. Poiché la pagina indica GRS80 come ellissoide
  predefinito, il PoC specifica esplicitamente `+ellps=WGS84`.
- [`pyproj.network`](https://pyproj4.github.io/pyproj/3.7.2/api/network.html)
  espone `set_network_enabled(False)` e `is_network_enabled()`. Il PoC usa anche
  `PROJ_NETWORK=OFF` come difesa aggiuntiva.
- [EPSG ellipsoid 7030](https://epsg.org/ellipsoid_7030/WGS-84.html) fissa per
  WGS84 semiasse maggiore `6378137 m` e inverso dello schiacciamento
  `298.257223563`.
- [GeographicLib `GeodesicProj`](https://geographiclib.sourceforge.io/html/GeodesicProj.1.html)
  descrive la proprietà radiale equidistante della AEQD. Non implica che ogni
  distanza fra due punti esterni al centro sia conservata esattamente.

### Riferimento geodetico indipendente

- La procedura [NOAA/NGS INVERSE/FORWARD](https://www.ngs.noaa.gov/PC_PROD/Inv_Fwd/readme.htm)
  usa gli algoritmi diretti/inversi di Vincenty e consente un ellissoide
  definito dall'utente. `reference/freeze_reference.py` implementa quel metodo
  con la sola libreria standard e congela risultati e hash prima di usare
  pyproj.
- [GeographicLib](https://geographiclib.sourceforge.io/doc/library.html) è una
  fonte utile, ma parte della sua implementazione geodetica è incorporata in
  PROJ; non viene quindi usata come unico riferimento indipendente.

### TruckLib e Map Editor

- [TruckLib 0.5.1 su NuGet](https://www.nuget.org/packages/TruckLib/0.5.1)
  dichiara .NET 10 e formato mappa 907 per ETS2/ATS 1.59–1.60; i metadati
  puntano al commit `bd745344fc52d3b2d70ce9ac7c88d61b99934805`.
- Il sorgente fissato di [`Map`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Map.cs)
  e [`Road`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Road.cs)
  conferma le API `new Map()`, `Map.Open`, `Map.Save` e `Road.Add`, e la
  settorizzazione nel piano X/Z.
- [`System.Numerics.Vector3`](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.vector3?view=net-10.0)
  usa componenti `Single`; la conversione da float64 deve essere misurata.
- Le [limitazioni TruckLib](https://github.com/sk-zk/TruckLib#known-issues-and-limitations)
  richiedono `Map > Recompute map`, perché la libreria non calcola tutte le
  bounding box degli item.
- I [comandi editor SCS](https://modding.scssoft.com/wiki/Documentation/Engine/Console/Commands/Editor)
  documentano rebuild/check e save. La
  [guida al salvataggio](https://modding.scssoft.com/wiki/Tutorials/Map_Editor/Introduction_to_the_Map_Editor/Saving,_Loading,_Sectors,_and_Files)
  documenta salvataggio, uscita, rilancio e riapertura. Le
  [scorciatoie correnti](https://modding.scssoft.com/wiki/Documentation/Tools/Map_Editor/Shortcuts)
  trattano Y come altezza e X/Z come piano orizzontale.

## Inferenze ancora aperte il 1 settembre 2026

- L'ambiente risolto deve registrare la propria versione PROJ; la versione
  della build della documentazione pyproj non viene assunta.
- Nessuna fonte TruckLib o SCS stabilisce `X=E, Y=H, Z=-N`, il segno del nord o
  la precisione millimetrica dopo il salvataggio editor. Sono ipotesi del PoC.
- Prima dell'implementazione, la spaziatura binary32 vicino a 10.000 m e
  l'errore della serializzazione fixed-point erano inferenze da misurare sui
  file reali, non soglie da aumentare. La seconda è stata poi risolta dalla RCA
  del 2 settembre descritta sotto; questa formulazione conserva la sequenza
  storica delle verifiche.
- Il readback TruckLib è diagnostico. Soltanto il ciclo completo Windows
  apertura → ispezione → recompute → save → chiusura → riapertura → readback
  numerico può chiudere la parte editor del gate.

## Verifica successiva: RCA Q256 del 2 settembre 2026

Il manifest automatico ha mostrato un errore oltre soglia e coordinate sulla
griglia 1/256 m. Una verifica successiva del pacchetto effettivamente risolto ha
collegato il `.nuspec`, il SourceLink e la versione informativa dell'assembly al
commit immutabile TruckLib
`bd745344fc52d3b2d70ce9ac7c88d61b99934805`, coincidente con `v0.5.1`.

Nel [`Node.cs` di quel commit](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Node.cs#L353-L397),
`Node.Serialize` scrive X, Y e Z come
`(int)(Position.<axis> * 256f)` e `Node.Deserialize` legge ciascun `Int32` e
divide per `256f`. [`Map.WriteNodes`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Map.cs#L1106-L1117)
e [`Map.ReadNodes`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Map.cs#L755-L772)
invocano direttamente quei metodi. La documentazione Microsoft conferma che
la [conversione esplicita da floating point a intero](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions#explicit-numeric-conversions)
tronca verso zero per gli input finiti e nel range.

La regola Q256 è quindi ora un fatto upstream e sperimentale confermato per
`Node.Position` e per il writer TruckLib 0.5.1 esercitato. Non è una specifica
generale di ogni coordinata ETS2, del Map Editor o di altre versioni. Prove,
limiti matematici e proposta non ancora adottata alla data della RCA sono in
[`native-q256-rca.md`](native-q256-rca.md). La successiva revisione canonica
[DT-07](../../../tasks/prd-osm2ets2-mvp.md) ha adottato prospetticamente il
modello Q256 esplicito per un nuovo rerun, tuttora `NOT_EXECUTED`; non modifica
queste fonti né il `FAIL` di PoC-002 v1.
