# PoC-001 — Checklist Map Editor ETS2 1.60.x

**Esito: `PASSED`.** Questa checklist conserva il protocollo usato per entrambi
i run. Il verbale distingue le evidenze effettivamente archiviate, compresa
l'assenza di screenshot, e riporta il confronto pre/post editor in
[`results.md`](results.md). PoC-002 non è stato iniziato.

## 1. Congelare la baseline Windows

Usare Windows 11 x64, ETS2 stabile 1.60.x e gioco base. Prima della prova:

1. registrare data, operatore, macchina e versione completa mostrata in
   `editor.log.txt`; per le sessioni Map Editor questo è il log rilevante;
2. registrare dimensione e SHA-256 di `base.scs`, `def.scs` e `version.scs`;
3. confermare dal log che non sono caricati mod locali o Workshop estranei;
4. usare TruckLib 0.5.1 e .NET 10 con `dotnet restore --locked-mode`;
5. copiare `output/run-01/` e `output/run-02/` in posizione di sola lettura o
   conservarne gli hash prima di aprire l'editor.

Non copiare nel repository archivi `.scs`, mod, modelli, texture o altri asset
SCS. I due run hanno lo stesso nome mappa e vanno provati uno alla volta.

## 2. Preparare `run-01`

Eseguire la prova su una copia. La
[collocazione documentata da TruckLib](https://sk-zk.github.io/trucklib/master/docs/TruckLib.ScsMap/map-class.html)
è la directory `map` del mod `user_map`:

```text
<ETS2 home>/mod/user_map/
└── map/
    ├── poc001_minimal.mbd
    └── poc001_minimal/
        ├── sec+0000+0000.aux
        ├── sec+0000+0000.base
        ├── sec+0000+0000.data
        ├── sec+0000+0000.desc
        └── sec+0000+0000.snd
```

`<ETS2 home>` è la home effettiva riportata dal gioco; normalmente è sotto la
cartella Documenti dell'utente. Fare prima il backup di un eventuale
`user_map`. Non unire i file a una mappa personale e non creare manualmente
`.layer`, `.expa`, `.set` o altri file mancanti.

Salvare un inventario pre-editor con percorsi, dimensioni e SHA-256. In
PowerShell, dalla directory del set copiato:

```powershell
Get-ChildItem -File -Recurse | Get-FileHash -Algorithm SHA256 | Export-Csv before-files.csv -NoTypeInformation
```

## 3. Apertura e ispezione

Avviare l'eseguibile Windows x64 della stessa installazione seguendo i
[parametri documentati da SCS](https://modding.scssoft.com/wiki/Tutorials/Map_Editor/Introduction_to_the_Map_Editor/Launching_the_Map_Editor):

```text
<ETS2 install>\bin\win_x64\eurotrucks2.exe -edit poc001_minimal -noworkshop
```

Se l'apertura diretta non trova la mappa, conservare il log e verificare la
collocazione con **File > Open**. Non spostare o aggiungere file per tentativi
senza registrare quale documentazione o comportamento dell'editor giustifica la
modifica.

Nel Map Editor:

1. confermare che la mappa si apre senza errore bloccante;
2. localizzare la strada vicino a `(100, 0, 100)`–`(200, 0, 100)`;
3. acquisire una schermata panoramica e una con la strada selezionata;
4. confermare che è un elemento Road nativo, selezionabile e modificabile;
5. registrare coordinate esposte, estremi, orientamento, tipo/look/variante e
   gli UID visibili o ottenibili dagli strumenti della build target;
6. confrontare gli UID attesi con `automatic-validation.json` senza dedurli
   dall'aspetto grafico;
7. conservare subito `editor.log.txt` con un nome che identifichi run e fase.

Nomi consigliati per le immagini:

- `run-01-01-open.png`;
- `run-01-02-road-selected.png`.

## 4. Recompute, salvataggio e riapertura

1. Eseguire **Map > Recompute map**.
2. Registrare e classificare ogni warning o errore; acquisire
   `run-01-03-after-recompute.png`.
3. Salvare con **File > Save** senza ricreare, sostituire o riparare la strada.
4. Chiudere completamente il Map Editor e verificare che il processo sia
   terminato.
5. Copiare l'intero set salvato in
   `manual-validation/run-01/after-editor/map/`, mantenendo `.mbd` e cartella dei
   settori come fratelli.
6. Salvare inventario, dimensioni, SHA-256 e log come evidenza post-editor.
7. Riaprire con lo stesso comando e ripetere selezione e controlli; acquisire
   `run-01-04-reopened-road-selected.png` e
   `run-01-reopen-editor.log.txt`.
8. Chiudere nuovamente l'editor.

Eseguire quindi, dalla directory dello spike:

```powershell
dotnet run -- --validate-editor-save manual-validation\run-01\after-editor\map\poc001_minimal.mbd output\run-01\automatic-validation.json *> manual-validation\run-01\editor-save-readback.txt
```

`EDITOR_SAVE_TRUCKLIB_READBACK_PASSED` prova la rilettura semantica da parte di
TruckLib. `UID stability: CHANGED_REQUIRES_REVIEW` richiede un confronto
documentato; non va trasformato automaticamente in successo o fallimento.

## 5. Ripetere `run-02`

Dopo avere archiviato `run-01`, ripristinare un `user_map` pulito, copiare il
set originale di `run-02` e ripetere integralmente i punti 2–4. Usare gli stessi
nomi sostituendo `run-01` con `run-02`. Non riutilizzare file salvati nel primo
ciclo.

## 6. Matrice da compilare per ciascun run

| Criterio | `run-01` | `run-02` | Evidenza obbligatoria |
| --- | --- | --- | --- |
| Output aperto senza errori bloccanti | `PASSED` | `PASSED` | log apertura/salvataggio |
| Strada visibile | `PASSED` | `PASSED` | osservazione manuale |
| Strada nativa, selezionabile e modificabile | `PASSED` | `PASSED` | osservazione + UID nel log |
| Recompute completato | `PASSED` | `PASSED` | `Rebuild done` nei log |
| Salvataggio completato | `PASSED` | `PASSED` | log + output post-editor |
| Chiusura e riapertura riuscite | `PASSED` | `PASSED` | log separati di riapertura |
| Geometria e riferimenti validi dopo save | `PASSED` | `PASSED` | readback TruckLib |
| Nessuna riparazione manuale | `PASSED` | `PASSED` | verbale operatore |

Entrambe le colonne hanno superato tutti i criteri. Gli avvisi ambientali sono
classificati in [`results.md`](results.md); nessuno ha impedito apertura,
recompute, salvataggio, riapertura o readback. Il gate PoC-001 è quindi
`PASSED`.
