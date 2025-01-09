function rimuoviElementoDallaLista(el) {
    var spanValore = el.siblings('.valore-elemento');
    var hidden = el.parent().parent().parent().siblings('input');

    const arrayValori = hidden.val().split(";");

    // Controlla se il valore è presente nell'array
    let index = $.inArray(spanValore.text(), arrayValori);

    // Se il valore è presente, rimuovilo
    if (index !== -1) {
        arrayValori.splice(index, 1);
    }

    // Converti l'array in una stringa separata da ";"
    let resultString = arrayValori.join(";");

    hidden.val(resultString);

    el.parent().parent().remove();

}

function aggiungiElementoDallaLista(el) {
    var spanValore = el.parent().siblings('input');
    var hidden = el.parent().parent().siblings('input');

    const arrayValori = hidden.val().split(";");

    // Controlla se il valore NON è presente nell'array
    if (arrayValori.indexOf(spanValore.val()) === -1) {
        // Aggiungi il valore all'array
        arrayValori.push(spanValore.val());

        // Converti l'array in una stringa separata da ";"
        let resultString = arrayValori.join(";");

        hidden.val(resultString);

        var listaElementi = el.parent().parent().siblings(".lista-elementi");

        listaElementi.append(badgeValore.replace('{{Valore}}', spanValore.val()));
    }

    // pulisco la input textbox
    spanValore.val('');
}

$(document).ready(function () {
    // Intercetta l'evento submit del form
    $('form').on('submit', function (event) {
        // Trova l'input di tipo hidden con la classe 'hidden-lista-con-inserimento' all'interno del form
        var hiddenInput = $(this).find('input.hidden-lista-con-inserimento[type="hidden"]');

        // Verifica se l'input hidden è presente nel form
        if (hiddenInput.length > 0) {
            // Verifica se l'input hidden è valorizzato
            if (hiddenInput.val()) {

            } else {
                alert('Campo ' + hiddenInput.attr('id') + ' non valorizzato');
                // Previene l'invio del form se l'input hidden non è valorizzato
                event.preventDefault();
            }
        }
    });
});

const badgeValore =
    `<div class="h6">
        <span class="badge bg-secondary">
            <span class="valore-elemento">{{Valore}}</span>
            <button
                type="button"
                class="btn-close text-white bg-danger"
                    aria-label="Close" onclick="rimuoviElementoDallaLista($(this));"></button>
        </span>
    </div>`;