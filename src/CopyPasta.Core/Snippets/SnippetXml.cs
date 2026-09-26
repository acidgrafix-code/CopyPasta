using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CopyPasta.Core.Snippets;

/// <summary>
/// Reads and writes the snippet interchange format.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately byte-compatible with macOS Clipy, which is the one place the two ports genuinely
/// meet: a snippet library exported on a Mac imports here and vice versa. The shape is fixed by
/// the original and must not drift.
/// </para>
/// <code>
/// &lt;folders&gt;
///   &lt;folder&gt;
///     &lt;title&gt;Signatures&lt;/title&gt;
///     &lt;snippets&gt;
///       &lt;snippet&gt;
///         &lt;title&gt;Formal&lt;/title&gt;
///         &lt;content&gt;Kind regards,&lt;/content&gt;
///       &lt;/snippet&gt;
///     &lt;/snippets&gt;
///   &lt;/folder&gt;
/// &lt;/folders&gt;
/// </code>
/// <para>
/// Note what the format does <em>not</em> carry: the enabled flag. macOS omits it, so an imported
/// folder or snippet always arrives enabled. Preserving it would make files the Mac cannot read.
/// </para>
/// </remarks>
public static class SnippetXml
{
    private const string RootElement = "folders";
    private const string FolderElement = "folder";
    private const string SnippetsElement = "snippets";
    private const string SnippetElement = "snippet";
    private const string TitleElement = "title";
    private const string ContentElement = "content";

    /// <summary>Serialises folders to the interchange format.</summary>
    public static string Export(IEnumerable<SnippetFolderDetail> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        XElement root = new(RootElement);

        foreach (SnippetFolderDetail detail in folders)
        {
            XElement folder = new(FolderElement, new XElement(TitleElement, detail.Folder.Title));
            XElement snippets = new(SnippetsElement);

            foreach (Snippet snippet in detail.Snippets)
            {
                snippets.Add(new XElement(
                    SnippetElement,
                    new XElement(TitleElement, snippet.Title),
                    new XElement(ContentElement, snippet.Content)));
            }

            folder.Add(snippets);
            root.Add(folder);
        }

        XmlWriterSettings settings = new()
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),

            // Carriage returns are written as &#13; rather than literally. XML parsers are
            // required to normalise a literal CRLF to LF on read, so without this a snippet
            // containing Windows line endings would silently come back with Unix ones. macOS
            // does not do this and loses CRs on its own round trip; entitising is strictly safer
            // and still reads correctly in any conformant parser, AEXML included.
            NewLineHandling = NewLineHandling.Entitize,
        };

        using Utf8StringWriter output = new();
        using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            new XDocument(root).Save(writer);
        }

        return output.ToString();
    }

    /// <summary>
    /// A <see cref="StringWriter"/> that reports UTF-8, so the XML declaration matches the bytes
    /// the file is written as.
    /// </summary>
    /// <remarks>
    /// Not a detail. <c>XmlWriter</c> takes the declared encoding from the writer it is given, and
    /// <see cref="StringWriter"/> reports UTF-16 — so the document would announce
    /// <c>encoding="utf-16"</c> while being saved as UTF-8 bytes. A parser trusting the declaration
    /// then mis-decodes the file, and macOS Clipy would fail to import anything non-ASCII.
    /// </remarks>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding { get; } =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    /// <summary>
    /// Parses the interchange format. Throws <see cref="XmlException"/> for malformed XML.
    /// </summary>
    /// <remarks>
    /// Tolerant in the same places macOS is. It takes every child of the root as a folder without
    /// checking the element name, and substitutes the default titles for missing or empty ones, so
    /// a hand-edited or slightly-off file still imports rather than being rejected wholesale.
    /// </remarks>
    public static IReadOnlyList<ImportedFolder> Import(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        // PreserveWhitespace matters: a snippet's leading indentation or trailing newline is part
        // of its content. macOS sets shouldTrimWhitespace = false for the same reason.
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

        XElement? root = document.Root;
        if (root is null)
        {
            return [];
        }

        List<ImportedFolder> folders = [];

        foreach (XElement folderElement in root.Elements())
        {
            folders.Add(new ImportedFolder(
                TextOr(folderElement.Element(TitleElement), SnippetFolder.DefaultTitle),
                ReadSnippets(folderElement)));
        }

        return folders;
    }

    /// <summary>Parses without throwing, for import from a file the user chose.</summary>
    public static bool TryImport(
        string xml,
        out IReadOnlyList<ImportedFolder> folders,
        out string? error)
    {
        try
        {
            folders = Import(xml);
            error = null;
            return true;
        }
        catch (XmlException exception)
        {
            folders = [];
            error = exception.Message;
            return false;
        }
    }

    private static IReadOnlyList<ImportedSnippet> ReadSnippets(XElement folderElement)
    {
        // The first <snippets> child only, matching macOS's subscript access.
        XElement? container = folderElement.Element(SnippetsElement);
        if (container is null)
        {
            return [];
        }

        List<ImportedSnippet> snippets = [];

        foreach (XElement snippetElement in container.Elements(SnippetElement))
        {
            snippets.Add(new ImportedSnippet(
                TextOr(snippetElement.Element(TitleElement), Snippet.DefaultTitle),
                TextOr(snippetElement.Element(ContentElement), string.Empty)));
        }

        return snippets;
    }

    /// <summary>
    /// An element's text, or the fallback when it is absent or empty.
    /// </summary>
    /// <remarks>
    /// Empty counts as absent because AEXML's <c>value</c> is nil for an empty element, which is
    /// what drives macOS to its default titles. Matching that keeps the two ports in step.
    /// </remarks>
    private static string TextOr(XElement? element, string fallback)
    {
        if (element is null)
        {
            return fallback;
        }

        string value = element.Value;
        return value.Length == 0 ? fallback : value;
    }
}
