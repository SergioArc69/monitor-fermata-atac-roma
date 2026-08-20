# Monitor Fermata ATAC Roma

App desktop per Windows che mostra in tempo reale gli orari di arrivo degli autobus (ATAC / Roma TPL) a una fermata di Roma, usando i feed **GTFS** e **GTFS-RT** open data di [Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset).

## Novità !
### Ho pubblicato anche la versione "sorella" [Monitor Fermata ATAC Roma — Chrome-Extension](https://github.com/SergioArc69/monitor-fermata-roma_chrome-ext), pensata per essere installata nei browser "Chromium", disponibile sul [Chrome Web Store](https://chromewebstore.google.com/detail/gggjfmkiafdeembhhglhgeignejpjdnc).


## Funzionalità

- 🔍 Ricerca fermate per codice o per nome, con suggerimenti live e ultime fermate monitorate (MRU)
- 🕒 Orari di arrivo in tempo reale, raggruppati per linea, con stato del ritardo
- 🔔 Notifiche desktop configurabili (minuti di preavviso e fascia oraria)
- 🚏 Filtro per linea, utile nelle fermate con molte linee
- 🗺️ Mappa interattiva: cerca le fermate vicino a te, oppure segui in tempo reale posizione e stato (fermo/in movimento) dei bus in arrivo alla fermata monitorata
- 🗂️ System tray: l'app resta in esecuzione in background
- ⚙️ Avvio automatico con Windows, aggiornamento dati linee/fermate su richiesta, sessione salvata tra un riavvio e l'altro

## Installazione

Scarica l'ultimo installer dalla pagina [Releases](https://github.com/SergioArc69/monitor-fermata-atac-roma/releases) ed eseguilo.

L'app è pubblicata come eseguibile *self-contained*: non richiede di installare .NET separatamente. Per la funzione mappa serve **Microsoft Edge WebView2 Runtime**, quasi sempre già presente su Windows 10/11 aggiornati; se mancante, l'installer lo segnala con il link per scaricarlo.

L'installazione è per-utente (in `%LocalAppData%`): **non servono permessi di amministratore**.

## Requisiti per compilare dai sorgenti

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (Windows)
- Visual Studio 2022+ (consigliato) oppure `dotnet build` da riga di comando

```bash
dotnet build MonitorFermataAtacRoma.csproj
```

Per generare il pacchetto di installazione (richiede [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```bash
pwsh installer\build.ps1
```

## Fonte dati

[Open data GTFS / GTFS-RT di Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset) — nessuna API key richiesta. I dati di fermate e linee vengono aggiornati periodicamente in locale; gli orari di arrivo e le posizioni dei bus sono sempre in tempo reale.

La mappa usa le tile di [OpenStreetMap](https://www.openstreetmap.org/copyright), © collaboratori di OpenStreetMap, distribuite con licenza [ODbL](https://opendatacommons.org/licenses/odbl/).

## Licenza

Pubblico dominio — vedi [LICENSE](LICENSE) ([The Unlicense](https://unlicense.org)). Usa, modifica e ridistribuisci liberamente questo software, per qualsiasi scopo.

## Autore

Sergio Arcangeli
