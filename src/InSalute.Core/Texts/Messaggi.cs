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
}
