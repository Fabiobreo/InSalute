window.inSalute = {
    // Avvisa il browser se si chiude o si ricarica la scheda mentre un invio è a metà.
    setUnloadGuard: enabled => {
        window.onbeforeunload = enabled ? e => { e.preventDefault(); e.returnValue = ''; } : null;
    },

    submitForm: id => document.getElementById(id)?.submit(),

    // Inserisce il testo alla posizione del cursore e notifica Blazor con un evento "input".
    insertAtCursor: (id, text) => {
        const el = document.getElementById(id);
        if (!el) {
            throw new Error('Campo non trovato');
        }
        const start = el.selectionStart ?? el.value.length;
        const end = el.selectionEnd ?? start;
        el.setRangeText(text, start, end, 'end');
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.focus();
    }
};

// Pagine dell'account (rendering statico, senza circuito): comportamenti con eventi delegati, così funzionano
// anche dopo la navigazione "enhanced" di Blazor, che sostituisce il contenuto senza ricaricare gli script.

// Mostra/nascondi password.
document.addEventListener('click', e => {
    const button = e.target.closest('[data-toggle-password]');
    if (!button) {
        return;
    }
    const input = document.getElementById(button.dataset.togglePassword);
    if (!input) {
        return;
    }
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    const label = show ? 'Nascondi password' : 'Mostra password';
    button.setAttribute('aria-pressed', String(show));
    button.setAttribute('aria-label', label);
    button.title = label;
    input.focus();
});

// Avviso di blocco maiuscole sui campi password.
['keydown', 'keyup'].forEach(type => document.addEventListener(type, e => {
    if (!(e.target instanceof HTMLInputElement) || typeof e.getModifierState !== 'function') {
        return;
    }
    const hint = document.querySelector(`[data-caps-for="${CSS.escape(e.target.id)}"]`);
    if (hint) {
        hint.hidden = !e.getModifierState('CapsLock');
    }
}));

// Durante l'invio del modulo il pulsante si disabilita e dice cosa sta succedendo (niente doppi clic).
document.addEventListener('submit', e => {
    const form = e.target;
    const text = form.dataset?.busyText;
    const button = form.querySelector('button[type=submit]');
    if (!text || !button) {
        return;
    }
    // Dopo l'avvio dell'invio: disabilitarlo prima lo escluderebbe dal modulo.
    setTimeout(() => {
        button.disabled = true;
        button.textContent = text;
    });
});
