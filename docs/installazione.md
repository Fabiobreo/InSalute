# Installazione di InSalute sul server

> **Stato del documento:** bozza preparata durante lo sviluppo, **prima** dell'installazione.
> I comandi sono scritti secondo il piano (sezione 12) ma **non sono stati provati sul server reale**.
> I campi `DA COMPILARE` vanno riempiti durante l'installazione (fase 8): alla fine questo file deve descrivere
> ciò che è stato davvero fatto, non ciò che era previsto.

InSalute gira come **servizio Windows con Kestrel** (non in IIS) sul PC server del centro, ed è usata via browser
dalle 3 postazioni della segreteria. Non si tocca nulla di ciò che c'è già su IIS.

## 0. Cose da sapere prima di iniziare

- **Windows 10 non è più supportato** (fine supporto il 14 ottobre 2025). Un server con dati sanitari senza aggiornamenti
  di sicurezza è un rischio: segnalare al titolare l'aggiornamento a Windows 11 Pro / Windows Server oppure gli
  aggiornamenti estesi a pagamento (ESU). Verificare anche che .NET 10 sia supportato sulla build di Windows 10
  installata; **se non lo è, fermarsi e chiedere**.
- **Account di assistenza tecnica.** Al primo avvio viene creato un account `SuperAdmin` (nome utente predefinito
  `assistenza`) riservato allo sviluppatore del software. È nascosto nell'interfaccia (elenco utenti, filtri, storico:
  gli invii fatti da lui appaiono come «Assistenza tecnica»), ma **non è nascosto al cliente**: il titolare del centro
  deve essere informato della sua esistenza. Il centro deve avere un proprio utente `Admin` indipendente.
- **Non modificare** siti, app pool o impostazioni IIS esistenti. Prima di iniziare annotare cosa c'è già.

### Rilevazioni iniziali (DA COMPILARE)

| Voce | Valore |
|---|---|
| Build di Windows 10 | DA COMPILARE |
| Siti IIS, binding e porte già usate (`netstat -ano`) | DA COMPILARE |
| Istanza SQL Server usata dal vecchio sistema (dal `Web.config` della vecchia Web API) | DA COMPILARE |
| `SELECT @@VERSION` (edizione e versione) | DA COMPILARE |
| Nome del PC | DA COMPILARE |
| Porta scelta per InSalute (es. 8443, oppure 443 se libera) | DA COMPILARE |
| Nome usato dalle postazioni (es. `insalute.studio.lan`) | DA COMPILARE |

Se la versione di SQL Server è **precedente a SQL Server 2016**: fermarsi e chiedere (serve impostare il livello di
compatibilità in EF Core oppure aggiornare SQL Server).

## 1. Pubblicazione (sul PC di sviluppo)

```powershell
dotnet publish src/InSalute.Web -c Release -r win-x64 --self-contained -o publish
```

L'app è *self-contained*: sul server **non** serve installare il runtime .NET. La cartella `publish` (circa 150 MB)
va copiata sul server. Anche la console di import dal vecchio sistema (punto 9) si pubblica a parte, nello stesso modo:

```powershell
dotnet publish tools/InSalute.LegacyImport -c Release -r win-x64 --self-contained -o publish-import
```

La cartella `publish-import` serve solo per l'import: copiarla sul server in una cartella di lavoro (es.
`C:\InSaluteData\import`), **non** in `C:\Program Files\InSalute`, e cancellarla a import concluso.

Lo script del database si genera sul PC di sviluppo:

```powershell
dotnet ef migrations script --idempotent --project src/InSalute.Core --startup-project src/InSalute.Web -o insalute.sql
```

## 2. Cartelle e utente di servizio (sul server, come amministratore)

```powershell
New-Item -ItemType Directory -Force "C:\Program Files\InSalute"
foreach ($d in "tmp","keys","logs","backup") { New-Item -ItemType Directory -Force "C:\InSaluteData\$d" }
```

Creare l'utente locale di servizio `svc-insalute` (password lunga e casuale, «la password non scade», nessun accesso
interattivo) da *Gestione computer → Utenti e gruppi locali*, oppure:

```powershell
$pwd = Read-Host -AsSecureString "Password per svc-insalute"
New-LocalUser -Name "svc-insalute" -Password $pwd -PasswordNeverExpires -UserMayNotChangePassword
```

Permessi: `svc-insalute` con *Modifica* su `C:\InSaluteData` e *Lettura* su `C:\Program Files\InSalute`;
ereditarietà rimossa da `C:\InSaluteData`, restano solo SYSTEM, Administrators e `svc-insalute`:

```powershell
icacls "C:\InSaluteData" /inheritance:r
icacls "C:\InSaluteData" /grant "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" "svc-insalute:(OI)(CI)M"
icacls "C:\Program Files\InSalute" /grant "svc-insalute:(OI)(CI)RX"
```

Copiare il contenuto di `publish` in `C:\Program Files\InSalute`.

> Le chiavi di Data Protection (`C:\InSaluteData\keys`) sono protette con DPAPI: sono leggibili solo dall'account
> `svc-insalute` su **questo** PC. Se si perdono, la password SMTP salvata va reinserita e tutti devono rifare il login.

## 3. Database

1. Creare il database `InSaluteV2` sull'istanza individuata.
2. Creare il login Windows `<NOMEPC>\svc-insalute` e dargli sul database **solo** `db_datareader` e `db_datawriter`.
3. Con un account amministratore SQL eseguire `insalute.sql` (le migrazioni **non** si applicano all'avvio dell'app;
   lo script va rieseguito a ogni rilascio, è idempotente):

```powershell
sqlcmd -S "<ISTANZA>" -d InSaluteV2 -E -i insalute.sql
```

## 4. Configurazione di produzione

Creare `C:\Program Files\InSalute\appsettings.Production.json` (**non è nel repository**: contiene la connection string):

```json
{
  "ConnectionStrings": {
    "Default": "Server=<ISTANZA>;Database=InSaluteV2;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:8443",
        "Certificate": { "Subject": "insalute.studio.lan", "Store": "My", "Location": "LocalMachine" }
      }
    }
  },
  "Bootstrap": { "SuperAdminUserName": "assistenza" }
}
```

- Un solo endpoint HTTPS, su una porta **non usata da IIS**; nessun endpoint HTTP.
- Le cartelle `C:\InSaluteData\tmp`, `keys` e `logs` sono i valori predefiniti (`Storage:TempPath`, `Storage:KeysPath`,
  log Serilog): non serve scriverli se si usano questi percorsi.
- Impostare `ASPNETCORE_ENVIRONMENT=Production` per il servizio (vedi punto 6) oppure tenere il file sopra e basta:
  senza la variabile l'app parte comunque come *Production*.

## 5. Certificato HTTPS in rete locale

Scegliere **una** strada e annotarla qui sotto.

**A. Autofirmato (più semplice).** Sul server, con i nomi che useranno le postazioni (nome DNS **e** nome macchina),
validità 5 anni:

```powershell
$cert = New-SelfSignedCertificate -DnsName "insalute.studio.lan","<NOMEPC>" `
    -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(5) -FriendlyName "InSalute"
Export-Certificate -Cert $cert -FilePath "$env:USERPROFILE\Desktop\insalute.cer"   # senza chiave privata
```

Dare a `svc-insalute` il permesso di lettura sulla chiave privata (*certlm.msc → Tutte le attività → Gestisci chiavi
private*). Installare `insalute.cer` nelle «Autorità di certificazione radice attendibili» **delle 3 postazioni**.

**B. Dominio vero + Let's Encrypt** con challenge DNS (es. win-acme): nessuna installazione sulle postazioni, ma serve
un dominio di proprietà e il rinnovo automatico.

Il nome scelto deve risolversi all'IP del server (DNS del router o riga nel file `hosts` di ogni postazione).
**Non** usare nomi che finiscono in `.local`.

| Voce | Valore |
|---|---|
| Strada scelta (A/B) | DA COMPILARE |
| Soggetto / nomi nel certificato | DA COMPILARE |
| **Scadenza del certificato** | DA COMPILARE (mettere un promemoria 1 mese prima) |
| Dove si rinnova / come | DA COMPILARE |

## 6. Servizio Windows

```powershell
sc.exe create InSalute binPath= "C:\Program Files\InSalute\InSalute.Web.exe" start= delayed-auto obj= ".\svc-insalute" password= "<PASSWORD>"
sc.exe failure InSalute reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start InSalute
```

(La password compare nella riga di comando: eseguirla da una console da cui poi si cancella la cronologia.)

**Primo avvio.** Se il database non ha utenti, l'app crea l'account `SuperAdmin` e scrive **una sola volta** la sua
password temporanea nel log di avvio (`C:\InSaluteData\logs\insalute-AAAAMMGG.log`, cercare «PRIMO AVVIO»). Il
SuperAdmin deve cambiarla al primo accesso, poi crea l'`Admin` del centro. Non lasciare la password in altri posti.

## 7. Firewall

Aprire la porta del servizio **solo verso la rete locale** (profilo Privato/Dominio). **Nessuna** esposizione su
Internet e nessun port forwarding sul router.

```powershell
New-NetFirewallRule -DisplayName "InSalute HTTPS" -Direction Inbound -Protocol TCP -LocalPort 8443 `
    -Action Allow -Profile Private,Domain -RemoteAddress LocalSubnet
```

## 8. Configurazione dell'applicazione

1. Dalle postazioni aprire `https://insalute.studio.lan:8443` (aggiungere la porta se non è 443), verificare il lucchetto.
2. Accesso come SuperAdmin → creare l'Admin del centro → l'Admin crea Manager e Operatori.
3. *Impostazioni → SMTP*: inserire i dati della casella reale e premere **Invia email di prova** verso un indirizzo interno.
4. *Impostazioni → Modelli email*: controllare i due testi (sono già impostati con quelli del vecchio sistema).
5. Nelle postazioni, in Edge: *App → Installa questo sito come app* (finestra propria e icona nella barra).

## 9. Import dal vecchio sistema (fase 7)

> **Ordine obbligatorio: prima il primo avvio dell'app (punto 6), poi l'import.** L'account `SuperAdmin` viene creato
> solo se nel database non c'è **nessun** utente. Gli utenti importati sono tutti disattivati: se l'import viene fatto
> prima del primo avvio, il SuperAdmin non viene creato e nessuno può accedere. Se succede: svuotare `InSaluteV2`
> (ricrearlo ed eseguire di nuovo `insalute.sql`), avviare il servizio e solo dopo rifare l'import.

L'import si esegue da una console amministratore sul server, con un account che può leggere il vecchio database e
scrivere su `InSaluteV2` (connection string con `Trusted_Connection=True`). Stampa solo conteggi, mai dati dei pazienti.

### 9.1 Simulazione su una copia

Fare una copia del vecchio database (`COPY_ONLY` non interferisce con eventuali backup già pianificati) e ripristinarla
con un altro nome, es. `VecchioDbCopia`:

```sql
BACKUP DATABASE [<VECCHIO DB>] TO DISK='C:\InSaluteData\backup\vecchio-copia.bak' WITH COPY_ONLY, INIT
RESTORE DATABASE VecchioDbCopia FROM DISK='C:\InSaluteData\backup\vecchio-copia.bak' WITH MOVE ... TO ...
```

```powershell
C:\InSaluteData\import\InSalute.LegacyImport.exe --source "<connection string di VecchioDbCopia>" --target "<connection string di InSaluteV2>" --dry-run
```

Controllare i conteggi. Se «non importabili» è diverso da zero, controllarne il motivo: di solito email o nomi utente
duplicati, anche rispetto agli utenti già creati in InSalute (per esempio l'Admin del centro creato al punto 8 con lo
stesso nome utente o la stessa email di un vecchio utente).

### 9.2 Passaggio definitivo (cutover)

Fuori dall'orario della segreteria:

1. Avvisare la segreteria e **chiudere il vecchio client WPF** su tutte le postazioni: da qui in poi nessun invio dal
   vecchio sistema.
2. Fare un backup di `InSaluteV2` (come al punto 10) prima di scrivere.
3. Lanciare l'import **senza** `--dry-run` sul vecchio database (o su una copia fatta in quel momento, come al 9.1):

   ```powershell
   C:\InSaluteData\import\InSalute.LegacyImport.exe --source "<connection string del vecchio DB>" --target "<connection string di InSaluteV2>"
   ```

   Rilanciarlo è sicuro: utenti e invii già importati non vengono aggiunti di nuovo. Se dopo la simulazione il vecchio
   sistema è stato ancora usato, questa esecuzione aggiunge solo le righe nuove.
4. Aprire *Storico* in InSalute e controllare che gli invii importati ci siano (data dell'ultimo invio del vecchio sistema).
5. Gli utenti importati sono **disattivati** e senza password: l'Admin riattiva quelli ancora in servizio e assegna a
   ciascuno una password temporanea dalla pagina *Utenti* (verrà chiesto di cambiarla al primo accesso).
6. Lasciare il vecchio sistema **spento ma intatto** (Web API su IIS e database) finché il nuovo non è stato usato
   qualche giorno senza problemi; la rimozione è la fase 10. Il vecchio database resta la copia di riferimento.
7. Cancellare `C:\InSaluteData\import` e la copia `VecchioDbCopia` (e il relativo `.bak`).

| Voce | Valore |
|---|---|
| Data e ora del cutover | DA COMPILARE |
| Conteggi della simulazione (utenti / invii / non importabili) | DA COMPILARE |
| Conteggi dell'import definitivo | DA COMPILARE |

## 10. Backup

SQL Server Express non ha l'Agent: usare un'operazione pianificata di Windows ogni notte. Esempio di script
`C:\InSaluteData\backup-insalute.ps1`:

```powershell
$stamp = Get-Date -Format "yyyyMMdd"
sqlcmd -S "<ISTANZA>" -E -Q "BACKUP DATABASE InSaluteV2 TO DISK='C:\InSaluteData\backup\InSaluteV2_$stamp.bak' WITH INIT"
Get-ChildItem "C:\InSaluteData\backup" -Filter *.bak | Where-Object LastWriteTime -lt (Get-Date).AddDays(-30) | Remove-Item
robocopy "C:\InSaluteData\backup" "<DESTINAZIONE ESTERNA>\backup" /E
robocopy "C:\InSaluteData\keys"   "<DESTINAZIONE ESTERNA>\keys"   /E
```

```powershell
schtasks /Create /TN "Backup InSalute" /SC DAILY /ST 02:00 /RU SYSTEM /TR "powershell -ExecutionPolicy Bypass -File C:\InSaluteData\backup-insalute.ps1"
```

Conservare 30 giorni. Il backup va copiato **fuori dal PC** (NAS, disco esterno o altro PC) e deve includere la cartella `keys`.

### Ripristino (da provare prima della produzione)

1. Fermare il servizio: `sc.exe stop InSalute`.
2. Ripristinare su un database **di test** per verificare il backup:
   `RESTORE DATABASE InSaluteV2Test FROM DISK='...\InSaluteV2_AAAAMMGG.bak' WITH MOVE ... TO ...`
3. In caso di ripristino vero, ripristinare anche `C:\InSaluteData\keys`, poi `sc.exe start InSalute`.
4. Controllare l'accesso e lo storico.

| Voce | Valore |
|---|---|
| Dove finisce la copia esterna | DA COMPILARE |
| Data e esito della prova di ripristino | DA COMPILARE |

## 11. Aggiornamenti

Fuori dall'orario della segreteria:

1. `sc.exe stop InSalute`
2. Copiare la versione attuale di `C:\Program Files\InSalute` in `C:\InSaluteData\release-precedente` (per tornare indietro).
3. Copiare i nuovi file in `C:\Program Files\InSalute`. **Non** sovrascrivere `appsettings.Production.json`.
4. Eseguire il nuovo `insalute.sql` con un account amministratore SQL.
5. `sc.exe start InSalute` e controllare il log.

Per tornare indietro: fermare il servizio, rimettere `release-precedente` (e, se la migrazione era distruttiva,
ripristinare il database dal backup).

## 12. Cosa è stato fatto davvero (DA COMPILARE)

Data dell'installazione, chi l'ha eseguita, scostamenti dal piano, problemi incontrati, versione installata.
