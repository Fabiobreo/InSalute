# InSalute

Applicazione web (ASP.NET Core Blazor Server, .NET 10) per la segreteria del Poliambulatorio InSalute: l'operatore
inserisce i dati del paziente e allega i referti, il server li protegge con una password casuale (PDF cifrato AES-128
oppure ZIP AES-256), invia **prima** l'email con l'allegato e **solo se questa è partita** una seconda email con la
password, e registra ogni invio nello storico. Gli amministratori gestiscono utenti, impostazioni SMTP e modelli email.
I file caricati vengono cancellati dal server subito dopo l'invio: l'applicazione non è un archivio di referti.

Il piano di lavoro completo, con le decisioni già prese, è in [docs/piano-rifacimento.md](docs/piano-rifacimento.md).
Le istruzioni di installazione sul server sono in [docs/installazione.md](docs/installazione.md).

## Struttura

| Cartella | Contenuto |
|---|---|
| `src/InSalute.Core` | Entità, DbContext, servizi e regole di business (invio, protezione allegati, permessi) |
| `src/InSalute.Web` | Blazor Web App: pagine, layout, `Program.cs`, hosting |
| `tests/InSalute.Tests` | xUnit + bUnit |
| `tools/InSalute.LegacyImport` | Console per importare utenti e log dal vecchio database |
| `Client/`, `Server/` | Il vecchio sistema (WPF + Web API), da rimuovere dopo il passaggio (fase 10) |

## Avvio in sviluppo

Servono .NET 10 SDK e SQL Server LocalDB (o un'altra istanza: la connection string è in
`src/InSalute.Web/appsettings.Development.json`).

```bash
dotnet tool restore
dotnet ef database update --project src/InSalute.Core --startup-project src/InSalute.Web
dotnet run --project src/InSalute.Web --launch-profile https
```

Al primo avvio, se non esiste nessun utente, viene creato l'account `SuperAdmin` (nome utente `assistenza`, modificabile
con `Bootstrap:SuperAdminUserName`): la sua password temporanea è scritta **una sola volta** nel log di avvio
(console e `src/InSalute.Web/logs/`). Cercare «PRIMO AVVIO».

Per provare gli invii serve un server SMTP finto, ad esempio [smtp4dev](https://github.com/rnwood/smtp4dev). Mai dati o
email di pazienti reali.

L'applicazione invia **solo su connessione cifrata** (SSL/TLS o STARTTLS: non esiste l'opzione "nessuna") e controlla il
certificato del server. Il server finto deve quindi offrire STARTTLS con un certificato valido per `localhost`: il
certificato di sviluppo HTTPS di .NET va bene, perché `dotnet dev-certs https --trust` lo rende già attendibile.

Da una cartella fuori dal repository (il comando crea lì `smtp-dev.crt` e `smtp-dev.key`, da non condividere):

```bash
dotnet dev-certs https -ep smtp-dev.crt --format PEM --no-password
dotnet tool install -g Rnwood.Smtp4dev
smtp4dev --smtpport=2525 --tlsmode=StartTls --tlscertificate=smtp-dev.crt --tlscertificateprivatekey=smtp-dev.key
```

In *Impostazioni → SMTP*: server `localhost`, porta `2525`, sicurezza *STARTTLS*, nome utente vuoto, un mittente
qualsiasi. Le email arrivano nell'interfaccia di smtp4dev (http://localhost:5000).

## Test

```bash
dotnet test
```

I test dei servizi usano SQLite in-memory e un `FakeEmailSender`; quelli dei componenti usano bUnit.

## Pubblicazione

```bash
dotnet publish src/InSalute.Web -c Release -r win-x64 --self-contained -o publish
```

## Regole da non dimenticare

- La password del documento non viene mai salvata (database, file, log, `localStorage`).
- La seconda email (password) parte solo se la prima risulta inviata per lo stesso invio.
- Nei log nessun dato dei pazienti: solo il `DispatchId`.
- Segreti e connection string reali non vanno nel repository (`appsettings.Production.json` è in `.gitignore`).

## Proposte (non implementate)

Il piano vieta di aggiungere funzioni non richieste. Idee emerse durante lo sviluppo, da valutare col proprietario:

- Reinvio di un invio dallo storico (oggi dal dettaglio non c'è nessuna azione).
- Icone dell'app ad alta risoluzione: il logo originale è 94×85 px, quindi le icone 192 e 512 sono ingrandimenti.
