# Verifica della versione 1.6.0

## Eseguito

- Compilazione Release .NET 8: zero errori e zero avvisi.
- Suite `tests/LayoutTests`: 16 controlli superati, eseguita con cultura italiana.
- Verificate importazione delle unità di misura, geometria di dialoghi e personaggi, salvataggio e riapertura FDX e DLite, compatibilità DLite v2, clone indipendente, fallback per dati invalidi, A4 con namespace e paginazione secondo la larghezza.

## Da eseguire su Windows prima della release pubblica

1. Aprire un file FDX reale e controllare visivamente rientri di scena, azione, personaggio, dialogo e transizione.
2. Ridimensionare la finestra e il navigatore con “Adatta alla finestra” attivo: tutto il foglio deve rimanere raggiungibile.
3. Trascinare il bordo destro, provare zoom 100% e 200%, scorrimento orizzontale e ritorno con Ctrl+9.
4. Provare “Dimensioni del foglio” con 25 cm di larghezza e margini da 3 cm: controllare i nuovi a capo e la conservazione di testo, tipi, note, colori e selezione.
5. Salvare in DLite e FDX, chiudere e riaprire: controllare le stesse misure. Aprire poi un documento nuovo per verificare il ripristino delle impostazioni standard.
6. Esportare PDF con formato “Documento” e confrontare larghezza e posizione dei blocchi. Provare anche A4 e Letter scelti esplicitamente.
7. Ripetere su schermi Windows con scala 100%, 150% e 200%.

L'applicazione Windows non è stata eseguita su questo Mac. Nessun test visivo su Windows è dichiarato completato.

## Riferimento per le unità FDX

La lettura di PageSize, PageLayout ed ElementSettings è stata confrontata con questo [file FDX pubblico](https://github.com/rsdoiel/fdx/blob/main/testdata/sample-01.fdx). I test inclusi usano contenuti sintetici.
