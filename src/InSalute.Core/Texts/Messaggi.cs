using InSalute.Core.Email;

namespace InSalute.Core.Texts;

/// <summary>Messaggi di errore/esito restituiti dai servizi.</summary>
public static class Messaggi
{
    public const string NonAutorizzato = "Non hai i permessi per eseguire questa operazione.";
    public const string UtenteNonTrovato = "Utente non trovato.";
    public const string NomeVisualizzatoObbligatorio = "Inserisci il nome visualizzato.";
    public const string NomeVisualizzatoTroppoLungo = "Il nome visualizzato può avere al massimo 100 caratteri.";
    public const string UsernameObbligatorio = "Inserisci il nome utente.";
    public const string EmailObbligatoria = "Inserisci l'indirizzo email.";
    public const string RuoloNonValido = "Il ruolo selezionato non è valido.";
    public const string NonPuoiDisattivareTeStesso = "Non puoi disattivare il tuo stesso account.";
    public const string NonPuoiCambiareIlTuoRuolo = "Non puoi cambiare il tuo ruolo.";
    public const string NonPuoiCambiareIlTuoUsername = "Non puoi cambiare il tuo nome utente.";
    public const string RuoloSuperAdminNonModificabile = "Il ruolo di assistenza tecnica non può essere assegnato o tolto da qui.";
    public const string UltimoAdmin = "Deve esistere almeno un amministratore attivo.";
    public const string AccountDisattivato = "Account disattivato. Contatta un amministratore.";
    public const string CredenzialiNonValide = "Nome utente o password non corretti.";
    public const string PasswordTroppoCorta = "La password deve avere almeno 10 caratteri.";
    public const string PasswordSenzaCifra = "La password deve contenere almeno una cifra.";
    public const string PasswordSenzaLettera = "La password deve contenere almeno una lettera.";
    public const string UsernameGiaInUso = "Questo nome utente è già in uso.";
    public const string EmailGiaInUso = "Questo indirizzo email è già in uso.";
    public const string EmailNonValida = "L'indirizzo email non è valido.";
    public const string UsernameNonValido = "Il nome utente può contenere solo lettere, cifre e i simboli - . _ @ +.";
    public const string ErroreGenerico = "Si è verificato un errore imprevisto. Riprova; se continua, avvisa un amministratore.";

    public const string HostObbligatorio = "Inserisci il server di posta (host).";
    public const string PortaNonValida = "La porta deve essere un numero tra 1 e 65535.";
    public const string SicurezzaNonValida = "Seleziona il tipo di connessione sicura.";
    public const string MittenteNonValido = "L'indirizzo del mittente non è valido.";
    public const string NomeMittenteTroppoLungo = "Il nome del mittente può avere al massimo 100 caratteri.";
    public const string PasswordSmtpObbligatoria = "Inserisci la password del server di posta.";
    public const string SmtpNonConfigurato = "Le impostazioni SMTP non sono ancora state configurate.";
    public const string IndirizzoProvaNonValido = "L'indirizzo email di prova non è valido.";
    public const string ModelloVuoto = "Oggetto e corpo del modello non possono essere vuoti.";
    public const string ModelloOggettoTroppoLungo = "L'oggetto può avere al massimo 200 caratteri.";
    public const string ModelloPasswordSenzaSegnaposto = "Il modello deve contenere {PASSWORD}.";
    public const string ModelloRefertoConPassword = "Il modello del referto non può contenere la password.";
    public const string ModelloNonTrovato = "Modello non trovato.";

    public const string CognomeObbligatorio = "Inserisci il cognome.";
    public const string NomePazienteObbligatorio = "Inserisci il nome.";
    public const string CognomeONomeTroppoLunghi = "Nome e cognome possono avere al massimo 100 caratteri.";
    public const string NessunReferto = "Carica almeno un referto.";
    public const string TroppiReferti = "Puoi caricare al massimo 10 referti.";
    public const string TotaleTroppoGrande = "I referti superano i 50 MB in totale.";
    public const string FatturaNonPdf = "La fattura deve essere un file PDF.";
    public const string FileNonDisponibile = "Uno dei file caricati non è più disponibile. Carica di nuovo i documenti.";
    public const string PdfGiaProtetto = "Questo PDF è già protetto da password. Rimuovi la protezione o carica il file originale.";
    public const string PdfNonLeggibile = "Il file PDF non è leggibile. Controlla che non sia danneggiato.";
    public const string InvioGiaPreparato = "Questo invio è già stato avviato.";
    public const string InvioNonTrovato = "Invio non trovato.";
    public const string FileTemporaneiNonDisponibili = "I file dell'invio non sono più disponibili. Annulla e rifai l'invio completo.";
    public const string InvioNonPiuValido = "Questo invio non è più valido (è scaduto o è stato annullato). Annulla e rifai l'invio completo.";
    public const string NotaArchivioZip = "Per aprire l'archivio è necessario un programma che supporti gli archivi ZIP protetti AES, ad esempio 7-Zip (gratuito).";

    public static string FileTroppoGrande(string fileName) => $"Il file è troppo grande (massimo 20 MB): {fileName}.";

    public static string TipoFileNonAmmesso(string fileName) => $"Tipo di file non ammesso: {fileName}.";

    public static string SegnapostoNonRiconosciuto(string placeholder) => $"Segnaposto non riconosciuto: {placeholder}.";

    public static string SegnapostoRimasto(string placeholder) => $"Segnaposto non sostituito: {placeholder}.";

    public const string OggettoEmailProva = "Email di prova InSalute";
    public const string CorpoEmailProva = "Questa è un'email di prova inviata da InSalute. Se la ricevi, le impostazioni SMTP sono corrette.";

    public static string PerErroreInvio(SendErrorCode code) => code switch
    {
        SendErrorCode.AuthenticationFailed => "Il server di posta ha rifiutato le credenziali. Avvisa un amministratore di controllare le impostazioni SMTP.",
        SendErrorCode.ConnectionFailed => "Impossibile collegarsi al server di posta. Controlla la connessione a Internet e riprova.",
        SendErrorCode.RecipientRejected => "Il server di posta ha rifiutato l'indirizzo del destinatario. Controlla l'email del paziente.",
        SendErrorCode.MessageTooLarge => "Gli allegati sono troppo grandi per il server di posta. Riduci il numero o la dimensione dei file.",
        SendErrorCode.Timeout => "Il server di posta non ha risposto in tempo. Riprova tra qualche minuto.",
        _ => "Si è verificato un errore durante l'invio. Riprova; se il problema continua avvisa un amministratore.",
    };
}
