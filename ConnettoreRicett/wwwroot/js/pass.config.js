function hdProjectData(state) {
    const prjData = {
        apikey: '1eb6a220177a5102bec9524243d21d35c98ccdc7'   // API Key Help Desk
        , redmine_url: 'https://pass.regione.marche.it'      // FISSO
        , project_id: 439                                    // FISSO - id del progetto ProcediMarche su Pass
        , list_url: '/Home/Segnalazioni'                    // DA EDITARE
        , issue_url: '/Home/Segnalazione'                   // DA EDITARE
        , cfid_segnalatore: 2                               // FISSO
        , cfname_segnalatore: 'segnalatore'                 // FISSO
        , cfid_ultimo_accesso: 9                            // FISSO
        , update_url: 'https://pass.regione.marche.it/apirm/' // FISSO
        , include_closed: false                              // EDITABILE: nella lista delle segnalazioni include quelle chiuse (sì/no)
        , status_id: null                                   // EDITABILE - se non indicato imposta lo stato di default a “Nuovo”
        , custom_fields: null
        , default_category_id: 389                          // EDITABILE - categoria assegnata al ticket (11 = helpdesk)
        , default_hd_state: 'contracted'
        , parent_issue_id: null
        , watcher_user_id: null                             // EDITABILE - per impostare osservatore del ticket
        , is_private: null                                  // ticket privato (sì/no)
        , tracker_id: null
        , categories: []                                    // EDITABILE - se vuoi far scegliere all'utente una serie di possibili categorie
    };
    return Object.assign(state, prjData);
};