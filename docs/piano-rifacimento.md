# Piano di rifacimento InSalute

> **Destinatario di questo documento:** uno sviluppatore o un modello AI che deve realizzare il nuovo sistema.
> Le decisioni qui sotto sono **già prese**: non vanno ridiscusse né "migliorate" durante l'implementazione.
> Se qualcosa non è chiaro o sembra sbagliato, **fermati e chiedi** invece di inventare una soluzione.
> Le risposte del proprietario alle domande iniziali sono nella sezione 13: sono definitive.

---

## 1. Obiettivo

Sostituire l'attuale sistema InSalute (client WPF in `Client/` + Web API in `Server/`) con **un'unica applicazione web** ASP.NET Core Blazor Server, installata sul PC server del centro medico (come servizio Windows) e usata via browser dalle **3 postazioni** della segreteria.

Cosa fa l'applicazione (e nient'altro):

1. Un operatore inserisce i dati del paziente (nome, cognome, email) e allega uno o più file (referti, fattura facoltativa).
2. Il server protegge i file con una password casuale (PDF cifrato oppure ZIP cifrato AES-256).
3. Il server invia **prima** l'email con l'allegato protetto e, **solo se questa è andata a buon fine**, una **seconda** email con la password.
4. Il server registra ogni invio in uno storico (audit) con l'esito reale.
5. Gli amministratori gestiscono utenti, impostazioni SMTP e modelli delle email.

L'applicazione **non** è un archivio di referti: i file caricati vengono cancellati dal server subito dopo l'invio. L'archivio ufficiale resta il gestionale dei medici.

### 1.1 Perché si rifà (riassunto per chi non ha letto l'analisi)

- Le password degli utenti sono cifrate in modo reversibile con chiave scritta nel codice (`Server/REST_API/Models/Security.cs`) e vengono restituite dall'API.
- Le credenziali SMTP stanno sulle postazioni, in un JSON solo codificato Base64.
- Il log degli invii lo scrive il client ed è registrato anche se l'invio fallisce.
- Bug grave: se fallisce la prima email, al "riprova" parte solo l'email con la password (`Client/InSaluteClient/ViewModel/CoreViewModel.cs:453-472`).
- Stack fuori supporto (.NET Core 3.1, ASP.NET Web API 5, EF6 EDMX), nessun test, UI valutata 3/10 per usabilità.

---

## 2. Decisioni tecniche (vincolanti)

| Area | Scelta | Note |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Pubblicazione self-contained (vedi 12): sul server non serve installare il runtime. |
| Tipo di app | **Blazor Web App**, render mode **Interactive Server**, interattività **globale** | Niente WebAssembly, niente API REST separata. |
| UI | **MudBlazor** (ultima versione stabile compatibile con .NET 10) | Unica libreria di componenti. Niente Bootstrap nelle pagine dell'app (vedi 2.1). |
| Autenticazione | **ASP.NET Core Identity** con cookie | Account locali. Nessuna registrazione pubblica. |
| Database | **SQL Server** (istanza già presente sul server), database **nuovo** `InSaluteV2` | Non riusare le tabelle del vecchio database. |
| ORM | **EF Core** (SQL Server provider), Code-First con migrazioni | Niente EDMX, niente Database-First. |
| Email | **MailKit** | Non usare `System.Net.Mail.SmtpClient`. |
| PDF | **PDFsharp ≥ 6.1** (pacchetto `PDFsharp`) | Per unire e cifrare PDF con AES-128 (vedi 7.3). |
| ZIP | **SharpZipLib** con `AESKeySize = 256` | Mai ZipCrypto (è il default se non imposti `AESKeySize`). |
| Segreti | **ASP.NET Core Data Protection**, chiavi su disco protette con DPAPI | Per la password SMTP salvata nel DB. |
| Log applicativo | **Serilog** su file giornaliero | Mai dati dei pazienti nei log (vedi 10). |
| Test | **xUnit** + **bUnit** (componenti) | Obbligatori per le parti elencate in 11. |
| Hosting | **Servizio Windows con Kestrel** (`UseWindowsService`) sul PC dedicato, **non** IIS | Il server è Windows 10: IIS sulle edizioni client limita le richieste contemporanee e il limite è condiviso con le altre app già presenti; le connessioni WebSocket di Blazor Server restano aperte e lo consumerebbero. Vedi 12. |

### 2.1 Pagine di account del template

Il template `dotnet new blazor --auth Individual` genera le pagine di login/gestione account in `Components/Account/` con Bootstrap e rendering statico. Regole:

- **Eliminare** le pagine di registrazione (`Register`, `RegisterConfirmation`, `ResendEmailConfirmation`, `ConfirmEmail*`), di login esterno (`ExternalLogin*`), di passkey/2FA se presenti, e i link a queste pagine.
- **Tenere**: `Login`, `Logout`, `Lockout`, `AccessDenied`, `Manage/ChangePassword`.
- Restilizzare la pagina di login a mano con CSS semplice coerente con la palette (vedi 8.1). È l'unica eccezione ammessa a "solo MudBlazor".

---

## 3. Struttura della soluzione

Il nuovo codice va nella **stessa repository**, in nuove cartelle. **Non modificare né spostare `Client/` e `Server/`** fino alla fase 10.

```
InSalute/
├─ Client/                      (vecchio, non toccare)
├─ Server/                      (vecchio, non toccare)
├─ docs/
│  └─ piano-rifacimento.md      (questo file)
├─ src/
│  ├─ InSalute.Web/             Blazor Web App: pagine, layout, Program.cs, hosting
│  └─ InSalute.Core/            Entità, DbContext, servizi, regole di business
├─ tests/
│  └─ InSalute.Tests/           xUnit + bUnit
├─ tools/
│  └─ InSalute.LegacyImport/    Console app per importare utenti e log dal vecchio DB
├─ InSalute.slnx (o .sln)
├─ Directory.Build.props        Nullable enable, TreatWarningsAsErrors, LangVersion latest
└─ .editorconfig
```

### 3.1 Contenuto di `InSalute.Core`

```
InSalute.Core/
├─ Data/
│  ├─ AppDbContext.cs                (eredita IdentityDbContext<ApplicationUser>)
│  ├─ Migrations/
│  └─ Configurations/                (IEntityTypeConfiguration<T> per ogni entità)
├─ Domain/
│  ├─ ApplicationUser.cs
│  ├─ Dispatch.cs                    (un invio)
│  ├─ DispatchStepStatus.cs          (enum)
│  ├─ ProtectionKind.cs              (enum)
│  ├─ EmailTemplate.cs
│  ├─ EmailTemplateKind.cs           (enum)
│  ├─ SmtpSettings.cs
│  └─ Roles.cs                       (costanti dei ruoli)
├─ Dispatching/
│  ├─ DispatchService.cs             (orchestrazione dell'invio, macchina a stati)
│  ├─ DispatchRequest.cs
│  ├─ PreparedDispatch.cs
│  ├─ AttachmentProtector.cs         (PDF/ZIP + password)
│  ├─ PasswordGenerator.cs
│  ├─ TemplateRenderer.cs            (sostituzione segnaposto)
│  ├─ RecipientChecks.cs             (refusi dominio, coerenza nome file, storico destinatario)
│  └─ TempFileStore.cs               (cartella temporanea + pulizia)
├─ Email/
│  ├─ IEmailSender.cs
│  └─ MailKitEmailSender.cs
├─ Users/
│  └─ UserAdminService.cs
├─ Settings/
│  ├─ SmtpSettingsService.cs
│  └─ EmailTemplateService.cs
├─ Security/
│  ├─ Policies.cs                    (nomi e definizione delle policy)
│  └─ ICurrentUser.cs                (utente corrente, implementato nel progetto Web)
└─ Texts/
   └─ Messaggi.cs                    (tutti i messaggi di errore/esito restituiti dai servizi, in italiano)
```

Regola: **tutta la logica sta in `InSalute.Core`**. I componenti Razor chiamano i servizi e mostrano risultati; non contengono logica di invio, cifratura, controllo permessi o accesso al DbContext.

### 3.2 Contenuto di `InSalute.Web`

```
InSalute.Web/
├─ Program.cs
├─ appsettings.json                  (nessun segreto, nessuna connection string reale)
├─ appsettings.Development.json      (connection string a LocalDB/SQL locale di sviluppo)
├─ Components/
│  ├─ App.razor
│  ├─ Routes.razor
│  ├─ Layout/
│  │  ├─ MainLayout.razor            (MudLayout: AppBar, Drawer, contenuto)
│  │  ├─ NavMenu.razor
│  │  └─ ReconnectModal.razor        (vedi 8.6)
│  ├─ Account/                       (pagine Identity tenute, vedi 2.1)
│  ├─ Pages/
│  │  ├─ InviaReferto/
│  │  │  ├─ InviaReferto.razor       (route "/")
│  │  │  ├─ StepPaziente.razor
│  │  │  ├─ StepDocumenti.razor
│  │  │  ├─ StepRiepilogo.razor
│  │  │  └─ StepEsito.razor
│  │  ├─ Storico.razor               ("/storico")
│  │  ├─ DettaglioInvio.razor        ("/storico/{id:guid}")
│  │  ├─ Utenti.razor                ("/utenti")
│  │  ├─ ImpostazioniSmtp.razor      ("/impostazioni/smtp")
│  │  ├─ ModelliEmail.razor          ("/impostazioni/modelli")
│  │  └─ Profilo.razor               ("/profilo")
│  └─ Shared/                        (componenti riusabili: StatoPasso, ConfermaDialog, ...)
├─ Services/
│  ├─ CurrentUser.cs                 (implementa ICurrentUser da AuthenticationStateProvider)
│  └─ TempCleanupHostedService.cs
└─ wwwroot/
   ├─ css/app.css
   ├─ img/logo.png                   (copiare da Client/InSaluteClient/Resources/Images/Logo.png)
   ├─ manifest.webmanifest           (vedi 8.6)
   └─ icons/                         (192x192, 512x512 generate dal logo)
```

### 3.3 Convenzioni

- Identificatori nel codice **in inglese** (classi, metodi, proprietà, tabelle). Testi visibili all'utente **in italiano**.
- I nomi delle pagine/route sono in italiano (sono parte della UI).
- Tutti i metodi di I/O sono `async` e accettano un `CancellationToken`. Vietati `.Result`, `.Wait()`, `async void` (eccetto gestori di eventi Blazor che lo richiedono).
- `Nullable` abilitato, warning trattati come errori.
- Date salvate in **UTC** (`DateTimeOffset` o `DateTime` con `Kind=Utc`), mostrate in ora italiana (`Europe/Rome`), formato `dd/MM/yyyy HH:mm`. Cultura dell'app: `it-IT`.
- Nessun `MessageBox`/`alert` JavaScript: esiti con `ISnackbar` di MudBlazor o con messaggi in pagina, conferme con `MudDialog`.

---

## 4. Modello dati

Tutte le tabelle nel database `InSaluteV2`. Chiavi primarie generate dal database (`IDENTITY`) o `Guid` generati dal codice. **Mai** calcolare gli ID "cercando il primo libero" come fa il vecchio codice.

### 4.1 `ApplicationUser` (estende `IdentityUser`)

| Campo | Tipo | Note |
|---|---|---|
| (campi Identity) | | `UserName`, `Email`, `PasswordHash`, `LockoutEnd`, ... |
| `DisplayName` | `string` (max 100) | Nome mostrato in UI, es. "Maria Rossi". |
| `IsActive` | `bool` | Gli utenti non si cancellano: si disattivano. Un utente non attivo non può accedere. |
| `MustChangePassword` | `bool` | Se `true`, dopo il login l'utente viene portato al cambio password e non può fare altro. |
| `CreatedAt` | `DateTime` UTC | |
| `LegacyId` | `long?` | ID nel vecchio DB, valorizzato solo dall'import. Indice univoco filtrato (non null). |

### 4.2 Ruoli (`Domain/Roles.cs`)

Quattro ruoli, nomi esatti: `SuperAdmin`, `Admin`, `Manager`, `Operatore`. Il vecchio ruolo `user` diventa `Operatore`. Ogni utente ha **esattamente un** ruolo.

`SuperAdmin` è l'account di assistenza tecnica dello sviluppatore (proprietario del software):

- Ha tutti i permessi dell'Admin.
- **Non compare** nell'elenco utenti, nei menu a tendina degli operatori, nei filtri, né nei conteggi, per nessuno tranne gli altri SuperAdmin.
- Nessun altro ruolo può vederlo, modificarlo, disattivarlo o reimpostarne la password. Solo un SuperAdmin può creare o gestire un SuperAdmin.
- Le sue azioni restano tracciate come quelle di chiunque altro. Nello storico gli invii fatti da un SuperAdmin mostrano come operatore **"Assistenza tecnica"** invece del nome.
- Non conta per la regola "almeno un Admin attivo" (5.1): il centro deve avere un proprio Admin indipendente dal SuperAdmin.
- Il fatto che esista un account di assistenza va scritto in `docs/installazione.md` e comunicato al titolare del centro: nascosto nell'interfaccia, non nascosto al cliente.

### 4.3 `Dispatch` (un invio)

| Campo | Tipo | Note |
|---|---|---|
| `Id` | `Guid` | |
| `CreatedAt` | `DateTime` UTC | |
| `CreatedByUserId` | `string` FK → `ApplicationUser` | `OnDelete: Restrict`. |
| `PatientFirstName` | `string` (max 100) | |
| `PatientLastName` | `string` (max 100) | |
| `RecipientEmail` | `string` (max 254) | Salvata in minuscolo, senza spazi. Indice non univoco. |
| `AttachmentsJson` | `string` | JSON: lista di `{ fileName, sizeBytes, sha256 }` dei file **originali** caricati. |
| `InvoiceFileName` | `string?` | |
| `ProtectionKind` | enum `ProtectionKind` | `PdfAes`, `ZipAes`, `Unknown` (solo import). |
| `ReportStatus` | enum `DispatchStepStatus` | Stato prima email. |
| `ReportSentAt` | `DateTime?` UTC | |
| `ReportError` | `string?` (max 1000) | Messaggio tecnico dell'errore, senza dati del paziente. |
| `PasswordStatus` | enum `DispatchStepStatus` | Stato seconda email. |
| `PasswordSentAt` | `DateTime?` UTC | |
| `PasswordError` | `string?` (max 1000) | |
| `IsLegacyImport` | `bool` | `true` per i record importati dal vecchio log. |

`DispatchStepStatus`: `Pending`, `Sent`, `Failed`, `Abandoned`, `Unknown` (solo import).

**La password del documento non viene mai salvata nel database**, né in chiaro né cifrata, né nei log.

Stato complessivo calcolato (non salvato), da mostrare nello storico:

- `Completato`: `ReportStatus == Sent && PasswordStatus == Sent`
- `Password non inviata`: `ReportStatus == Sent && PasswordStatus != Sent`
- `Non inviato`: `ReportStatus != Sent`
- `Importato (esito sconosciuto)`: `IsLegacyImport`

### 4.4 `EmailTemplate`

| Campo | Tipo | Note |
|---|---|---|
| `Id` | `int` | |
| `Kind` | enum `EmailTemplateKind` | `Report` (prima email) o `Password` (seconda email). **Un solo record per tipo** (indice univoco su `Kind`). |
| `Subject` | `string` (max 200) | Può contenere segnaposto. |
| `Body` | `string` | Testo semplice (non HTML), può contenere segnaposto. |
| `UpdatedAt` | `DateTime` UTC | |
| `UpdatedByUserId` | `string?` | |

Seed iniziale (dalla configurazione attualmente in uso, `config.json` nella root, **solo i testi, non le credenziali**):

- Report: oggetto `Referto`, corpo:
  ```
  Buongiorno {NOME} {COGNOME},
  allego il referto delle analisi.
  Riceverà una seconda email con la password per poterlo aprire.
  Grazie
  Poliambulatorio InSalute
  ```
- Password: oggetto `Credenziali`, corpo:
  ```
  Buongiorno {NOME} {COGNOME},
  questa è la password con cui aprire il file ricevuto nell'email precedente: {PASSWORD}
  Poliambulatorio InSalute
  ```

### 4.5 `SmtpSettings` (riga singola, `Id = 1`)

| Campo | Tipo | Note |
|---|---|---|
| `Host` | `string` | Es. `smtps.aruba.it`. |
| `Port` | `int` | Es. `465` o `587`. |
| `Security` | enum | `SslOnConnect`, `StartTls` (mappati su `MailKit.Security.SecureSocketOptions`). Mai `None`. |
| `UserName` | `string` | |
| `ProtectedPassword` | `string` | Password cifrata con `IDataProtector` (purpose `"InSalute.SmtpPassword"`). |
| `FromAddress` | `string` | Es. `info@poliambulatorioinsalute.it`. |
| `FromName` | `string` | Es. `Poliambulatorio InSalute`. |
| `UpdatedAt`, `UpdatedByUserId` | | |

La password SMTP non viene **mai** restituita alla UI: la pagina mostra "Password impostata ✓" e un campo per sostituirla.

---

## 5. Permessi

### 5.1 Matrice

| Azione | Operatore | Manager | Admin | SuperAdmin |
|---|:---:|:---:|:---:|:---:|
| Inviare referti | ✓ | ✓ | ✓ | ✓ |
| Vedere **tutti** gli invii nello storico | ✓ | ✓ | ✓ | ✓ |
| Cambiare la propria password | ✓ | ✓ | ✓ | ✓ |
| Creare / disattivare / riattivare **Operatori**, reimpostarne la password | | ✓ | ✓ | ✓ |
| Creare / disattivare / modificare **Manager e Admin** | | | ✓ | ✓ |
| Cambiare il ruolo di un utente (tranne verso/da SuperAdmin) | | | ✓ | ✓ |
| Impostazioni SMTP | | | ✓ | ✓ |
| Modelli email | | | ✓ | ✓ |
| Vedere e gestire account **SuperAdmin** | | | | ✓ |

Vincoli aggiuntivi:

- Nessuno può disattivare sé stesso né togliersi il proprio ruolo (evita di restare senza amministratori).
- Deve esistere sempre **almeno un Admin attivo** (SuperAdmin esclusi): il servizio rifiuta l'operazione che lo renderebbe falso.
- Ogni ricerca o elenco di utenti fatto da un non-SuperAdmin esclude i SuperAdmin **nella query del servizio** (non filtrando dopo in UI).

### 5.2 Implementazione

- Definire le policy in `Security/Policies.cs`: `CanSend` (tutti i ruoli, include anche la lettura dello storico), `CanManageUsers` (Manager, Admin, SuperAdmin), `CanManageSettings` (Admin, SuperAdmin), `IsSuperAdmin` (SuperAdmin).
- Le pagine usano `@attribute [Authorize(Policy = ...)]`. Il menu mostra solo le voci consentite (`AuthorizeView`).
- **I servizi in `Core` ricontrollano sempre i permessi** usando `ICurrentUser` (ruolo e Id presi **solo** dall'identità autenticata). Nascondere un pulsante non è un controllo di sicurezza.
- Mai fidarsi di un Id utente o di un ruolo passato come parametro dalla UI per decidere un permesso (è il bug del vecchio `UserController.Put`).
- Fallback: `options.FallbackPolicy` = utente autenticato. Tutte le pagine richiedono login tranne `Login`, `Lockout`, `AccessDenied`.

### 5.3 Regole Identity

- Password: minimo 10 caratteri, almeno una lettera e una cifra; nessun obbligo di simboli.
- Lockout: 5 tentativi falliti → blocco 15 minuti. Abilitato anche per i nuovi utenti.
- Cookie: `HttpOnly`, `Secure`, `SameSite=Strict`, scadenza scorrevole **30 minuti** di inattività, nessun "ricordami" (rimuovere la checkbox dal login).
- Login con **username oppure email**.
- Un utente con `IsActive == false` non può accedere (messaggio: "Account disattivato. Contatta un amministratore.").
- `MustChangePassword == true` → dopo il login reindirizzare a `/Account/Manage/ChangePassword` e bloccare ogni altra pagina finché non la cambia (middleware o controllo nel layout).
- Primo avvio: se non esiste nessun utente, creare un **SuperAdmin** con username preso da `Bootstrap:SuperAdminUserName` (default `assistenza`), password generata casualmente **scritta una sola volta nel log di avvio** con avviso evidente, `MustChangePassword = true`. Il SuperAdmin crea poi l'Admin del centro. Nessuna password di default fissa (il vecchio sistema usava "Password").

---

## 6. Pagine e flussi

Layout comune: barra in alto con logo e nome "InSalute", a destra il nome utente con menu (Profilo, Esci). Menu laterale:

1. **Invia referto** (`/`), icona invio
2. **Storico invii** (`/storico`), icona cronologia
3. **Utenti** (`/utenti`), solo `CanManageUsers`
4. **Impostazioni** → **SMTP**, **Modelli email**, solo `CanManageSettings`

Se le impostazioni SMTP non sono configurate, la pagina "Invia referto" mostra un avviso e il pulsante di invio è disabilitato. Per gli Admin l'avviso contiene il link alle impostazioni.

### 6.1 Invia referto: procedura guidata a 4 passi

Usare `MudStepper` (o equivalente) lineare: non si salta avanti, si può tornare indietro fino al passo 3 incluso. Lo stato della procedura vive nel componente `InviaReferto.razor` (un oggetto `DispatchDraft`), non in variabili statiche.

#### Passo 1: Paziente

Campi (tutti obbligatori):

| Campo | Validazione | Messaggio di errore |
|---|---|---|
| Cognome | non vuoto, max 100 | "Inserisci il cognome." |
| Nome | non vuoto, max 100 | "Inserisci il nome." |
| Email | formato email valido (usare `MailboxAddress.TryParse` di MimeKit + presenza di un punto nel dominio) | "L'indirizzo email non è valido." |
| Conferma email | uguale a Email, confronto senza differenza tra maiuscole e minuscole, dopo trim | "Le due email non coincidono." |

Gli errori compaiono **sotto il campo**, non in una finestra.

Controlli non bloccanti (avvisi gialli sotto il campo email, `RecipientChecks`):

1. **Refuso nel dominio:** se il dominio dista 1–2 caratteri (distanza di Levenshtein) da uno della lista `gmail.com, hotmail.it, hotmail.com, outlook.it, outlook.com, live.it, libero.it, yahoo.it, yahoo.com, icloud.com, virgilio.it, alice.it, tiscali.it, tim.it, pec.it` e non coincide esattamente, mostrare: "Forse intendevi …@gmail.com?" con un pulsante "Correggi" che sostituisce il dominio in entrambi i campi.
2. **Destinatario già usato per un altro paziente:** se nello storico esiste un invio a questa email con cognome diverso, mostrare: "Attenzione: questo indirizzo è stato usato in passato per un altro paziente (cognome diverso). Verifica che sia corretto." Non mostrare **quale** altro paziente (privacy).

Pulsante: **Avanti** (disabilitato finché ci sono errori bloccanti).

#### Passo 2: Documenti

- Area di caricamento con trascinamento (`MudFileUpload` con drag & drop, oppure `InputFile`) per i **referti**: da 1 a 10 file, massimo 20 MB ciascuno, 50 MB in totale.
- Estensioni ammesse: `.pdf, .jpg, .jpeg, .png, .doc, .docx, .odt, .txt`. Altrimenti: "Tipo di file non ammesso: <nome>."
- Controllo estensione **senza differenza tra maiuscole e minuscole** (il vecchio codice falliva con `.PDF`).
- Elenco dei file caricati come schede/righe: icona del tipo, nome, dimensione leggibile (es. "1,2 MB"), numero di pagine per i PDF, pulsante ✕ per rimuovere il singolo file. Caricare altri file **aggiunge** all'elenco, non sostituisce.
- Sezione separata, facoltativa: **Fattura** (un solo file PDF). La fattura **non** viene protetta: viaggia in chiaro come allegato separato della prima email (come nel sistema attuale, decisione D3). Sotto il campo, testo informativo: "La fattura sarà allegata senza password."
- Al caricamento, ogni file viene salvato in `TempFileStore` (vedi 7.5) e se ne calcola lo SHA-256.
- Se un PDF è già protetto da password (PDFsharp non riesce ad aprirlo), errore immediato sulla scheda del file: "Questo PDF è già protetto da password. Rimuovi la protezione o carica il file originale." Il file non viene accettato.
- Avviso non bloccante: se **nessun** nome di file contiene il cognome del paziente (confronto senza maiuscole, accenti e spazi), mostrare: "Nessun file contiene il cognome «Rossi» nel nome. Verifica di aver allegato i documenti giusti."

Pulsanti: **Indietro**, **Avanti** (disabilitato senza almeno un referto).

#### Passo 3: Riepilogo (bloccante)

Mostra, in sola lettura:

- **Destinatario** in grande: "Mario Rossi, mario.rossi@gmail.com".
- Elenco dei referti, con il nome del file protetto che riceverà il paziente (vedi 7.2), e, se presente, la fattura con la dicitura "allegata senza password".
- Tipo di protezione, a parole: "I documenti saranno uniti in un unico PDF protetto da password" oppure "I documenti saranno inseriti in un archivio ZIP protetto da password".
- **Anteprima delle due email** (oggetto e corpo con i segnaposto già sostituiti). Nella seconda il segnaposto `{PASSWORD}` è mostrato come `••••••••••••` (la password si genera solo all'invio).
- Checkbox obbligatoria: "Ho verificato che destinatario e documenti sono corretti."

Pulsanti: **Indietro**, **Invia referto** (primario, disabilitato finché la checkbox non è spuntata).

Al clic su **Invia referto**:
1. Disabilitare subito il pulsante (niente doppi invii).
2. Passare al passo 4 e avviare l'invio.

#### Passo 4: Esito

Due righe di stato aggiornate in tempo reale:

```
[spinner | ✓ | ✗]  Email con il referto        Inviata alle 10:42
[spinner | ✓ | ✗ | —]  Email con la password   Inviata alle 10:42
```

Casi:

| Situazione | Cosa mostra | Azioni |
|---|---|---|
| Entrambe inviate | Banner verde "Referto e password inviati a mario.rossi@gmail.com." | **Nuovo invio** (torna al passo 1 con tutto vuoto) |
| Prima fallita | ✗ sulla prima, "—" sulla seconda (non tentata). Messaggio comprensibile (vedi 7.4) + espandibile "Dettagli tecnici". | **Riprova invio referto**, **Annulla invio** |
| Prima ok, seconda fallita | ✓ sulla prima, ✗ sulla seconda + messaggio. | **Riprova invio password**, **Annulla** (con avviso: "Il paziente ha ricevuto il referto ma non la password. Se annulli dovrai rifare l'invio completo.") |

Regole **non negoziabili** (sono il bug principale del vecchio sistema):

- La seconda email (password) si invia **solo se** `ReportStatus == Sent` per **lo stesso** `Dispatch`.
- "Riprova invio referto" rimanda **la prima email**, poi, se riesce, la seconda.
- "Riprova invio password" rimanda **solo la seconda**, con **la stessa password** usata per l'allegato già inviato.
- "Annulla" imposta gli step non inviati a `Abandoned` e cancella i file temporanei.

La password del documento resta **solo in memoria** nell'oggetto `PreparedDispatch` tenuto dal componente (circuito Blazor). Se il circuito si perde (pagina chiusa, server riavviato) prima dell'invio della seconda email:
- il `Dispatch` resta con `PasswordStatus = Pending`/`Failed`;
- un servizio in background (vedi 7.5) dopo 60 minuti lo marca `Abandoned` e cancella i file temporanei;
- nello storico appare come "Password non inviata" con la nota: "Rifare l'invio completo: il paziente ha un file che non può aprire."

Finché l'invio è in corso o la seconda email non è partita, avvisare se l'utente prova a lasciare la pagina (`NavigationManager.RegisterLocationChangingHandler` + `beforeunload`).

### 6.2 Storico invii (`/storico`)

- `MudDataGrid` con paginazione lato server (25 righe per pagina), ordinata per data decrescente.
- Colonne: Data e ora, Paziente (Cognome Nome), Email, Operatore, Stato (chip colorato: verde Completato, arancione Password non inviata, rosso Non inviato, grigio Importato).
- Filtri: intervallo date (default: ultimi 30 giorni, **non** una data fissa come nel vecchio codice), testo libero su cognome/email, stato, operatore.
- Tutti i ruoli vedono tutti gli invii (serve quando un paziente chiama e risponde un operatore diverso da chi ha inviato).
- Gli invii fatti da un SuperAdmin mostrano "Assistenza tecnica" come operatore; il SuperAdmin non compare nel filtro operatore (salvo per un SuperAdmin).
- Griglia in **sola lettura** (nel vecchio `LogView.xaml` le colonne erano modificabili).
- Clic su una riga → `/storico/{id}`: dettaglio con tutti i campi, elenco file con SHA-256, errori tecnici. Nessuna azione di reinvio da qui nella prima versione.

### 6.3 Utenti (`/utenti`)

- Tabella: Nome visualizzato, Username, Email, Ruolo, Stato (Attivo/Disattivato), Creato il.
- Il Manager vede e gestisce solo gli Operatori. L'Admin vede tutti **tranne i SuperAdmin**. Il SuperAdmin vede tutti.
- **Nuovo utente**: pulsante che apre un dialog con form (Nome visualizzato, Username, Email, Ruolo; il Manager può creare solo Operatori). Alla creazione il sistema genera una password temporanea (12 caratteri, vedi 7.1) e la mostra **una sola volta** in un dialog con pulsante "Copia" e il testo "Comunica questa password all'utente. Al primo accesso dovrà cambiarla." `MustChangePassword = true`.
- Azioni per riga (menu ⋮ con etichette testuali, non sole icone): **Modifica** (dialog), **Reimposta password** (genera e mostra una nuova password temporanea, `MustChangePassword = true`), **Disattiva**/**Riattiva** (con conferma).
- Niente modifica inline nella griglia, niente "salva tutto" (il vecchio salvataggio massivo dava risultati parziali).
- Niente cancellazione definitiva: gli utenti restano per lo storico.

### 6.4 Impostazioni SMTP (`/impostazioni/smtp`, solo Admin)

- Form con i campi di 4.5. Per la password: se già impostata, testo "Password impostata ✓" + campo vuoto "Nuova password (lascia vuoto per non cambiarla)".
- Preset rapidi per Host, Porta e Sicurezza: "Aruba" (`smtps.aruba.it`, 465, SslOnConnect), "Gmail" (`smtp.gmail.com`, 587, StartTls), "Outlook / Microsoft 365" (`smtp.office365.com`, 587, StartTls). I preset compilano i campi, che restano modificabili.
- Pulsante **Invia email di prova**: chiede un indirizzo di destinazione (default: email dell'Admin), invia "Email di prova InSalute" e mostra l'esito o l'errore.
- Salvataggio con validazione (host non vuoto, porta 1–65535, indirizzo mittente valido).

### 6.5 Modelli email (`/impostazioni/modelli`, solo Admin)

- Due schede: **Email con il referto**, **Email con la password**.
- Per ciascuna: Oggetto, Corpo (area di testo multilinea), elenco dei segnaposto disponibili con pulsante "Inserisci" (`{NOME}`, `{COGNOME}`, e `{PASSWORD}` **solo** nel modello password).
- Anteprima a destra (o sotto, su schermi stretti) con dati di esempio "Mario Rossi", password `Ab3k9Qm2Xz7p`.
- Validazioni al salvataggio (in `EmailTemplateService`, non solo in UI):
  - Il modello **password** deve contenere `{PASSWORD}` nel corpo: "Il modello deve contenere {PASSWORD}."
  - Il modello **referto** **non** deve contenere `{PASSWORD}` né nell'oggetto né nel corpo: "Il modello del referto non può contenere la password." (altrimenti la password viaggerebbe insieme all'allegato).
  - Segnaposto sconosciuti (qualunque `{PAROLA}` non previsto) → errore: "Segnaposto non riconosciuto: {XYZ}."
  - Oggetto e corpo non vuoti.

### 6.6 Profilo (`/profilo`)

- Mostra Nome visualizzato, Username, Email, Ruolo (sola lettura).
- Link a "Cambia password" (pagina Identity).
- L'utente non può cambiare da solo ruolo, username o stato.

---

## 7. Servizi principali (comportamento dettagliato)

### 7.1 `PasswordGenerator`

- 12 caratteri presi dall'alfabeto senza caratteri ambigui: `ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789` (senza `0 O o 1 l I`).
- Usare `System.Security.Cryptography.RandomNumberGenerator.GetString(alfabeto, 12)`. Vietati `Random` e `Guid.NewGuid()`.
- Garantire almeno una maiuscola, una minuscola e una cifra (rigenerare se manca).
- Lo stesso generatore si usa per le password temporanee degli utenti.

### 7.2 Scelta della protezione e nome del file

Input: lista dei referti. La fattura **non** partecipa alla protezione.

1. Se **tutti** i referti sono PDF → **un unico PDF**: unire i file nell'ordine dell'elenco e cifrare. `ProtectionKind = PdfAes`. Il paziente lo apre con qualunque lettore PDF, senza programmi aggiuntivi.
2. Altrimenti → **ZIP AES-256** con dentro tutti i referti con il loro nome originale. `ProtectionKind = ZipAes`. Nel corpo della prima email aggiungere automaticamente, in fondo, la riga: "Per aprire l'archivio è necessario un programma che supporti gli archivi ZIP protetti AES, ad esempio 7-Zip (gratuito)."

Allegati della prima email: il file protetto e, se presente, la fattura **così com'è** (decisione D3 del proprietario: una fattura sanitaria rivela la prestazione eseguita, ma si mantiene il comportamento attuale).

Nome del file inviato: `Referto_{Cognome}_{Nome}_{yyyyMMdd}.pdf` (o `.zip`), con cognome e nome **sanificati**: niente accenti (é→e), solo lettere, cifre e `_`, massimo 60 caratteri.

### 7.3 `AttachmentProtector`

- Lavora su file in `TempFileStore`. Il risultato va nella stessa cartella temporanea dell'invio, **mai** accanto ai file originali dell'utente (il vecchio sistema lasciava `_encrypted.pdf` e `.zip` nelle cartelle delle postazioni).
- PDF: aprire ogni sorgente con `PdfReader.Open(..., PdfDocumentOpenMode.Import)`, copiare le pagine in un nuovo `PdfDocument`, impostare `SecuritySettings.UserPassword = password`, `OwnerPassword` = un'altra password casuale (non comunicata), consentire stampa ed estrazione per accessibilità, negare modifica.
- Cifratura PDF: da PDFsharp 6.1 il default è la **versione 4, AES a 128 bit**. Usare questa e impostarla **esplicitamente** con `SecuritySettings.SetEncryption(...)` (valore di `PdfDefaultEncryption` corrispondente a V4/AES-128), così un cambio di default futuro non la modifica. **Non** usare RC4 (V1–V3). **Non** usare la versione 5 (AES-256, PDF 2.0): alcuni lettori PDF più vecchi dei pazienti potrebbero non aprirla. Verificare nome esatto dell'enum sulla documentazione ufficiale della versione installata (https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Encryption.html).
- ZIP: `ZipOutputStream` con `Password = password`, e per **ogni** `ZipEntry` `AESKeySize = 256`. Livello di compressione 6.
- PDF già protetto in ingresso: eccezione di dominio `ProtectedPdfException` → messaggio del passo 2.
- Test obbligatori: vedi 11.

### 7.4 `DispatchService` e `IEmailSender`

Interfaccia di invio (in `Core`):

```
IEmailSender.SendAsync(EmailMessage message, CancellationToken ct) -> SendResult
SendResult = { Success: bool, ErrorCode: SendErrorCode, TechnicalDetails: string? }
SendErrorCode = None, AuthenticationFailed, ConnectionFailed, RecipientRejected, MessageTooLarge, Timeout, Unknown
```

`MailKitEmailSender`:
- Legge `SmtpSettings` dal DB a ogni invio (così le modifiche valgono subito), decifra la password con `IDataProtector`.
- `SmtpClient.Timeout = 60000` ms. Connessione, autenticazione, invio, disconnessione per ogni messaggio (volumi bassi, niente pool).
- Corpo in testo semplice UTF-8.
- Mappa le eccezioni MailKit in `SendErrorCode`: `AuthenticationException` → AuthenticationFailed; `SocketException`/`SslHandshakeException` → ConnectionFailed; `SmtpCommandException` con stato `RecipientNotAccepted` → RecipientRejected; `MessageSize` → MessageTooLarge; `OperationCanceledException`/`TimeoutException` → Timeout; altro → Unknown.
- **Non** lancia eccezioni verso il chiamante per errori SMTP: restituisce `SendResult`.

Messaggi per l'utente (in `Texts/Messaggi.cs`):

| Codice | Messaggio |
|---|---|
| AuthenticationFailed | "Il server di posta ha rifiutato le credenziali. Avvisa un amministratore di controllare le impostazioni SMTP." |
| ConnectionFailed | "Impossibile collegarsi al server di posta. Controlla la connessione a Internet e riprova." |
| RecipientRejected | "Il server di posta ha rifiutato l'indirizzo del destinatario. Controlla l'email del paziente." |
| MessageTooLarge | "Gli allegati sono troppo grandi per il server di posta. Riduci il numero o la dimensione dei file." |
| Timeout | "Il server di posta non ha risposto in tempo. Riprova tra qualche minuto." |
| Unknown | "Si è verificato un errore durante l'invio. Riprova; se il problema continua avvisa un amministratore." |

Flusso di `DispatchService`:

```
PrepareAsync(DispatchRequest, ct) -> PreparedDispatch
  1. Controlla permesso CanSend e che SmtpSettings esista.
  2. Valida di nuovo tutti i dati (non fidarsi della UI).
  3. Genera la password (7.1), crea il pacchetto protetto (7.2, 7.3).
  4. Salva il Dispatch con ReportStatus=Pending, PasswordStatus=Pending.
  5. Restituisce PreparedDispatch { DispatchId, Password (solo in memoria), ProtectedFilePath, FileName }.

SendReportAsync(PreparedDispatch, ct)
  - Precondizione: ReportStatus in {Pending, Failed}. Altrimenti non fa nulla.
  - Rende il modello Report, allega il file protetto, invia.
  - Aggiorna ReportStatus/ReportSentAt/ReportError e salva.

SendPasswordAsync(PreparedDispatch, ct)
  - Precondizione: ReportStatus == Sent E PasswordStatus in {Pending, Failed}. Altrimenti eccezione InvalidOperationException.
  - Rende il modello Password con {PASSWORD} = PreparedDispatch.Password, invia, senza allegati.
  - Aggiorna PasswordStatus/PasswordSentAt/PasswordError e salva.
  - Se Sent: cancella la cartella temporanea dell'invio.

AbandonAsync(PreparedDispatch, ct)
  - Step non Sent → Abandoned. Cancella la cartella temporanea.
```

Il componente del passo 4 chiama `SendReportAsync`, poi, solo se ha avuto successo, `SendPasswordAsync`.

### 7.5 `TempFileStore` e pulizia

- Cartella radice configurabile `Storage:TempPath`, default `C:\InSaluteData\tmp` (**fuori** dalla cartella dell'applicazione). Una sottocartella per invio, chiamata con un `DraftId` (Guid) generato all'apertura della procedura guidata. Lo stesso Guid diventa poi `Dispatch.Id` in `PrepareAsync`, così file e record restano collegati.
- "Nuovo invio", "Annulla" e l'uscita dalla pagina prima dell'invio cancellano la sottocartella del draft.
- Permessi NTFS: accesso solo all'identità dell'app pool e agli amministratori (vedi 12).
- Nome dei file su disco: GUID + estensione. Il nome originale si tiene solo in memoria e nel `Dispatch`.
- `TempCleanupHostedService` (BackgroundService), ogni 15 minuti:
  - cancella le sottocartelle più vecchie di 60 minuti;
  - marca `Abandoned` i Dispatch con step `Pending`/`Failed` più vecchi di 60 minuti.
- All'avvio dell'app, esegue subito una pulizia.

### 7.6 `TemplateRenderer`

- Sostituisce `{NOME}`, `{COGNOME}`, `{PASSWORD}` (maiuscole esatte). Nient'altro.
- Il nome e il cognome vengono inseriti come scritti dall'operatore, con trim.
- Restituisce errore se dopo la sostituzione resta un `{...}` (difesa in profondità oltre a 6.5).

---

## 8. Linee guida UI/UX

### 8.1 Tema

- Font: **Roboto** (da Google Fonts o self-hosted in `wwwroot/fonts`, sono già nel vecchio progetto in `Client/InSaluteClient/Resources/Fonts/`).
- Palette MudBlazor (`MudTheme`):
  - Primary `#3375B0`
  - Secondary `#6CBCCE`
  - Background `#FAFAFA`
  - Surface `#FFFFFF`
  - Success `#2E7D32`, Warning `#ED6C02`, Error `#C62828`
  - Testo secondario: non più chiaro di `#5F6B76` (il vecchio `#88939D` non rispetta il contrasto AA).
- Bordi arrotondati 6px (`LayoutProperties.DefaultBorderRadius`).
- Tema scuro: **non** nella prima versione.

### 8.2 Principi

1. **Una sola azione primaria per schermata** (pulsante pieno Primary). Le altre sono secondarie (outlined/text).
2. **Errori accanto al campo**, non in finestre. Esiti con Snackbar (successo: 4 s; errore: resta finché non viene chiuso).
3. **Nessuna operazione blocca l'interfaccia**: durante le attese, spinner o progress e pulsante disabilitato.
4. **Linguaggio da segreteria**, non tecnico. Il dettaglio tecnico va solo negli espandibili "Dettagli tecnici".
5. **Etichette testuali** su tutti i pulsanti d'azione. Icone sole ammesse solo con tooltip **e** `aria-label`.
6. **Conferma** (dialog) solo per azioni irreversibili o rischiose: invio (tramite la checkbox del riepilogo), disattivazione utente, annullamento con referto già inviato.
7. Il pulsante "Cancella" del vecchio profilo significava "Annulla": usare **sempre** "Annulla" per annullare e "Elimina"/"Disattiva" per le azioni distruttive.

### 8.3 Accessibilità e tastiera

- Tutto usabile da tastiera, con ordine di tabulazione naturale (ordine del DOM, niente `tabindex` positivi).
- Focus sempre visibile (non rimuovere l'outline di MudBlazor).
- Nel passo 1 il focus parte su "Cognome". `Invio` su un campo del passo equivale a "Avanti" se il passo è valido.
- Ogni campo ha un `Label` visibile, non solo un placeholder.
- Contrasto minimo WCAG AA (4,5:1 per il testo normale).

### 8.4 Layout e dimensioni

- Deve funzionare bene da **1280×720** in su. Su schermi più piccoli resta usabile (il menu laterale si richiude). Niente dimensioni minime rigide come il vecchio `MinWidth=1200 MinHeight=850`.
- Larghezza massima del contenuto della procedura guidata: 900px, centrato.

### 8.5 Testi

- Tutti in italiano, con la stessa terminologia ovunque: "referto", "paziente", "invio", "storico", "operatore".
- Messaggi dei servizi centralizzati in `Texts/Messaggi.cs`. Le etichette statiche possono stare nei file `.razor`.
- Il footer mostra l'anno corrente calcolato (`DateTime.Now.Year`), non scritto a mano.

### 8.6 Sensazione da programma installato

- **Schermata di riconnessione personalizzata** (`ReconnectModal.razor` / `components-reconnect-modal`): banner discreto in alto "Connessione al server persa, riconnessione in corso…" e, se fallisce, "Impossibile riconnettersi. [Ricarica la pagina]". Mai la schermata predefinita.
- `manifest.webmanifest` con nome "InSalute", `display: standalone`, colore tema `#3375B0`, icone 192 e 512, così le postazioni possono usare **"Installa come app"** di Edge o Chrome (finestra propria, icona nella barra delle applicazioni). Niente service worker offline: l'app senza server non deve funzionare.
- Titolo della scheda per pagina: "Invia referto · InSalute", ecc.

---

## 9. Configurazione e segreti

- `appsettings.json` (nel repo): solo valori non sensibili (`Storage:TempPath`, limiti upload, livelli di log).
- Connection string di produzione: **solo sul server**, tramite `appsettings.Production.json` (escluso da git) **oppure** variabile d'ambiente `ConnectionStrings__Default` impostata per il servizio Windows. Mai nel repo.
- Password SMTP: solo nel DB, cifrata con Data Protection (4.5).
- Data Protection: `PersistKeysToFileSystem(new DirectoryInfo(@"C:\InSaluteData\keys"))` + `ProtectKeysWithDpapi()`, `SetApplicationName("InSalute")`. La cartella delle chiavi va inclusa nel backup (se si perde, bisogna reinserire la password SMTP e tutti devono rifare il login).
- Aggiungere a `.gitignore`: `appsettings.Production.json`, `*.user`, `.vs/`, `bin/`, `obj/`, `config.json`, `*.pfx`.

---

## 10. Log applicativi e privacy

- Serilog su `C:\InSaluteData\logs\insalute-.log`, rotazione giornaliera, conservazione 30 file.
- **Vietato** scrivere nei log: email dei pazienti, nomi, cognomi, nomi dei file, password (documento, SMTP, utenti), contenuto delle email. Nei log si usa solo il `DispatchId`.
- Errori non gestiti: pagina di errore generica "Si è verificato un errore imprevisto. Riprova; se continua, avvisa un amministratore. Codice: {traceId}". Dettagli tecnici solo in ambiente Development.
- Non restituire mai eccezioni o stack trace all'interfaccia (il vecchio server lo faceva).

---

## 11. Test obbligatori

Progetto `tests/InSalute.Tests`. Ogni fase si chiude solo con i suoi test verdi. Usare un `IEmailSender` finto (`FakeEmailSender` che registra i messaggi e può essere configurato per fallire) e un DB SQLite in-memory o SQL Server LocalDB per i servizi.

| Area | Casi minimi |
|---|---|
| `PasswordGenerator` | Lunghezza 12; solo caratteri dell'alfabeto; contiene maiuscola, minuscola e cifra; 1000 generazioni tutte diverse. |
| `TemplateRenderer` | Sostituzione corretta; segnaposto rimasto → errore. |
| `EmailTemplateService` | Modello password senza `{PASSWORD}` → rifiutato; modello referto con `{PASSWORD}` → rifiutato; segnaposto sconosciuto → rifiutato. |
| `AttachmentProtector` | PDF singolo → si apre **solo** con la password ed è cifrato AES-128 (non RC4); più PDF → un PDF con la somma delle pagine; PDF + JPG → ZIP che si apre **solo** con la password ed è AES (verificare `AESKeySize` sull'entry letta); PDF già protetto → `ProtectedPdfException`; estensione `.PDF` maiuscola gestita; la fattura non viene mai inclusa nel pacchetto protetto. |
| `DispatchService` | Prima email fallisce → **nessuna** email password inviata; `SendPasswordAsync` con ReportStatus≠Sent → eccezione; retry dopo il fallimento della prima → invia prima e poi seconda; prima ok e seconda fallita, retry → invia **solo** la seconda con **la stessa** password; dopo il successo la cartella temporanea non esiste più; la password non compare in nessun campo del Dispatch salvato. |
| `RecipientChecks` | `gmial.com` → suggerisce `gmail.com`; `gmail.com` → nessun avviso; email usata per un altro cognome → avviso. |
| Permessi (servizi) | Operatore vede tutti gli invii; Manager che crea un Admin → rifiutato; Manager che disattiva un Manager → rifiutato; disattivare l'ultimo Admin → rifiutato (anche se esiste un SuperAdmin attivo); utente che si disattiva da solo → rifiutato. |
| SuperAdmin | Elenco utenti chiesto da Admin o Manager non contiene SuperAdmin; Admin che modifica/disattiva/reimposta un SuperAdmin (anche conoscendone l'Id) → rifiutato; Admin che assegna il ruolo SuperAdmin → rifiutato; invio di un SuperAdmin mostrato come "Assistenza tecnica" a un Admin; SuperAdmin vede sé stesso nell'elenco. |
| `TempCleanupHostedService` | Cartella più vecchia di 60 minuti cancellata, più recente mantenuta; Dispatch Pending vecchio → Abandoned. |
| bUnit (procedura guidata) | "Avanti" disabilitato con email non coincidenti; "Invia referto" disabilitato senza checkbox; al passo 4, con il fake che fallisce la prima, è visibile "Riprova invio referto" e non "Riprova invio password". |

Per le prove manuali usare un server SMTP finto locale come **smtp4dev** o **Papercut SMTP**. **Mai** usare dati o email di pazienti reali nei test.

---

## 12. Installazione sul server (servizio Windows)

Il server è un PC **Windows 10** dedicato su cui gira anche altro sotto IIS. Per questo InSalute **non** va in IIS ma gira come **servizio Windows** con il web server integrato di ASP.NET Core (Kestrel):

- IIS sulle edizioni client di Windows limita le richieste contemporanee, e il limite è condiviso con le altre applicazioni già presenti. Blazor Server tiene aperta una connessione WebSocket per ogni scheda del browser, e consumerebbe quel limite.
- Come servizio separato, InSalute non tocca la configurazione di IIS e non può essere disturbata dalle altre app.

**Non modificare** siti, app pool o impostazioni IIS esistenti. Prima di iniziare, annotare cosa c'è già (siti, binding e porte usate, istanze SQL Server).

> **Attenzione, Windows 10:** il supporto di Windows 10 è terminato il 14 ottobre 2025. Un server che tratta dati sanitari senza aggiornamenti di sicurezza è un rischio. Da segnalare al proprietario: aggiornare a Windows 11 Pro (o Windows Server) oppure attivare gli aggiornamenti estesi a pagamento (ESU). Verificare inoltre che .NET 10 sia supportato sulla build di Windows 10 installata; se non lo è, fermarsi e chiedere.

1. **Prerequisiti**
   - Pubblicare l'app **self-contained** per `win-x64` (`dotnet publish -c Release -r win-x64 --self-contained`), così sul server non serve installare il runtime .NET.
   - In `Program.cs`: `builder.Host.UseWindowsService()` (pacchetto `Microsoft.Extensions.Hosting.WindowsServices`).
2. **Cartelle**
   - `C:\Program Files\InSalute\` → file pubblicati.
   - `C:\InSaluteData\tmp`, `C:\InSaluteData\keys`, `C:\InSaluteData\logs`, `C:\InSaluteData\backup` → dati.
   - Creare un utente locale di servizio `svc-insalute` (password lunga casuale, "la password non scade", nessun accesso interattivo).
   - Permessi: `svc-insalute` con Modifica su `C:\InSaluteData\*` e Lettura su `C:\Program Files\InSalute`. Rimuovere l'ereditarietà su `C:\InSaluteData` e lasciare solo SYSTEM, Administrators e `svc-insalute`.
3. **Servizio**
   - Registrare il servizio: `sc.exe create InSalute binPath= "C:\Program Files\InSalute\InSalute.Web.exe" start= delayed-auto obj= ".\svc-insalute" password= <password>`.
   - Ripristino in caso di errore: riavvio automatico dopo 1 minuto (`sc.exe failure InSalute reset= 86400 actions= restart/60000/restart/60000/restart/60000`).
   - Variabile d'ambiente `ASPNETCORE_ENVIRONMENT=Production` per il servizio, oppure `appsettings.Production.json` nella cartella dell'app (non nel repo).
4. **Endpoint HTTPS di Kestrel** in `appsettings.Production.json`:
   - Un solo endpoint HTTPS su una porta **non usata da IIS** (verificare con `netstat -ano`; es. `8443`, oppure `443` se libera).
   - Certificato letto dall'archivio di Windows (`Certificate: { Subject, Store: "My", Location: "LocalMachine" }`) con permesso di lettura della chiave privata concesso a `svc-insalute`.
   - Nessun endpoint HTTP.
5. **Certificato HTTPS in rete locale** (scegliere una delle due strade e documentarla):
   - **A. Certificato autofirmato** (più semplice): creare con PowerShell sul server un certificato per il nome che useranno le postazioni (es. `insalute.studio.lan` **e** il nome macchina), con SAN corretti e validità 5 anni. Esportarlo **senza chiave privata** (.cer) e installarlo nelle "Autorità di certificazione radice attendibili" delle 3 postazioni.
   - **B. Dominio vero + Let's Encrypt** con challenge DNS (es. con win-acme): nessuna installazione sulle postazioni, ma serve un dominio di proprietà e il rinnovo automatico.
   - Il nome usato dalle postazioni (es. `insalute.studio.lan`) deve risolversi all'IP del server: tramite il DNS del router o della rete, oppure aggiungendo una riga nel file `hosts` di ciascuna postazione. Non usare nomi che finiscono in `.local` (conflitto con mDNS).
6. **Database**
   - Usare l'istanza SQL Server già presente. La versione non è nota: trovare l'istanza effettivamente usata nel `Web.config` della vecchia Web API pubblicata in IIS, poi eseguire `SELECT @@VERSION` con SQL Server Management Studio o `sqlcmd`. Annotare edizione e versione in `docs/installazione.md`.
   - Se la versione è **precedente a SQL Server 2016**: fermarsi e chiedere (serve almeno impostare `UseCompatibilityLevel` in EF Core, o aggiornare SQL Server).
   - Creare il database `InSaluteV2`.
   - Creare il login Windows `<NOMEPC>\svc-insalute` e assegnargli sul DB solo `db_datareader` e `db_datawriter`.
   - Le migrazioni **non** si applicano all'avvio dell'app: generare uno script idempotente (`dotnet ef migrations script --idempotent`) ed eseguirlo con un account amministratore SQL a ogni rilascio.
7. **Aggiornamenti**
   - `sc.exe stop InSalute` → copiare i nuovi file in `C:\Program Files\InSalute` (tenere una copia della versione precedente in `C:\InSaluteData\release-precedente` per tornare indietro) → applicare lo script SQL → `sc.exe start InSalute`.
   - Fare gli aggiornamenti fuori dall'orario della segreteria.
8. **Firewall**: aprire la porta del servizio **solo verso la rete locale** (profilo Privato/Dominio). **Nessuna** esposizione su Internet e nessun port forwarding sul router.
9. **Backup** (SQL Server Express non ha l'Agent):
   - Operazione pianificata di Windows ogni notte: `sqlcmd` con `BACKUP DATABASE InSaluteV2 TO DISK=...` in `C:\InSaluteData\backup\`, poi copia della cartella `backup` **e** di `C:\InSaluteData\keys` su un altro dispositivo (NAS, disco esterno o altro PC).
   - Conservare 30 giorni.
   - **Provare un ripristino** su un database di test prima di andare in produzione, e annotare la procedura.
10. **Postazioni**: aprire `https://insalute.studio.lan` (aggiungere `:8443` o la porta scelta, se non è 443) in Edge, verificare che il lucchetto sia valido, poi "App → Installa questo sito come app".

---

## 13. Decisioni prese con il proprietario

Tutte le domande iniziali hanno una risposta. Queste decisioni sono **definitive**: non riaprirle.

| # | Domanda | Decisione | Dove è applicata |
|---|---|---|---|
| D1 | Chi vede lo storico degli invii? | **Tutti i ruoli vedono tutti gli invii.** | 5.1, 6.2 |
| D2 | Per quanto si conserva lo storico? | Il centro non ha un termine: "per quanto necessario". **Nessuna cancellazione automatica.** Prevedere comunque il parametro `Retention:DispatchYears` (null = mai) nel servizio di pulizia, per attivarlo quando ci sarà un termine. | 7.5 |
| D3 | Fattura protetta o in chiaro? | **In chiaro**, allegata separatamente alla prima email, come oggi. | 6.1, 7.2 |
| D4 | Password via email o altro canale? | **Due email**, come oggi. Nessuna astrazione per SMS o altri canali nella prima versione. | 6.1, 7.4 |
| D5 | Active Directory? | **No**: account propri dell'app (Identity). | 5.3 |
| D6 | Windows e SQL Server del server? | **Windows 10.** Versione SQL da rilevare in installazione. Per Windows 10: hosting come servizio Windows, non IIS. | 2, 12 |
| D7 | Da dove arrivano i PDF? | Da **fonti diverse** (email, gestionale, ecc.). **Nessuna integrazione.** Conseguenze: il nome del file spesso non conterrà il cognome (l'avviso del passo 2 resta non bloccante) e i PDF già protetti saranno più frequenti (messaggio chiaro al passo 2). | 6.1 |
| D8 | Admin al primo avvio? | Ruolo **SuperAdmin** per lo sviluppatore, nascosto nell'interfaccia agli altri ruoli; il centro ha poi un proprio Admin. | 4.2, 5.1, 5.3 |
| D9 | PDFsharp supporta AES? | **Sì**, da PDFsharp 6.1 il default è AES-128 (versione 4). Usare quella, impostata esplicitamente. | 7.3 |

---

## 14. Fasi di lavoro

Ogni fase ha: compiti, criteri di completamento ("Fatto quando") e stima indicativa (giorni di lavoro di una persona esperta). **Non passare alla fase successiva se i criteri della precedente non sono soddisfatti.** Lavorare su un branch `feature/rifacimento` creato da `develop`, con un commit per ogni compito significativo.

### Fase 0: messa in sicurezza del sistema attuale (0,5–1 giorno)

Il vecchio sistema resta in uso per alcune settimane: va reso meno pericoloso subito. Queste modifiche si fanno **nel vecchio codice**, su `develop` **dopo** il punto 3 (il client ha modifiche non committate proprio nei file da correggere), poi si rilasciano su `master` con il solito flusso release.

1. **Cambiare la password della casella** `info@poliambulatorioinsalute.it` (la fa il proprietario, non il codice) e aggiornare il `config.json` delle postazioni.
2. Aggiungere `config.json` a `.gitignore`. Verificare con `git log --all -- config.json` che non sia mai stato committato. Se lo è stato, avvisare: la password va considerata compromessa.
3. Committare il lavoro non committato del client (compreso `ViewModel/ViewModelBase.cs`, che oggi non è tracciato ma è usato da tutti i ViewModel) su `develop`, prima di ogni altra cosa.
4. **Bug "Riprova"** in `Client/InSaluteClient/ViewModel/CoreViewModel.cs`: nel ramo in cui fallisce la **prima** email (righe ~453-457) **non** impostare `EmailSentCorrectly = false`, così il clic successivo rimanda entrambe le email. Verificare a mano: SMTP sbagliato → errore → SMTP corretto → "Invia" → arrivano referto **e** password.
5. Stesso file: un'eccezione non SMTP durante l'invio (`catch (Exception ex)` alle righe ~560-563) deve impostare `success = false`. Il log al server (righe ~565-575) va inviato solo se `success == true`.
6. `Server/REST_API/Controllers/UserController.cs`: togliere il campo `Password` dalla risposta di `Get(Users user)` (riga ~327).
7. Stesso file: eliminare l'endpoint `PostNoAuth` (`api/user/register`).
8. Tutti i controller: sostituire `Content(HttpStatusCode.BadRequest, ex)` e `CreateErrorResponse(..., ex)` con un messaggio generico (`"Errore interno."`) e codice 500.
9. `Put(...)` privato: per i manager, la condizione `user_to_edit.id != edited_user.id` (riga ~652) diventa `user_to_edit.id != <Id dell'utente autenticato, dal claim "Id">`. Il controllo sul cambio password (riga ~721) diventa `HasClaim("Id", user_to_edit.id.ToString())`. Mai usare `edited_user.id`, che arriva dal body ed è manipolabile.
10. Ricompilare, pubblicare server e client, verificare login, invio e gestione utenti.

**Fatto quando:** le 10 voci sono fatte, il server pubblicato non restituisce password né stack trace, e un invio con prima email fallita seguita da un retry fa arrivare entrambe le email.

### Fase 1: scheletro della soluzione (1–2 giorni)

1. Creare `src/InSalute.Web` con `dotnet new blazor --interactivity Server --all-interactive --auth Individual -f net10.0` (verificare i nomi delle opzioni con `dotnet new blazor --help`).
2. Spostare entità, DbContext e Identity in `src/InSalute.Core`, che `Web` referenzia.
3. Passare il provider a **SQL Server** (`UseSqlServer`), connection string di sviluppo su LocalDB o SQL Server locale.
4. Creare `tests/InSalute.Tests` (xUnit + bUnit) e la soluzione con tutti i progetti.
5. `Directory.Build.props` con `Nullable=enable`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`. Aggiungere `.editorconfig`.
6. Installare MudBlazor, configurare il tema (8.1) e il layout con il menu (6, intro). Pagine vuote con titolo per tutte le route di 3.2.
7. Pulire il template: rimuovere le pagine di esempio (Counter, Weather) e le pagine Identity elencate in 2.1.
8. Cultura `it-IT`, Serilog (10), pagina di errore generica.
9. Se la repository ha un remote su GitHub: workflow CI con `dotnet build` e `dotnet test` su ogni push e pull request.

**Fatto quando:** l'app parte in locale, mostra il layout con il tema, tutte le pagine (vuote) richiedono il login, la build è senza warning e la CI (se presente) è verde.

### Fase 2: utenti, ruoli e accesso (2–3 giorni)

1. `ApplicationUser` (4.1), ruoli (4.2), regole Identity (5.3), seed del primo Admin.
2. Policy e `ICurrentUser` (5.2).
3. Login con username o email, utenti disattivati bloccati, obbligo di cambio password.
4. `UserAdminService` con tutti i vincoli di 5.1.
5. Pagine Utenti (6.3) e Profilo (6.6). Restyling della pagina di login.
6. Migrazione EF iniziale.
7. Test della riga "Permessi" in 11.

**Fatto quando:** il SuperAdmin creato al primo avvio crea un Admin, l'Admin crea un Manager, il Manager crea un Operatore; Admin e Manager non vedono il SuperAdmin nell'elenco utenti; l'Operatore entra con la password temporanea ed è costretto a cambiarla; le operazioni vietate dalla matrice falliscono **anche chiamando direttamente il servizio**; i test sono verdi.

### Fase 3: impostazioni SMTP e modelli email (1–2 giorni)

1. Entità `SmtpSettings` ed `EmailTemplate`, migrazione, seed dei modelli (4.4).
2. Data Protection configurata come in 9.
3. `MailKitEmailSender` (7.4) e `SmtpSettingsService`.
4. `EmailTemplateService` con le validazioni di 6.5, `TemplateRenderer` (7.6).
5. Pagine Impostazioni SMTP (6.4) e Modelli email (6.5) con anteprima ed email di prova.
6. Test di `TemplateRenderer` ed `EmailTemplateService`.

**Fatto quando:** con smtp4dev in locale l'email di prova arriva; nel DB la password SMTP non è leggibile; i modelli con `{PASSWORD}` sbagliati non si salvano; i test sono verdi.

### Fase 4: protezione degli allegati e servizio di invio (2–3 giorni)

1. **Prima di tutto:** installare PDFsharp ≥ 6.1 e verificare sulla documentazione il valore esatto di `PdfDefaultEncryption` per V4/AES-128 (7.3).
2. `PasswordGenerator` (7.1), `TempFileStore` e `TempCleanupHostedService` (7.5).
3. `AttachmentProtector` (7.2, 7.3).
4. Entità `Dispatch` (4.3) e migrazione.
5. `DispatchService` (7.4) con la macchina a stati.
6. `RecipientChecks` (6.1, passo 1).
7. Test di 11 per queste aree.

**Fatto quando:** tutti i test di `PasswordGenerator`, `AttachmentProtector`, `DispatchService`, `RecipientChecks` e della pulizia sono verdi. In particolare: nessun percorso di codice invia la password prima del referto.

### Fase 5: procedura guidata "Invia referto" (3–4 giorni)

1. Pagina `InviaReferto.razor` con i 4 passi esattamente come in 6.1.
2. Upload con trascinamento, elenco dei file, rimozione del singolo file, errori per file.
3. Avvisi non bloccanti (dominio, destinatario già usato, nome file).
4. Riepilogo con anteprima delle email e checkbox.
5. Passo Esito con aggiornamento in tempo reale, retry separati, annullamento, avviso all'uscita dalla pagina.
6. Avviso SMTP non configurato.
7. Test bUnit di 11.

**Fatto quando:** con smtp4dev si completa un invio di un PDF, di tre PDF (un unico PDF), di PDF + JPG (ZIP AES), e si verifica che gli allegati si aprono solo con la password ricevuta; simulando un errore SMTP sulla prima email non parte la seconda; la cartella temporanea è vuota a fine invio; tutto è usabile solo da tastiera.

### Fase 6: storico invii (1 giorno)

1. Pagina Storico (6.2) con paginazione lato server, filtri e chip di stato.
2. Pagina Dettaglio.
3. Visualizzazione "Assistenza tecnica" per gli invii dei SuperAdmin, applicata nel servizio.

**Fatto quando:** tutti i ruoli vedono tutti gli invii; nessun Admin/Manager/Operatore vede il nome di un SuperAdmin nello storico; la griglia non è modificabile.

### Fase 7: import dei dati dal vecchio sistema (1 giorno)

Console app `tools/InSalute.LegacyImport` (.NET 10), parametri: `--source "<connection string vecchio DB>" --target "<connection string InSaluteV2>" [--dry-run]`.

1. Legge dal vecchio DB `InSalute` le tabelle `Users` (`id, email, username, password, creation_date, role`) e `Log` (`id, user_id, receiver_email, sending_time`) con query SQL semplici (`Microsoft.Data.SqlClient`). **Non** usare l'EDMX né il vecchio progetto `BusinessLogic`.
2. **Utenti:** per ogni utente non ancora importato (`LegacyId`), crea un `ApplicationUser` con stesso username ed email, ruolo mappato (`admin`→Admin, `manager`→Manager, `user`→Operatore), `CreatedAt` = `creation_date` (interpretata come ora italiana e convertita in UTC), **nessuna password** (`PasswordHash = null`), `IsActive = false`, `MustChangePassword = true`.
   - Le vecchie password **non** vengono migrate: sono cifrate con un algoritmo (Rijndael con blocco a 256 bit) che non esiste su .NET moderno, e gli utenti sono pochi. Un Admin riattiva gli utenti e assegna loro una password temporanea dalla pagina Utenti.
3. **Log:** per ogni riga crea un `Dispatch` con `IsLegacyImport = true`, `CreatedAt = sending_time` (convertita in UTC), `CreatedByUserId` = utente importato corrispondente, `RecipientEmail = receiver_email`, nome e cognome vuoti, `AttachmentsJson = "[]"`, `ProtectionKind = Unknown`, entrambi gli stati `Unknown`.
   - Nota: il vecchio sistema registrava **una riga per ogni email** (quindi due per invio, e anche per gli invii falliti). Raggruppare le righe con lo stesso utente e destinatario a distanza di meno di 2 minuti in un solo `Dispatch`.
4. Idempotente: rieseguendolo non crea duplicati.
5. `--dry-run`: mostra solo i conteggi (utenti, righe di log, Dispatch risultanti) senza scrivere.
6. Stampa a video solo conteggi, **mai** email o nomi.

**Fatto quando:** su una copia del vecchio DB il dry-run mostra conteggi coerenti, l'import reale li conferma, e una seconda esecuzione non aggiunge nulla.

### Fase 8: installazione sul server (1–2 giorni)

1. Rilevare la versione di SQL Server e la build di Windows 10 (12, punti iniziali e 6) e segnalare al proprietario la fine del supporto di Windows 10.
2. Eseguire tutti i passi di 12, nell'ordine.
3. Eseguire l'import (fase 7) sul DB di produzione.
4. Configurare SMTP con la casella reale e inviare un'email di prova a un indirizzo interno.
5. Provare il backup e il ripristino.
6. Scrivere `docs/installazione.md` con cosa è stato fatto, dove sono le cartelle, come si aggiorna, come si ripristina il backup, quale certificato è installato e quando scade.

**Fatto quando:** dalle 3 postazioni l'app si apre in HTTPS senza avvisi del browser, l'email di prova arriva, il ripristino del backup funziona, la documentazione esiste.

### Fase 9: prova in parallelo e passaggio (1–2 settimane di calendario, poco lavoro)

1. Il vecchio sistema (su IIS) e il nuovo (servizio Windows) convivono sul server.
2. Una postazione usa il nuovo sistema per gli invii reali per alcuni giorni. Raccogliere i problemi e correggerli.
3. Poi tutte e 3 le postazioni.
4. Quando il proprietario conferma: fermare il vecchio sito/app pool (non cancellarlo per un mese), disinstallare il client WPF dalle postazioni.
5. **Sulle postazioni:** con il personale, cercare e cancellare le copie cifrate abbandonate dal vecchio programma (`*_encrypted.pdf` e gli `.zip` creati accanto ai referti), solo dopo aver verificato che sono copie e non originali.

**Fatto quando:** per una settimana tutti gli invii passano dal nuovo sistema senza problemi bloccanti.

### Fase 10: pulizia della repository (0,5 giorni)

1. Creare il tag `legacy-final` sull'ultimo commit con il vecchio codice.
2. Spostare `Client/` e `Server/` in `legacy/` (o rimuoverli: il tag li conserva).
3. Rimuovere dal repo `Client/InSaluteClient/Lib/BusinessLogic.dll`, `Server/.vs/`, i file `*.user`.
4. Aggiornare il `README.md` (oggi vuoto): cos'è, come si avvia in sviluppo, come si testa, link a `docs/installazione.md`.

**Fatto quando:** la repository contiene solo il nuovo sistema più il tag del vecchio, e il README permette a un nuovo sviluppatore di avviare l'app in locale.

### Stima complessiva

| Fase | Giorni |
|---|---|
| 0 | 0,5–1 |
| 1 | 1–2 |
| 2 | 2–3 |
| 3 | 1–2 |
| 4 | 2–3 |
| 5 | 3–4 |
| 6 | 1 |
| 7 | 1 |
| 8 | 1–2 |
| 10 | 0,5 |
| **Totale** | **circa 13–20 giorni di lavoro**, più 1–2 settimane di calendario per la fase 9 |

---

## 15. Regole per chi implementa (da rileggere prima di ogni fase)

**Fare sempre:**
- Seguire questo documento alla lettera. Se un punto sembra sbagliato o mancante, fermarsi e chiedere.
- Scrivere i test indicati nella fase **prima** di considerarla chiusa.
- Controllare i permessi nei servizi, non solo nella UI.
- Usare `async`/`await` con `CancellationToken` per tutto l'I/O.
- Usare `RandomNumberGenerator` per qualsiasi segreto.
- Cancellare i file temporanei in **ogni** percorso (successo, errore, annullamento).

**Non fare mai:**
- Scrivere algoritmi di cifratura o hash a mano. Usare solo Identity, Data Protection, PDFsharp e SharpZipLib come indicato.
- Salvare la password del documento (DB, file, log, cache, `localStorage`).
- Inviare l'email con la password se quella con il referto non risulta `Sent`.
- Mettere segreti, connection string reali o dati di pazienti nel repository, nei test o nei log.
- Restituire eccezioni o stack trace all'interfaccia.
- Decidere permessi in base a Id o ruoli ricevuti dalla UI.
- Aggiungere librerie non elencate in 2 senza chiedere (il vecchio progetto aveva pacchetti mai usati: EventFlow, RtfToHtml, SimpleModal, EntityFramework nel client).
- Aggiungere funzioni non richieste (archivio referti, tema scuro, SMS, API pubbliche, registrazione utenti). Annotarle come proposte nel README.
- Modificare `Client/` e `Server/` fuori dalla fase 0.
- Toccare altre applicazioni o configurazioni presenti sul server IIS.
- Esporre l'applicazione su Internet.
