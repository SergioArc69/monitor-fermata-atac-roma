## Monitor Fermata ATAC Roma — v1.2.0

App desktop per Windows che mostra in tempo reale gli orari di arrivo dei mezzi ATAC / Roma TPL a una fermata, usando i feed **GTFS** e **GTFS-RT** open data di Roma Mobilità.

### Novità in questa versione

- 🗺️ **Nuovo motore per la mappa**: i tile gratuiti `tile.openstreetmap.org`, gestiti da volontari, hanno iniziato a bloccare il traffico generato dall'app secondo la loro [policy d'uso per app di terze parti](https://wiki.openstreetmap.org/wiki/Blocked). Dopo un primo tentativo con i tile raster di Wikimedia (rivelatisi troppo soggetti a rate-limit per un uso normale), la mappa ora usa [OpenFreeMap](https://openfreemap.org/) — gratuito, senza limiti di richieste e senza chiave API. OpenFreeMap serve però tile **vettoriali**, non immagini raster: il motore di rendering della mappa è stato quindi sostituito, da Leaflet a **MapLibre GL JS**. Come effetto collaterale positivo, sulla mappa compaiono ora anche i nomi delle vie e delle località.

### Correzioni

- Rimosso un avviso innocuo ma ripetuto in console, relativo ad alcune icone del nuovo stile della mappa non presenti nel set di icone associato.

### Funzionalità

- 🔍 **Ricerca fermate** per codice, nome o numero di linea, con suggerimenti live e ultime fermate monitorate (MRU)
- 🕒 **Orari di arrivo in tempo reale e schedulati**, raggruppati per linea, con stato del ritardo, e fallback automatico sull'orario del giorno successivo quando il servizio odierno è terminato
- 🔔 **Notifiche desktop** configurabili (minuti di preavviso e fascia oraria) quando un bus sta per arrivare
- 🚏 **Filtro per linea**, utile nelle fermate con molte linee
- 🗺️ **Mappa interattiva** (MapLibre GL JS + OpenFreeMap): cerca le fermate vicino a te sulla mappa, oppure segui in tempo reale posizione e stato (in movimento / fermo con orario di ripartenza / già passato) dei bus della fermata monitorata, con pulsante per fermare il monitoraggio senza chiudere la mappa
- 🗂️ **System tray**: l'app resta in esecuzione in background con icona nella tray
- ⚙️ Avvio automatico con Windows, aggiornamento dati linee/fermate su richiesta, sessione salvata tra un riavvio e l'altro

### Installazione

Scarica `MonitorFermataAtacRoma-Setup-1.2.0.exe` qui sotto ed eseguilo.
L'installer:
- non richiede prerequisiti aggiuntivi (l'app è self-contained, include il runtime .NET)
- richiede **Microsoft Edge WebView2 Runtime** per la funzione mappa — quasi sempre già presente su Windows 10/11 aggiornati; se mancante, l'installer te lo segnala con il link per scaricarlo
- installa per l'utente corrente (in `%LocalAppData%`): **non serve essere amministratori**

### Fonte dati

[Open data GTFS/GTFS-RT di Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset) — nessuna API key richiesta. I dati di fermate e linee vengono aggiornati periodicamente in locale; gli orari di arrivo e le posizioni dei bus sono sempre in tempo reale.

La mappa usa le tile di [OpenFreeMap](https://openfreemap.org/), con dati cartografici © collaboratori di [OpenStreetMap](https://www.openstreetmap.org/copyright), distribuiti con licenza [ODbL](https://opendatacommons.org/licenses/odbl/).
