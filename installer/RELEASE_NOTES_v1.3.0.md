## Monitor Fermata ATAC Roma — v1.3.0

App desktop per Windows che mostra in tempo reale gli orari di arrivo dei mezzi ATAC / Roma TPL a una fermata, usando i feed **GTFS** e **GTFS-RT** open data di Roma Mobilità.

### Novità in questa versione

- 🚌 **Filtro per linea anche sulla mappa**: quando il monitoraggio è attivo, l'header della mappa mostra ora gli stessi checkbox per linea del filtro della finestra principale — modificabili direttamente da lì. Prima, il tracciamento dei bus sulla mappa ignorava il filtro e mostrava sempre tutte le linee.
- 🗺️ **Apertura mappa centrata sulla fermata cercata**: cliccando su "Mappa" con un codice fermata valido già scritto nella casella di ricerca (e nessun monitoraggio attivo), la mappa si apre centrata su quella fermata invece che sulla posizione utente o sul centro di Roma.

### Correzioni

- La lista degli arrivi e il titolo della finestra non venivano svuotati/ripristinati fermando il monitoraggio.
- Fermando il monitoraggio dal pulsante sulla mappa, la finestra principale non si aggiornava finché non si chiudeva la mappa.
- Quando GTFS-RT non aveva dati in tempo reale per la fermata (es. linee ad altissima frequenza come la metro), il fallback sull'orario statico poteva mostrare decine di corse per la stessa linea: ora ogni linea è limitata alle prossime corse.
- I tooltip delle fermate sulla mappa potevano restare bloccati a video, senza modo di chiuderli, se il marker veniva rimosso (es. da un aggiornamento della vista) mentre il mouse ci era ancora sopra.
- Il checkbox "Filtra per linea" (finestra principale e mappa) resta ora disattivato e deselezionato di default finché non si seleziona almeno una linea.
- Aggiornato lo stile della mappa da "bright" a "liberty", ora mantenuto come predefinito da OpenFreeMap.

### Funzionalità

- 🔍 **Ricerca fermate** per codice, nome o numero di linea, con suggerimenti live e ultime fermate monitorate (MRU)
- 🕒 **Orari di arrivo in tempo reale e schedulati**, raggruppati per linea, con stato del ritardo, e fallback automatico sull'orario del giorno successivo quando il servizio odierno è terminato
- 🔔 **Notifiche desktop** configurabili (minuti di preavviso e fascia oraria) quando un bus sta per arrivare
- 🚏 **Filtro per linea**, utile nelle fermate con molte linee — applicato sia agli arrivi/notifiche che al tracciamento sulla mappa
- 🗺️ **Mappa interattiva** (MapLibre GL JS + OpenFreeMap): cerca le fermate vicino a te sulla mappa, oppure segui in tempo reale posizione e stato (in movimento / fermo con orario di ripartenza / già passato) dei bus della fermata monitorata, con pulsante per fermare il monitoraggio senza chiudere la mappa
- 🗂️ **System tray**: l'app resta in esecuzione in background con icona nella tray
- ⚙️ Avvio automatico con Windows, aggiornamento dati linee/fermate su richiesta, sessione salvata tra un riavvio e l'altro

### Installazione

Scarica `MonitorFermataAtacRoma-Setup-1.3.0.exe` qui sotto ed eseguilo.
L'installer:
- non richiede prerequisiti aggiuntivi (l'app è self-contained, include il runtime .NET)
- richiede **Microsoft Edge WebView2 Runtime** per la funzione mappa — quasi sempre già presente su Windows 10/11 aggiornati; se mancante, l'installer te lo segnala con il link per scaricarlo
- installa per l'utente corrente (in `%LocalAppData%`): **non serve essere amministratori**

### Fonte dati

[Open data GTFS/GTFS-RT di Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset) — nessuna API key richiesta. I dati di fermate e linee vengono aggiornati periodicamente in locale; gli orari di arrivo e le posizioni dei bus sono sempre in tempo reale.

La mappa usa le tile di [OpenFreeMap](https://openfreemap.org/), con dati cartografici © collaboratori di [OpenStreetMap](https://www.openstreetmap.org/copyright), distribuiti con licenza [ODbL](https://opendatacommons.org/licenses/odbl/).
