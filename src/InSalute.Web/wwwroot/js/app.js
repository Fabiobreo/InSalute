window.inSalute = {
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
