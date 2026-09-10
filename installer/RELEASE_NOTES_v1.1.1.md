## Monitor Fermata ATAC Roma — v1.1.1

App desktop per Windows che mostra in tempo reale gli orari di arrivo dei mezzi ATAC / Roma TPL a una fermata, usando i feed **GTFS** e **GTFS-RT** open data di Roma Mobilità.

### Novità in questa versione

- 🕘 **Orario di ripartenza per i mezzi fermi**: quando un bus è fermo a una fermata e il feed fornisce la previsione di ripartenza, la chip sotto la mappa la mostra accanto allo stato, ad esempio `[443] 5026: fermo (09:20:00 |→) — tra 13 min (→| 09:33:00)`. Il marcatore `→|` sull'orario di arrivo compare solo quando è presente anche la partenza `|→`, così i due orari restano distinguibili
- ⏱️ **Aggiornamento posizioni bus ogni 20 secondi** (prima 15), per un tracciamento più regolare

### Correzioni

- **Marker duplicati sulla mappa**: in caso di aggiornamenti lenti, il ciclo successivo poteva partire prima che il precedente avesse finito, lasciando sulla mappa il marker "vecchio" oltre a quello nuovo. Ora un nuovo aggiornamento viene saltato se il precedente è ancora in corso, e i marker vengono sostituiti in blocco solo a fine ciclo
- **Mezzi segnalati "già passato" per errore**: un bus in ritardo, ancora in avvicinamento, poteva essere marcato come già transitato solo perché l'orario previsto era passato. Ora lo stato "già passato" richiede sia che il mezzo sia sparito dal feed della fermata (i dati GTFS-RT rimuovono la fermata appena il veicolo la supera) sia che l'ultima previsione sia oltre 90 secondi nel passato

### Funzionalità

- 🔍 **Ricerca fermate** per codice, nome o numero di linea, con suggerimenti live e ultime fermate monitorate (MRU)
- 🕒 **Orari di arrivo in tempo reale e schedulati**, raggruppati per linea, con stato del ritardo, e fallback automatico sull'orario del giorno successivo quando il servizio odierno è terminato
- 🔔 **Notifiche desktop** configurabili (minuti di preavviso e fascia oraria) quando un bus sta per arrivare
- 🚏 **Filtro per linea**, utile nelle fermate con molte linee
- 🗺️ **Mappa interattiva**: cerca le fermate vicino a te sulla mappa, oppure segui in tempo reale posizione e stato (in movimento / fermo con orario di ripartenza / già passato) dei bus della fermata monitorata, con pulsante per fermare il monitoraggio senza chiudere la mappa
- 🗂️ **System tray**: l'app resta in esecuzione in background con icona nella tray
- ⚙️ Avvio automatico con Windows, aggiornamento dati linee/fermate su richiesta, sessione salvata tra un riavvio e l'altro

### Installazione

Scarica `MonitorFermataAtacRoma-Setup-1.1.1.exe` qui sotto ed eseguilo.
L'installer:
- non richiede prerequisiti aggiuntivi (l'app è self-contained, include il runtime .NET)
- richiede **Microsoft Edge WebView2 Runtime** per la funzione mappa — quasi sempre già presente su Windows 10/11 aggiornati; se mancante, l'installer te lo segnala con il link per scaricarlo
- installa per l'utente corrente (in `%LocalAppData%`): **non serve essere amministratori**

### Fonte dati

[Open data GTFS/GTFS-RT di Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset) — nessuna API key richiesta. I dati di fermate e linee vengono aggiornati periodicamente in locale; gli orari di arrivo e le posizioni dei bus sono sempre in tempo reale.

La mappa usa le tile di [OpenStreetMap](https://www.openstreetmap.org/copyright), © collaboratori di OpenStreetMap, distribuite con licenza [ODbL](https://opendatacommons.org/licenses/odbl/).
