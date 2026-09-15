# DraftLite

Scrittura di sceneggiature per Windows: la parte utile di Final Draft, senza le altre mille.

Un editor che sa cos'è una sceneggiatura — margini, maiuscole, rientri, paginazione — e che
sta zitto su tutto il resto. Niente licenze, niente abbonamenti, un solo eseguibile.

![DraftLite](docs/icon.png)

---

## Novità 1.6.0: area di scrittura e importazione Final Draft

- **Visualizza → Dimensioni del foglio…**: cambia la larghezza e i margini in centimetri. Il testo si dispone nella nuova larghezza; i rientri si ridimensionano in proporzione.
- **Adatta alla finestra** (`Ctrl+9`): il foglio bianco occupa lo spazio disponibile e segue il ridimensionamento della finestra.
- Trascina gli ultimi 10 pixel del bordo destro del foglio per regolare lo zoom. Questa operazione cambia la dimensione a video, mantenendo le misure del documento. A zoom elevato è disponibile lo scorrimento orizzontale.
- Aprendo `.fdx` o `.fdxt`, vengono lette le dimensioni pagina, i margini verticali e i rientri orizzontali degli `ElementSettings` per i sei tipi supportati. Il foglio si adatta automaticamente alla finestra.
- La geometria viene conservata nei file `.dlite` (versione 3, lettura compatibile con versione 2), nell'autosalvataggio e nell'esportazione `.fdx`.
- Il PDF propone **Documento** come formato iniziale, per mantenere le dimensioni importate. La paginazione usa le larghezze dei tipi di paragrafo.

L'importazione non riproduce ogni dettaglio di Final Draft: font, interlinea, rientro della prima riga e formattazioni locali diverse dalle impostazioni del tipo restano gestiti come nella versione precedente. I dialoghi simultanei mantengono le colonne previste dal motore esistente. La numerazione delle pagine può quindi differire da Final Draft.

### Verifiche

```sh
dotnet build src/DraftLite/DraftLite.csproj -c Release
dotnet run --project tests/LayoutTests
```

I controlli coprono misure FDX, cultura italiana, A4 e Letter, dati non validi, isolamento dei documenti, salvataggi FDX/DLite e larghezza di paginazione. La compilazione è stata verificata da macOS; le interazioni WinForms richiedono una prova su Windows.


## La barra

Sei linguette — **File, Home, Inserisci, Formato, Vista, Modifica** — con i comandi raggruppati
come nei programmi di scrittura veri: appunti, carattere, elementi, viste, zoom, aspetto. I
pulsanti sono disegnati dal programma, quindi seguono il tema (anche quello scuro) e non c'è
nessuna libreria di terze parti dietro. Se la finestra si stringe, i gruppi di destra tengono la
sola icona invece di sparire: il comando resta lì, la scritta va nel suggerimento.

Sopra il foglio c'è il **righello** in pollici, con i due indicatori che mostrano dove comincia e
dove finisce l'elemento su cui stai scrivendo: il dialogo rientra, il personaggio sta più in
dentro, l'azione tiene tutta la riga. Si spegne da *Vista*.

Tutto quello che c'è nella barra sta anche nei menu, con le stesse scorciatoie: la barra è solo
la strada più corta.

## Scrivere

**Formattazione automatica.** Sei elementi (Scena, Azione, Personaggio, Parentetica, Dialogo,
Transizione). `Invio` passa all'elemento che viene dopo per logica — dopo il nome del
personaggio c'è il dialogo, dopo il dialogo c'è l'azione — `Tab` cambia tipo. Rientri e
maiuscole li mette lui.

**Suggerimenti mentre scrivi.** I nomi dei personaggi già usati, le intestazioni di scena già
scritte, i momenti della giornata dopo il trattino. Frecce per scegliere, `Invio` o `Tab` per
confermare, `Esc` per ignorare.

**Dialogo simultaneo.** `Ctrl+D` su un nome di personaggio e la sua battuta viene affiancata a
quella precedente: nel PDF escono su due colonne, come quando due personaggi parlano sopra.

**Note ancorate.** `Ctrl+M` attacca un appunto a una riga: resta agganciato a quella riga anche
se sopra ne aggiungi o ne togli venti. La riga si evidenzia, e tutte le note stanno insieme nel
pannello laterale.

**Schede scena.** `F6` apre la bacheca: una scheda per scena, con sinossi e colore. Si
trascinano per riordinare il copione, doppio clic per saltare alla scena.

**Il menù degli elementi.** Vai a capo e compare l'elenco con l'elemento che verrebbe da solo
già selezionato: frecce e `Invio` per cambiarlo, oppure continui a scrivere e sparisce senza
darti fastidio. Si spegne dalle preferenze se preferisci il flusso liscio.

**Grassetto, corsivo, sottolineato.** `Ctrl+B`, `Ctrl+I`, `Ctrl+U` come in qualunque editor.
Vengono salvati come marcatori Fountain (`**così**`, `*così*`, `_così_`), quindi sopravvivono a
tutti i formati, e nel PDF escono davvero in Courier grassetto, obliquo e sottolineato.

**Ogni elemento ha il suo colore.** Scene, personaggi, parentetiche e transizioni si
distinguono a colpo d'occhio mentre scorri — e i colori li decidi tu, uno per uno, in
*Aspetto*: se li vuoi tutti neri come su carta, basta metterli neri.

**Colore e evidenziatore sul testo.** Selezioni e colori, come in Word: una battuta in rosso
perché non convince, una riga evidenziata in giallo da rivedere domani. Restano nel file `.dlite`
e a schermo; nel PDF di consegna spariscono, a meno che tu non spunti *Stampa anche i colori*
nella finestra di esportazione.

**Come ti pare.** Tema carta (avorio, riposante) oltre a chiaro, seppia e scuro; carattere a
scelta tra quelli a larghezza fissa; ingrandimento dal 75% al 200%; macchina da scrivere che
tiene la riga corrente a metà schermo (`F11`).

**Maiuscolo e minuscolo.** `Shift+F3` gira la selezione fra TUTTO MAIUSCOLO, tutto minuscolo e
Iniziali Maiuscole, un paragrafo per volta: il tipo di ogni elemento resta quello che era.

**Interruzione di pagina.** `Ctrl+Invio` mette una riga di `===` e da lì in poi si stampa su una
pagina nuova — la convenzione di Fountain, quindi il file resta leggibile anche fuori da qui.

**Seleziona la scena.** `Ctrl+Shift+A` prende tutta la scena su cui sei, dall'intestazione a
quella dopo: comoda per spostarla o cancellarla in un colpo solo.

**Vai a.** `Ctrl+G` salta a una scena o a una pagina per numero.

**Elenco personaggi.** `F4` apre chi parla, quante battute, quante parole, in quante scene e da
quale scena: da lì si rinomina un personaggio in tutto il copione (le estensioni tipo (V.O.)
restano al loro posto) o si esportano i suoi sides.

**Simboli.** Trattino lungo, puntini, virgolette basse: quelli che sulla tastiera italiana non
ci sono e che in una sceneggiatura servono di continuo.

**Ripristina il paragrafo.** Toglie da una riga i grassetti, i corsivi e i colori messi a mano e
la riporta com'era prevista dal suo tipo.

**Le pagine si vedono.** Nel margine del foglio compaiono il numero di pagina e la riga di
stacco dove il PDF andrà a capo: sai sempre a che pagina sei mentre scrivi.

## Produrre

**Revisioni.** Congeli la bozza e da quel momento ogni riga cambiata prende l'asterisco a
margine, con il colore della bozza stampato in testa alla pagina (Bianca, Blu, Rosa, Gialla...
nell'ordine standard di produzione).

**Numeri di scena bloccati.** Blocchi la numerazione: le scene inserite dopo diventano 12A, 12B
e le vecchie non slittano. Nel PDF i numeri escono su entrambi i margini.

**Sides per attore.** Un PDF con le sole scene in cui compare un personaggio, numeri di scena
originali compresi, da mandargli senza girare tutto il copione.

**Filigrana e copie nominative.** "BOZZA — NON DISTRIBUIRE" in diagonale su ogni pagina, e il
nome del destinatario in fondo: se la copia gira, si sa da dove è partita.

**Statistiche.** Pagine, durata stimata, scene per interni/esterni e giorno/notte, battute e
parole per personaggio, elenco scene con la pagina. A video (`F8`) o in PDF stampabile.

**Paginazione vera.** 55 righe per pagina, dialoghi spezzati con `(MORE)` e `(CONT'D)`,
intestazioni di scena mai lasciate orfane a fondo pagina. Il numero di pagine nella barra di
stato è quello che esce dal PDF.

**Backup.** A ogni salvataggio la versione precedente finisce in "Versioni DraftLite" accanto al
file, datata; restano le ultime dieci. Più il salvataggio automatico ogni due minuti.

## Formati

| Formato | Apri | Salva | Note |
|---|:--:|:--:|---|
| `.dlite` | ✔ | ✔ | Nativo. JSON leggibile, ottimo con git. L'unico che conserva tutto, colori compresi |
| `.fountain` `.spmd` | ✔ | ✔ | Standard aperto: note, sinossi, numeri di scena, dual dialogue e stili del testo |
| `.fdx` `.fdxt` | ✔ | ✔ | Final Draft, anche i modelli. Con ScriptNote, SceneProperties e DualDialogue |
| `.docx` `.rtf` `.txt` | ✔ | — | Import: gli elementi si riconoscono dai rientri, o dalle maiuscole se non ce ne sono |
| `.pdf` | — | ✔ | Courier 12, formato standard di consegna |

Se apri un `.fountain`, `Ctrl+S` continua a salvare in Fountain: puoi tenere il copione
versionato in un repo senza pensarci. Colori delle schede e stato della revisione vivono solo
nel `.dlite`.

Quando incolli da Word o dal browser, il testo viene riclassificato invece di portarsi dietro i
rientri altrui — che qui sono l'unica cosa che distingue un dialogo da un'azione.

## Scorciatoie

```
Invio            elemento successivo + menù      Ctrl+1   Scena
Shift+Invio      stesso elemento, riga nuova     Ctrl+2   Azione
Tab              cambia tipo elemento            Ctrl+3   Personaggio
Shift+Tab        tipo precedente                 Ctrl+4   Parentetica
Tab su un nome   apre la parentetica             Ctrl+5   Dialogo
Invio su vuoto   torna ad Azione                 Ctrl+6   Transizione

Ctrl+B grassetto   Ctrl+I corsivo   Ctrl+U sottolineato   Ctrl+D dialogo simultaneo
Shift+F3 MAIUSCOLO / minuscolo / Iniziali
Colore del testo ed evidenziatore: menù Formato, o i due pulsanti colorati in barra

Ctrl+N nuovo   Ctrl+O apri   Ctrl+S salva   Ctrl+P esporta PDF
Ctrl+F trova   Ctrl+G vai a scena o pagina   Ctrl+A tutto   Ctrl+Shift+A la scena
Ctrl+Shift+N nuova scena   Ctrl+Invio interruzione di pagina

F4 elenco personaggi   F6 schede scena   F7 frontespizio   F8 statistiche
F9 pannello laterale   F11 macchina da scrivere   Ctrl+M nota
Ctrl+ +/- zoom   Ctrl+0 zoom 100%   F1 aiuto
```

Scrivendo `INT.` o `EST.` all'inizio di un'azione, la riga diventa da sola un'intestazione di
scena. Le transizioni italiane (`DISSOLVENZA A:`, `STACCO SU:`, `FINE`) sono riconosciute e
allineate a destra.

## Installazione

Dall'ultima [release](../../releases) scarichi quello che ti serve:

**`DraftLite-Setup-x.y.z.exe`** — installazione normale. Mette il programma in Programmi, il
collegamento nel menu Start (e sul desktop se lo spunti) e associa i file `.dlite`: doppio clic
su un copione e si apre, con la sua icona. Durante l'installazione puoi associare anche i
`.fountain` e — se sai quello che fai — i `.fdx`, che però così li togli a Final Draft. Si
disinstalla dal Pannello di controllo, e alla disinstallazione ti chiede se buttare via anche le
preferenze.

**`DraftLite-x.y.z-portabile.exe`** — un file solo, ci clicchi sopra e scrive. Niente
installazione, niente registro: buono per la chiavetta o per provarlo senza impegno.

In tutti e due i casi serve Windows 10 o successivo a 64 bit, e **non serve installare .NET**:
è già dentro l'eseguibile. Preferenze e salvataggio automatico stanno in `%APPDATA%\DraftLite`,
quindi passando dal portabile all'installato ritrovi tutto.

### Aggiornamenti

All'avvio (una volta al giorno, non a ogni apertura) DraftLite chiede a GitHub se c'è una
versione nuova, e parla solo se c'è: niente download automatici, niente dati inviati, e se la
rete non c'è non te ne accorgi nemmeno. Lo spegni dal menù `?` › *Controlla all'avvio*, e da lì
puoi anche cercare gli aggiornamenti a mano quando ti va.

## Compilare

```bash
dotnet build src/DraftLite/DraftLite.csproj -c Release

# eseguibile singolo autocontenuto (quello della release)
dotnet publish src/DraftLite/DraftLite.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish
```

Il progetto ha `EnableWindowsTargeting`, quindi compila (non esegue) anche da Linux o macOS.

L'installer si costruisce con [Inno Setup 6](https://jrsoftware.org/isinfo.php), dopo il publish:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.6.0 installer\DraftLite.iss
# esce in installer\Output\DraftLite-Setup-1.6.0.exe
```

La build ufficiale la fa GitHub Actions: ogni push su `main` produce gli artifact (installer e
portabile), un tag `v*` pubblica la release. Inno Setup se non c'è sul runner viene installato
dal workflow.

```bash
git tag v1.6.0 && git push origin v1.6.0
```

I file della release hanno sempre lo stesso nome, quindi questi due link valgono per sempre e
puntano da soli all'ultima versione:

```
https://github.com/Giuseppe-TD/DraftLite/releases/latest/download/DraftLite-Setup.exe
https://github.com/Giuseppe-TD/DraftLite/releases/latest/download/DraftLite-Portabile.exe
```

## Come è fatto

C# 12 / .NET 8, WinForms, **nessuna dipendenza NuGet** — nemmeno per il PDF, generato a mano con
i font Courier standard del formato, né per il `.docx`, che è uno zip di XML.

```
src/DraftLite/
  Model/      ElementType, ElementStyle (metriche tipografiche), ScreenElement + ElementMeta,
              StyledText (grassetto/corsivo inline), Screenplay (numerazione, sides),
              Revision, TitlePage
  Editor/     ScreenplayEditor (RichTextBox + PARAFORMAT2), NativeMethods
  IO/         DraftLiteFile, FountainIO, FdxIO, TextImporter (docx/rtf/txt),
              Paginator, ScreenplayStats, PdfBuilder, PdfExporter
  UI/         MainForm, Ribbon (barra multifunzione disegnata a mano), RibbonIcons,
              RulerStrip, CardsForm, ReportForm, AppearanceForm, SmallDialogs,
              TitlePageForm, FindForm, UpdateChecker, Theme, Icons, AppSettings
installer/    DraftLite.iss (script Inno Setup), copione.ico (icona dei file)
```

Due scelte che spiegano il resto:

- **Il tipo di ogni paragrafo non è tenuto in una struttura parallela**, è codificato nella
  formattazione del paragrafo stesso (rientro + spazio prima + allineamento). Così resta
  corretto anche dopo copia, incolla, annulla e ripristina, senza niente da sincronizzare.
- **I dati che nel testo non ci stanno** (note, sinossi, colori, numeri di scena) vivono in una
  lista parallela riallineata a ogni modifica confrontando prefisso e suffisso del documento;
  quello che sparisce resta in panchina e torna al suo posto se la riga ricompare.
- **La barra multifunzione è disegnata a mano**, pulsante per pulsante, e anche le icone: sono
  vettori tracciati a runtime dentro una griglia 32×32 e poi scalati. Costa qualche riga in più,
  ma non c'è nessun file da distribuire, niente si sgrana sui monitor ad alta densità e
  l'inchiostro segue il tema invece di restare nero su fondo scuro.

---

Tastiere Digitali srls
