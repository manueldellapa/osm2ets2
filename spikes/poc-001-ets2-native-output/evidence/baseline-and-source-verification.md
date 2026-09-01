# PoC-001 — Baseline e verifica delle fonti

Rilevazione automatica: **1 settembre 2026**. Questi dati dimostrano versione,
API, catalogo consultato e generazione locale. Non dimostrano il comportamento
del Map Editor Windows.

## Input canonici congelati

| File | SHA-256 |
| --- | --- |
| `tasks/prd-osm2ets2-mvp.md` | `c5e8d6f1a51a8980a042e53b40bf49ee1dc0dc6c8c9d1521a5659e50432e1e97` |
| `tasks/spikes-osm2ets2-mvp.md` | `c04c6c88965b206ddd5cd28c55827af0a0925088231340d8a5c51c530bdec0e2` |

## Runtime e TruckLib

- macOS 26.6.2 ARM64;
- .NET SDK 10.0.400, runtime 10.0.11;
- archivio SDK ufficiale macOS ARM64, SHA-512
  `e440e9a58d4ff7741c8342ac3e086fa9ee2dadc25e01c0449a88317a74cfbd63625b8092c3b2a131ae14b16ab3401e9cc470e578e4c65a72a0b5786bd2308cde`;
- pacchetto NuGet TruckLib 0.5.1, SHA-256
  `19a55b329c9448cc2d35ee85f0e553c43be271ea6c46a6f3ba6956328660f743`;
- assembly `0.5.1.0`, informational version
  `0.5.1.0+bd745344fc52d3b2d70ce9ac7c88d61b99934805`;
- sorgente esatto indicato dal `.nuspec`: commit
  `bd745344fc52d3b2d70ce9ac7c88d61b99934805`;
- formato supportato nel sorgente esatto: `907`.

Il progetto usa una versione NuGet esatta (`[0.5.1]`) e
`packages.lock.json`. Versioni risolte e output della compilazione sono in
`packages.txt` e `build-release.txt`.

Fonti consultate:

- [TruckLib 0.5.1 su NuGet](https://www.nuget.org/packages/TruckLib/0.5.1);
- [formati dichiarati da TruckLib](https://sk-zk.github.io/trucklib/master/);
- [documentazione della classe Map](https://sk-zk.github.io/trucklib/master/docs/TruckLib.ScsMap/map-class.html);
- [API della classe Road](https://sk-zk.github.io/trucklib/master/api/TruckLib.ScsMap.Road.html);
- [sorgente Map al commit del pacchetto](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Map.cs);
- [sorgente Road al commit del pacchetto](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Road.cs);
- [sorgente Node al commit del pacchetto](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Node.cs).

Dal sorgente esatto sono state verificate, senza reflection inventata, le API
`new Map()`, `Road.Add(...)`, `Map.Save(...)` e `Map.Open(...)`. Sono inoltre
osservati:

- settori da 4000 unità motore, che TruckLib descrive come metri, scelti tramite
  X/Z;
- posizioni dei nodi serializzate come tre interi fixed-point con fattore 256;
- UID a 64 bit prodotti dai primi otto byte di un `Guid` generato dalla libreria;
- strada nel file `.base`, nodi associati in `.base` e payload della strada in
  `.data`;
- `.aux` e `.snd` sempre scritti dal serializer per il settore;
- `.desc` scritto come metadato del settore;
- `.layer` scritto solo se almeno un elemento non usa il layer predefinito 0;
- `.mbd` composto da header e metadati di mappa: UID editor, start position,
  un campo settore il cui significato è lasciato ignoto dallo stesso sorgente,
  start rotation, game tag, scale normale/città e correzione UI Europe.

Questa è una descrizione del comportamento di TruckLib 0.5.1, non una
specifica ufficiale del formato SCS.

## Catalogo ETS2 locale consultato

L'installazione legittima locale è macOS e quindi non sostituisce la baseline
Windows richiesta. Il log registra pack `1.60.1.7`, gioco `1.60.1.7s`
revisione `26c95e307fd5`, Steam build ID `23966373` e manifest depot base
`924276640658760907`.

| Archivio | Byte | SHA-256 |
| --- | ---: | --- |
| `base.scs` | 10,349,477,809 | `1b0ef3e11ac2fe1b6d1083337931d8ed79e0d730765d14004df4132ebf206c87` |
| `def.scs` | 26,846,220 | `d79ac4944bbbb810d3f81f390b9fbea0fc4eeb9845b4de2c616d28b151e46076` |
| `version.scs` | 12,757 | `a7fca9662bdbdbb106d38c695558eef6deee9560c5af358582cccfd8dc5dd4e1` |

Un probe con le API TruckLib `HashFs`, `Sii` e `Models` ha aperto
direttamente questi archivi base, senza leggere DLC o mod. Ha osservato:

- unità `road_look` `road.ger1` in
  `/def/world/road_look.template.sii`;
- template `/road_template/ger/ger_road_1.pmd` presente in `base.scs`;
- look `ger_1` e variante `broken_de` nel modello;
- edge `road_edge.ger_sh_15` in `/def/world/road_edge.sii` e fra gli edge
  compatibili di `road.ger1`.

L'output integrale è in `catalog-validation.txt`. Il `game.log.txt` disponibile
proveniva da una sessione ordinaria con mod attivi e non è stato trattato come
prova editor del PoC. Nessun asset o archivio proprietario è stato copiato nel
repository.

## Discrepanze aperte

La [guida SCS sui file di mappa](https://modding.scssoft.com/wiki/Tutorials/Map_Editor/Introduction_to_the_Map_Editor/Saving,_Loading,_Sectors,_and_Files)
descrive cinque file di settore comprendenti `.layer`, con `.snd` aggiuntivo in
presenza di suoni, e menziona anche file mappa `.epa` e `.set`. TruckLib 0.5.1
ha prodotto invece `.base`, `.data`, `.aux`, `.snd`, `.desc`, senza `.layer`, e
soltanto `.mbd` come file mappa. La documentazione TruckLib colloca inoltre
l'output in `user_map/map`, mentre quella pagina SCS mostra una struttura più
vecchia direttamente sotto `user_map`.

Non sono stati creati file artificiali per colmare la differenza. Soltanto il
ciclo nel Map Editor 1.60.x può determinare se il set TruckLib è sufficiente e
quali file vengono eventualmente creati al salvataggio.
