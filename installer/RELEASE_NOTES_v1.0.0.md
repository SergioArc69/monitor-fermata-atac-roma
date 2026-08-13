## Monitor Fermata ATAC Roma — v1.0.0

App desktop per Windows che mostra in tempo reale gli orari di arrivo degli autobus (ATAC / Roma TPL) a una fermata, usando i feed **GTFS** e **GTFS-RT** open data di Roma Mobilità.

### Funzionalità

- 🔍 **Ricerca fermate** per codice o per nome, con suggerimenti live e ultime fermate monitorate (MRU)
- 🕒 **Orari di arrivo in tempo reale**, raggruppati per linea, con stato del ritardo
- 🔔 **Notifiche desktop** configurabili (minuti di preavviso e fascia oraria) quando un bus sta per arrivare
- 🚏 **Filtro per linea**, utile nelle fermate con molte linee
- 🗺️ **Mappa interattiva**: cerca le fermate vicino a te sulla mappa, oppure segui in tempo reale la posizione (e lo stato fermo/in movimento) dei bus in arrivo alla fermata monitorata
- 🗂️ **System tray**: l'app resta in esecuzione in background con icona nella tray
- ⚙️ Avvio automatico con Windows, aggiornamento dati linee/fermate su richiesta, sessione salvata tra un riavvio e l'altro

### Installazione

Scarica `MonitorFermataAtacRoma-Setup-1.0.0.exe` qui sotto ed eseguilo. L'installer:
- non richiede prerequisiti aggiuntivi (l'app è self-contained, include il runtime .NET)
- richiede **Microsoft Edge WebView2 Runtime** per la funzione mappa — quasi sempre già presente su Windows 10/11 aggiornati; se mancante, l'installer te lo segnala con il link per scaricarlo

### Fonte dati

[Open data GTFS/GTFS-RT di Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset) — nessuna API key richiesta.
