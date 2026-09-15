using DraftLite.IO;
using DraftLite.Model;
using System.Globalization;

var folder = Path.Combine(Path.GetTempPath(), "DraftLite-tests-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Near(double expected, double actual, string name) => Check(Math.Abs(expected - actual) < 0.001, name);
Screenplay Read(string xml)
{
    var path = Path.Combine(folder, "source.fdx");
    File.WriteAllText(path, xml);
    return FdxIO.Load(path);
}
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
    var sp = Read("""
        <FinalDraft>
          <Content><Paragraph Type="Action"><Text>Una stanza.</Text></Paragraph></Content>
          <PageLayout TopMargin="90" BottomMargin="54"><PageSize Width="8.5" Height="11"/></PageLayout>
          <ElementSettings Type="Action"><ParagraphSpec LeftIndent="1.25" RightIndent="7.25"/></ElementSettings>
          <ElementSettings Type="Character"><ParagraphSpec LeftIndent="3.75" RightIndent="7.25"/></ElementSettings>
          <ElementSettings Type="Dialogue"><ParagraphSpec LeftIndent="2.56" RightIndent="6.25"/></ElementSettings>
          <ElementSettings Type="Transition"><ParagraphSpec LeftIndent="5.25" RightIndent="6.75"/></ElementSettings>
        </FinalDraft>
        """);
    Near(8.5, sp.Layout.PaperWidth, "Larghezza pagina in pollici");
    Near(1.25, sp.Layout.TopMargin, "Margine verticale in punti convertito");
    Near(6, sp.Layout.TextWidth, "Area scrivibile dal modello");
    Near(1.31, sp.Layout.Style(ElementType.Dialogue).LeftInch, "Rientro dialogo relativo al testo");
    Near(3.69, sp.Layout.Style(ElementType.Dialogue).WidthInch, "RightIndent come coordinata");
    Near(0, sp.Layout.Style(ElementType.Character).RightTwips, "Rientro destro personaggio");
    var fdx = Path.Combine(folder, "roundtrip.fdx");
    FdxIO.Save(fdx, sp);
    var round = FdxIO.Load(fdx);
    Near(3.69, round.Layout.Style(ElementType.Dialogue).WidthInch, "Round trip FDX con cultura italiana");
    var native = Path.Combine(folder, "roundtrip.dlite");
    DraftLiteFile.Save(native, sp);
    Near(1.25, DraftLiteFile.Load(native).Layout.LeftMargin, "Round trip nativo");
    var clone = sp.Clone();
    clone.Layout.Paragraphs[ElementType.Action].Left = 2;
    Near(1.25, sp.Layout.Paragraphs[ElementType.Action].Left, "Clone indipendente");
    File.WriteAllText(native, "{\"Version\":2,\"Elements\":[]}");
    Check(DraftLiteFile.Load(native).Layout == null, "Compatibilita documenti versione 2");
    var plain = Read("<FinalDraft><Content/></FinalDraft>");
    Near(6, plain.Layout.TextWidth, "FDX senza impostazioni");
    Check(plain.Layout.Paragraphs.Count == 0, "Nessuna contaminazione fra documenti");
    var malformed = Read("""
        <FinalDraft><PageLayout TopMargin="NaN"><PageSize Width="Infinity" Height="-1"/></PageLayout>
        <ElementSettings Type="Action"><ParagraphSpec LeftIndent="7" RightIndent="2"/></ElementSettings></FinalDraft>
        """);
    Near(8.5, malformed.Layout.PaperWidth, "Fallback misure non valide");
    Check(malformed.Layout.Paragraphs.Count == 0, "Rifiuto intervalli invertiti");
    var a4 = Read("""
        <FinalDraft xmlns="urn:test"><PageLayout><PageSize Width="8.2677" Height="11.6929"/></PageLayout>
        <ElementSettings Type="Action"><ParagraphSpec LeftIndent="1" RightIndent="-1"/></ElementSettings></FinalDraft>
        """);
    Near(6.2677, a4.Layout.TextWidth, "A4, namespace e coordinata destra negativa");
    sp.Elements = new() { new ScreenElement(ElementType.Action, string.Join(" ", Enumerable.Repeat("parola", 40))) };
    var wide = Paginator.Paginate(sp, PageSetup.Letter()).Sum(p => p.Lines.Count);
    sp.Layout.Paragraphs[ElementType.Action].Right = 4;
    var narrow = Paginator.Paginate(sp, PageSetup.Letter()).Sum(p => p.Lines.Count);
    Check(narrow > wide, "Paginazione usa larghezza importata");
    Console.WriteLine($"{checks} verifiche superate.");
}
finally { Directory.Delete(folder, true); }
