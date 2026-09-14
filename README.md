# DraftLite

Scrittura di sceneggiature per Windows: la parte utile di Final Draft, senza le altre mille.

Un editor che sa cos'è una sceneggiatura — margini, maiuscole, rientri, paginazione — e che
sta zitto su tutto il resto. Niente revisioni colorate, niente schede indice, niente licenze.

![DraftLite](docs/icon.png)

---

## Cosa fa

**Formattazione automatica.** Sei elementi (Scena, Azione, Personaggio, Parentetica, Dialogo,
Transizione). `Invio` passa all'elemento che viene dopo per logica — dopo il nome del
personaggio c'è il dialogo, dopo il dialogo c'è l'azione — `Tab` cambia tipo. Rientri e
maiuscole li mette lui.

**Suggerimenti mentre scrivi.** I nomi dei personaggi già usati, le intestazioni di scena già
scritte, i momenti della giornata dopo il trattino. Frecce per scegliere, `Invio` o `Tab` per
confermare, `Esc` per ignorare.

**Pannello scene.** Tutte le scene numerate in colonna: clic per saltarci, trascina per
spostare l'intera scena (intestazione e contenuto) da un'altra parte del copione.

**Frontespizio e statistiche.** Titolo, autore, contatti. E il conto di pagine, durata stimata,
scene per interni/esterni, battute e parole per personaggio.

**Paginazione vera.** 55 righe per pagina, dialoghi spezzati con `(MORE)` e `(CONT'D)`,
intestazioni di scena mai lasciate orfane a fondo pagina. Il numero di pagine nella barra di
stato è quello che esce dal PDF.

## Formati

| Formato | Apri | Salva | Note |
|---|:--:|:--:|---|
| `.dlite` | ✔ | ✔ | Nativo. JSON leggibile, ottimo con git |
| `.fountain` `.spmd` `.txt` | ✔ | ✔ | Standard aperto, testo puro |
| `.fdx` | ✔ | ✔ | Final Draft |
| `.pdf` | — | ✔ | Courier 12, formato standard di consegna |

Se apri un `.fountain`, `Ctrl+S` continua a salvare in Fountain: puoi tenere il copione
versionato in un repo senza pensarci.

## Scorciatoie

```
Invio            elemento successivo             Ctrl+1   Scena
Shift+Invio      stesso elemento, riga nuova     Ctrl+2   Azione
Tab              cambia tipo elemento            Ctrl+3   Personaggio
Shift+Tab        tipo precedente                 Ctrl+4   Parentetica
Tab su un nome   apre la parentetica             Ctrl+5   Dialogo
Invio su vuoto   torna ad Azione                 Ctrl+6   Transizione

Ctrl+N nuovo   Ctrl+O apri   Ctrl+S salva   Ctrl+P esporta PDF   Ctrl+F trova
F7 frontespizio   F8 statistiche   F9 pannello scene   Ctrl+ +/- zoom   F1 aiuto
```

Scrivendo `INT.` o `EST.` all'inizio di un'azione, la riga diventa da sola un'intestazione di
scena. Le transizioni italiane (`DISSOLVENZA A:`, `STACCO SU:`, `FINE`) sono riconosciute e
allineate a destra.

## Installazione

Scarica `DraftLite.exe` dall'ultima [release](../../releases) e lancialo. È un eseguibile
singolo, autocontenuto: non serve installare .NET, non scrive nel registro, non chiede nulla.
Le preferenze e il salvataggio automatico stanno in `%APPDATA%\DraftLite`.

## Compilare

```bash
dotnet build src/DraftLite/DraftLite.csproj -c Release

# eseguibile singolo autocontenuto (quello della release)
dotnet publish src/DraftLite/DraftLite.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish
```

Il progetto ha `EnableWindowsTargeting`, quindi compila (non esegue) anche da Linux o macOS.

La build ufficiale la fa GitHub Actions: ogni push su `main` produce l'artifact, un tag `v*`
pubblica la release.

```bash
git tag v1.0.0 && git push origin v1.0.0
```

## Come è fatto

C# 12 / .NET 8, WinForms, nessuna dipendenza NuGet — nemmeno per il PDF, che è generato a
mano con i font Courier standard del formato.

```
src/DraftLite/
  Model/      ElementType, ElementStyle (metriche tipografiche), ScreenElement, Screenplay
  Editor/     ScreenplayEditor (RichTextBox + PARAFORMAT2), NativeMethods
  IO/         DraftLiteFile, FountainIO, FdxIO, Paginator, PdfBuilder, PdfExporter
  UI/         MainForm, TitlePageForm, ReportForm, FindForm, AppSettings
```

Il tipo di ogni paragrafo non è tenuto in una struttura parallela ma codificato nella
formattazione del paragrafo stesso (rientro + spazio prima + allineamento): così resta
corretto anche dopo copia/incolla, annulla e ripristina, senza sincronizzazioni da mantenere.

---

Tastiere Digitali srls
