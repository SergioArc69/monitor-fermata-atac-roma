## Monitor Fermata ATAC Roma — v1.1.0

App desktop per Windows che mostra in tempo reale gli orari di arrivo dei mezzi ATAC / Roma TPL a una fermata, usando i feed **GTFS** e **GTFS-RT** open data di Roma Mobilità.

### Novità in questa versione

- 🚌 **Bus già passati visibili sulla mappa**: i mezzi che hanno appena superato la fermata monitorata restano visibili per 10 minuti, evidenziati in blu con l'etichetta "già passato"/"passato X min fa", con la posizione aggiornata in tempo reale (non restano fermi nel punto di passaggio)
- 🏷️ **Identificativo linea sui bus in mappa**: l'etichetta di ogni bus mostra ora anche la linea, ad esempio `[443] 5045: fermo — tra 11 min (16:02:22)` — utile nelle fermate servite da più linee
- ⏹️ **Pulsante "Ferma monitoraggio" sulla mappa**: interrompe il monitoraggio e torna alla ricerca delle fermate vicine senza dover chiudere e riaprire la mappa
- 🌙 **Orario del giorno successivo come fallback**: attivando il monitoraggio di una fermata dopo l'ultima corsa della giornata, la lista non resta più vuota — vengono mostrate le prime corse schedulate del giorno dopo
- 📍 **Posizione utente lontana da Roma**: se rilevata a più di 50 km dal centro di Roma, la mappa mostra comunque le fermate del centro città (non ci sono fermate visualizzabili così lontano dall'area servita)
- 🎨 **Nuova icona**: sostituita con un design ispirato alla "palina" di fermata ATAC (giallo/bordeaux), al posto del precedente vagone arancione

### Correzioni

- Risolta un'eccezione (null reference) che poteva verificarsi chiudendo la finestra della mappa mentre un aggiornamento era in corso
- I bus già passati non sparivano più dopo 10 minuti come previsto, ma dopo circa 1 minuto: il feed GTFS-RT non mantiene l'informazione di una fermata già transitata per più di un minuto, quindi ora l'app la ricorda autonomamente per l'intera finestra di 10 minuti

### Funzionalità

- 🔍 **Ricerca fermate** per codice, nome o numero di linea, con suggerimenti live e ultime fermate monitorate (MRU)
- 🕒 **Orari di arrivo in tempo reale e schedulati**, raggruppati per linea, con stato del ritardo, e fallback automatico sull'orario del giorno successivo quando il servizio odierno è terminato
- 🔔 **Notifiche desktop** configurabili (minuti di preavviso e fascia oraria) quando un bus sta per arrivare
- 🚏 **Filtro per linea**, utile nelle fermate con molte linee
- 🗺️ **Mappa interattiva**: cerca le fermate vicino a te sulla mappa, oppure segui in tempo reale la posizione (e lo stato fermo/in movimento/già passato) dei bus della fermata monitorata, con pulsante per fermare il monitoraggio senza chiudere la mappa
- 🗂️ **System tray**: l'app resta in esecuzione in background con icona nella tray
- ⚙️ Avvio automatico con Windows, aggiornamento dati linee/fermate su richiesta, sessione salvata tra un riavvio e l'altro

### Installazione

Scarica `MonitorFermataAtacRoma-Setup-1.1.0.exe` qui sotto ed eseguilo.
L'installer:
- non richiede prerequisiti aggiuntivi (l'app è self-contained, include il runtime .NET)
- richiede **Microsoft Edge WebView2 Runtime** per la funzione mappa — quasi sempre già presente su Windows 10/11 aggiornati; se mancante, l'installer te lo segnala con il link per scaricarlo
- installa per l'utente corrente (in `%LocalAppData%`): **non serve essere amministratori**

### Fonte dati

[Open data GTFS/GTFS-RT di Roma Mobilità](https://romamobilita.it/it/tecnologie/open-data/dataset) — nessuna API key richiesta. I dati di fermate e linee vengono aggiornati periodicamente in locale; gli orari di arrivo e le posizioni dei bus sono sempre in tempo reale.

La mappa usa le tile di [OpenStreetMap](https://www.openstreetmap.org/copyright), © collaboratori di OpenStreetMap, distribuite con licenza [ODbL](https://opendatacommons.org/licenses/odbl/).
