# PoC-002 — RCA della precisione nativa Q256

**Stato PoC-002 v1 sotto i criteri originali congelati: `FAIL`.**

**Data RCA: 2 settembre 2026.** Questa analisi chiude la causa tecnica del
fallimento automatico già osservato. Alla data della sua redazione non
modificava DT-07, non alzava la soglia di 0,001 m, non costituiva un nuovo run
di PoC-002 e non autorizzava PoC-003. La decisione PRD successiva è registrata
in fondo senza reinterpretare il run v1.

## Conclusione

Il pacchetto effettivamente usato, TruckLib 0.5.1, riceve ogni componente di
`Node.Position` come `System.Single` e la serializza come `Int32` signed con:

```text
n = (int)(float32_scene_axis * 256f)
q = n / 256f
```

Per input finiti e nel dominio del PoC, il cast C# tronca verso zero. La regola
confermata è quindi:

```text
expected_native_axis = trunc_toward_zero(float32_scene_axis * 256) / 256
```

X, Y e Z sono trattati separatamente e nello stesso ordine. Il passo è
`Δ = 1/256 m = 0,00390625 m`. Il requisito DT-07 corrente, inteso come garanzia
generale di errore nativo non superiore a 0,001 m per coordinate arbitrarie, è
matematicamente incompatibile con questa rappresentazione: il solo
troncamento può avvicinarsi a 0,00390625 m su un asse. Coordinate particolari
allineate alla griglia possono essere esatte, ma non rendono realizzabile la
garanzia generale. Pre-allineare ogni coordinata potrebbe rendere nullo il
solo errore del writer, ma introdurrebbe una modifica geometrica esplicita e
non soddisfa automaticamente il confronto vigente con le coordinate float64;
richiederebbe la medesima decisione PRD, non un aggiustamento nascosto.

## Identità del codice realmente eseguito

Il file [NuGet TruckLib 0.5.1](https://www.nuget.org/packages/TruckLib/0.5.1)
installato dichiara nel `.nuspec` il commit repository
`bd745344fc52d3b2d70ce9ac7c88d61b99934805`. L'assembly espone
`0.5.1.0+bd745344fc52d3b2d70ce9ac7c88d61b99934805` come versione informativa.
Il commit coincide sia con il tag upstream `v0.5.1` sia con il SourceLink nel
PDB del pacchetto.

| Evidenza locale/upstream | Valore |
| --- | --- |
| SHA-256 `.nupkg` usato | `19a55b329c9448cc2d35ee85f0e553c43be271ea6c46a6f3ba6956328660f743` |
| SHA-256 `.nuspec` usato | `c6c9e401eca625e887138a28c8aa3c129bbe494665e4e281738b86e80e820669` |
| Tag/commit | `v0.5.1` / `bd745344fc52d3b2d70ce9ac7c88d61b99934805` |
| Git blob `Node.cs` | `a2c327167e8a8fc354a8f5a94fe23424b0a02ed5` |
| Git blob `Map.cs` | `0069f78c613a54a49f5fa44cb91988c1e796309e` |

Nel [sorgente immutabile di `Node`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Node.cs#L353-L397):

- `fixedPointFactor` è il literal `float` `256f`;
- `Deserialize` legge tre `Int32`, li divide per `256f` e costruisce il
  `Vector3` X/Y/Z;
- `Serialize` scrive tre volte
  `(int)(Position.<asse> * fixedPointFactor)` per X/Y/Z.

[`Map.WriteNodes`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Map.cs#L1106-L1117)
invoca direttamente `node.Serialize`; il percorso di apertura usa
[`ReadNodes` → `node.Deserialize`](https://github.com/sk-zk/TruckLib/blob/bd745344fc52d3b2d70ce9ac7c88d61b99934805/TruckLib/ScsMap/Map.cs#L755-L772).
[`BinaryWriter.Write(Int32)`](https://learn.microsoft.com/en-us/dotnet/api/system.io.binarywriter.write?view=net-10.0)
produce quattro byte little-endian signed e il reader applica l'operazione
inversa. La [conversione numerica esplicita C#](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions#explicit-numeric-conversions)
da `float` finito e nel range a intero tronca verso zero; `Vector3` usa
[componenti `Single`](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.vector3.x?view=net-10.0).

La conclusione è delimitata alle posizioni dei `Node` scritte nei settori
`.base/.aux/.snd` da TruckLib 0.5.1. Non viene generalizzata a ogni coordinata
del formato ETS2, a versioni differenti di TruckLib o al comportamento del Map
Editor.

## Caratterizzazione del quantizzatore

Sia `f` la componente float32 in ingresso, `k` un intero positivo e
`Δ=1/256 m`.

- per `f ≥ 0`, `Q(f)=floor(f/Δ)·Δ`: la cella `[kΔ,(k+1)Δ)` va a `kΔ`;
- per `f < 0`, `Q(f)=ceil(f/Δ)·Δ`: la cella `(-(k+1)Δ,-kΔ]` va a `-kΔ`;
- `(-Δ,+Δ)` va a zero; anche `-float.Epsilon` viene riletto come `+0`;
- a un bordo positivo `b=kΔ`: appena sotto va a `b-Δ`, sul bordo e appena
  sopra vanno a `b`;
- a un bordo negativo `b=-kΔ`: appena sotto, cioè più negativo, va a `b`; sul
  bordo va a `b`; appena sopra, verso zero, va a `b+Δ`.

Quindi la regola non è un `floor` globale: sui valori negativi usa il verso
opposto. Non è neppure un arrotondamento al più vicino.

Il test diagnostico chiama direttamente `Node.Serialize` e `Node.Deserialize`
dell'assembly NuGet, legge anche i tre `Int32` grezzi e ripete ogni caso
indipendentemente su X, Y e Z, mantenendo a zero gli altri assi. I 15 scalari
sono: i due vicini float immediati dello zero, zero, ±0,001, ±0,01, ±0,1 e i
valori float immediatamente sotto, sul e sopra i bordi ±1 m.

| Caso discriminante | Bit float input | Intero | Readback |
| --- | --- | ---: | ---: |
| appena sotto `+1` | `0x3F7FFFFF` | 255 | 0,99609375 m |
| `+1` | `0x3F800000` | 256 | 1 m |
| appena sopra `+1` | `0x3F800001` | 256 | 1 m |
| appena sotto `-1` (più negativo) | `0xBF800001` | -256 | -1 m |
| `-1` | `0xBF800000` | -256 | -1 m |
| appena sopra `-1` (verso zero) | `0xBF7FFFFF` | -255 | -0,99609375 m |

Risultato: **45/45** sonde corrispondono al troncamento verso zero; soltanto
27/45 corrispondono a `floor` e 27/45 a nearest-even. Il primo caso positivo
appena sotto il bordo esclude nearest; il caso negativo appena sopra esclude
`floor`. Le triple grezze dimostrano lo stesso comportamento su ciascuno dei
tre assi.

Il manifest machine-readable completo è
[`native-q256-validation.json`](native-q256-validation.json), SHA-256
`e64ab191fc03d90248e01c7b377caee1abf6104d8e5718c7d80ce73e77ebd137`.
Un secondo output in `/private/tmp` è risultato byte-per-byte identico.

## Verifica sulle fixture PoC-002 esistenti

Il diagnostico ha letto, senza modificarlo, il manifest nativo già scelto
`output/run-automatic/native-final/adapter-validation.json`, SHA-256
`d0f412c2c9ffc9d5404ffe0cbb0a4825e6ad78b90ada1bf3b30e4c5610ec0b1f`.
Ha controllato 8 Road, 16 endpoint e tutte le 48 componenti X/Y/Z:

- 48/48 componenti di readback sono su Q256, residuo massimo `0 m`;
- 48/48 coincidono esattamente con
  `trunc(float32_input * 256) / 256`, residuo massimo `0 m`.

Il massimo appartiene all'endpoint obliquo a scala 1:

| Stadio | X (m) | Y (m) | Z (m) |
| --- | ---: | ---: | ---: |
| scena float64 | 193,81569474576932 | 0 | -256,0380003011508 |
| input `Vector3` float32 | 193,81568908691406 | 0 | -256,0379943847656 |
| readback Q256 | 193,8125 | 0 | -256,03515625 |

Dal float32 al readback, gli spostamenti sono `-0,0031890869140625 m` su X e
`+0,002838134765625 m` su Z: entrambi vanno verso zero. La loro norma X/Z è
`0,004269108137924589 m`. La conversione float64 → float32 dell'endpoint vale
separatamente `0,000008186956489898853 m`; il confronto originario float64 →
readback vale `0,004277268693810707 m`.

## Limiti teorici e coerenza del valore osservato

La moltiplicazione/divisione per la potenza di due 256 è esatta per i float32
finiti nel dominio PoC. Con `|asse| ≤ 10.000 m`, l'intero scalato non supera
2.560.000, quindi resta anche sotto `2^24` ed è rappresentabile esattamente
come float32 al readback. Questa osservazione non viene estesa ai limiti estremi
di `Int32`. Rispetto all'input float32, il troncamento ha errore assoluto su
ciascun asse strettamente minore di `Δ`; il valore `Δ` è un estremo superiore,
non un massimo raggiunto.

| Regola | Per asse | X/Z euclideo | 3D euclideo |
| --- | ---: | ---: | ---: |
| troncamento Q256 osservato | `< 0,00390625 m` | `< 0,005524271728019903 m` | `< 0,0067658234670659265 m` |
| nearest Q256 ipotetico | `≤ 0,001953125 m` | `≤ 0,0027621358640099515 m` | `≤ 0,0033829117335329633 m` |

Il contributo Q256 osservato, `0,004269108137924589 m`, è inferiore al limite
X/Z del troncamento (`0,005524271728019903 m`) ed è quindi pienamente
coerente. È inoltre superiore perfino al limite 3D di un ipotetico nearest
(`0,0033829117335329633 m`), che viene così escluso anche dal dato oltre che
dal sorgente. Il valore complessivo `0,004277268693810707 m` include il distinto
passaggio float64 → float32 e non va attribuito interamente al fixed-point.

## Budget separati

Questa RCA non somma né confonde massimi provenienti da fixture diverse.

| Stadio | Massimo osservato | Stato/interpretazione corrente |
| --- | ---: | --- |
| WGS84 ↔ AEQD, round-trip | 0,000000001578416 m | `PASS` rispetto a 0,001 m |
| discretizzazione della geometria proiettata, pre-scala | 0,001618855054 m | `PASS` rispetto a 0,01 m |
| float64 scena → `Vector3` float32 | 0,0003793050975375676 m | `PASS` rispetto a 0,001 m |
| float32 → fixed-point Q256 → readback | 0,004269108137924589 m | quantizzazione ora spiegata; il confronto DT-07 congelato resta `FAIL` |
| Map Editor dopo recompute/save/close/reopen | non eseguito | nessuna misura disponibile; nessuna deriva può essere dichiarata |

L'errore radiale della proiezione e la discretizzazione erano stati misurati
separatamente dal run Python. Il fixed-point è successivo alla conversione
float32. Un futuro errore post-editor sarà un confronto di persistenza
separato, non un nuovo permesso di quantizzare la stessa coordinata.

## Modello di precisione proposto (stato alla data della RCA)

Alla chiusura della RCA serviva una decisione esplicita di architettura/PRD
prima di modificare DT-07 o rieseguire PoC-002. La proposta tecnica era
scomporre il requisito:

1. mantenere una misura autonoma del passaggio float64 neutro → float32
   dell'adapter, senza assorbirla nella quantizzazione;
2. per TruckLib 0.5.1, calcolare per ogni asse il codice atteso
   `n = trunc_toward_zero(float32_scene_axis * 256)` e richiedere uguaglianza
   esatta dell'`Int32` scritto o del readback `n/256`;
3. dichiarare esplicitamente il vincolo componente per componente
   `|Q(f)-f| < 1/256 m`, mantenendo i limiti X/Z e 3D derivati come diagnostica,
   non sostituendo 1 mm con una tolleranza arbitraria;
4. conservare separati i budget WGS84/proiezione, discretizzazione, float32,
   Q256 e persistenza editor.

Alla data della RCA questa proposta **non era DT-07 vigente**, non cambiava
l'esito `FAIL` e non era stata inserita nel PRD.

Una possibile formulazione da sottoporre alla decisione, senza applicarla, è:

> I budget di andata/ritorno geografico (1 mm) e discretizzazione proiettata
> (1 cm pre-scaling) restano invariati. L'adapter misura separatamente la
> conversione float64 → float32, con errore 3D aggiunto massimo 1 mm della
> scena. Per ogni componente `a` di `Node.Position` con TruckLib 0.5.1,
> `n_a = trunc_toward_zero(float32(a) * 256)` e la posizione serializzata attesa
> è esattamente `n_a/256`; writer e readback devono coincidere per componente
> con tale codice. L'errore intrinseco Q256 rispetto all'input float32 è
> `<1/256 m` per asse, con limiti derivati `<sqrt(2)/256 m` nel piano X/Z e
> `<sqrt(3)/256 m` in 3D. Dopo il ciclo Map Editor, ogni codice Q256 deve restare
> identico al codice atteso pre-editor; non si applica un secondo budget di
> quantizzazione.

Per un futuro criterio post-editor, l'aspettativa più verificabile è confrontare
ogni `Int32` Q256 dopo **Recompute map → save → chiusura completa → riapertura**
con il codice Q256 pre-editor già atteso. Nel dominio del PoC, Q256 è
idempotente: `Q(Q(f))=Q(f)`. Non va quindi concesso automaticamente un secondo
budget `Δ` a ogni salvataggio. La proposta è richiedere uguaglianza per asse;
qualsiasi cambio di codice è drift aggiuntivo, salvo una diversa trasformazione
nativa dimostrata e approvata esplicitamente.

## Comandi diagnostici eseguiti

Dalla checkout upstream temporanea usata per confrontare pacchetto, tag e
sorgente:

```bash
git -C /private/tmp/osm2ets2-trucklib-051.YSOIzS/source \
  rev-parse HEAD 'v0.5.1^{commit}'
git -C /private/tmp/osm2ets2-trucklib-051.YSOIzS/source \
  describe --exact-match --tags HEAD
git -C /private/tmp/osm2ets2-trucklib-051.YSOIzS/source \
  hash-object TruckLib/ScsMap/Node.cs TruckLib/ScsMap/Map.cs
shasum -a 256 \
  <USER_HOME>/.nuget/packages/trucklib/0.5.1/trucklib.0.5.1.nupkg \
  <USER_HOME>/.nuget/packages/trucklib/0.5.1/trucklib.nuspec
```

Dalla directory `spikes/poc-002-coordinate-geometry/csharp/`:

```bash
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --self-test
dotnet run --configuration Release --no-build -- \
  --quantizer-rca \
  ../output/run-automatic/native-final/adapter-validation.json \
  ../evidence/native-q256-validation.json
dotnet format --verify-no-changes --no-restore
```

La verifica di riproducibilità è stata eseguita scrivendo due report in
`/private/tmp`, confrontandoli con `cmp -s` e calcolandone SHA-256. Non sono
stati chiamati `run_automatic.py`, la generazione nativa `Map.Save` o il Map
Editor.

## Provenienza delle conclusioni

- **Repository canonico:** soglia DT-07 e stato `FAIL` congelato.
- **Upstream:** commit, tipi e istruzioni precise di serializzazione/readback.
- **Esperimento:** byte, codici, valori di bordo e corrispondenza delle 48
  componenti esistenti.
- **Matematica:** intervalli e limiti euclidei derivati da `Δ`.
- **Proposta alla data della RCA:** modello a stadi e criterio post-editor;
  non erano ancora decisioni.

## Decisione architetturale successiva

Il 2 settembre 2026 la revisione canonica
[DT-07](../../../tasks/prd-osm2ets2-mvp.md) ha selezionato la terza alternativa:
modellare esplicitamente la quantizzazione deterministica Q256 al confine
dell'adapter TruckLib 0.5.1. Le altre alternative — cambiare
writer/rappresentazione per mantenere 1 mm oppure pre-allineare la geometria
alla griglia — non sono state adottate.

La decisione è prospettica. PoC-002 v1 resta `FAIL`; il nuovo rerun è
`NOT_EXECUTED` e segue la
[`specifica congelata`](../revised-rerun-spec.md). Q256 resta provato soltanto
per TruckLib 0.5.1 `TruckLib.ScsMap.Node.Position`; semantica geografica degli
assi e persistenza post-editor restano aperte. Nessun test o output è stato
rigenerato per questa riconciliazione documentale.
