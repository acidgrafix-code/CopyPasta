using System.Text;
using System.Xml.Linq;
using CopyPasta.Core.Snippets;

namespace CopyPasta.Core.Tests;

/// <summary>
/// The snippet interchange format — the Phase 5 acceptance criterion. A library exported on a Mac
/// must import here, and what this writes must be readable there.
/// </summary>
public class SnippetXmlTests
{
    /// <summary>
    /// A file in exactly the shape macOS Clipy writes, including its declaration and indentation.
    /// </summary>
    private const string MacOsExport = """
        <?xml version="1.0" encoding="utf-8" standalone="no"?>
        <folders>
            <folder>
                <title>Signatures</title>
                <snippets>
                    <snippet>
                        <title>Formal</title>
                        <content>Kind regards,</content>
                    </snippet>
                    <snippet>
                        <title>Casual</title>
                        <content>Cheers,</content>
                    </snippet>
                </snippets>
            </folder>
            <folder>
                <title>Boilerplate</title>
                <snippets>
                    <snippet>
                        <title>MIT</title>
                        <content>Permission is hereby granted</content>
                    </snippet>
                </snippets>
            </folder>
        </folders>
        """;

    private static SnippetFolderDetail Folder(
        string title,
        params (string Title, string Content)[] snippets)
    {
        Guid folderId = Guid.NewGuid();

        return new SnippetFolderDetail(
            new SnippetFolder(folderId, title, 0),
            snippets
                .Select((snippet, index) => new Snippet(
                    Guid.NewGuid(),
                    folderId,
                    snippet.Title,
                    snippet.Content,
                    index))
                .ToArray());
    }

    /// <summary>Exports then re-imports, which is the shape interchange actually takes.</summary>
    private static IReadOnlyList<ImportedFolder> RoundTrip(params SnippetFolderDetail[] folders) =>
        SnippetXml.Import(SnippetXml.Export(folders));

    // ---- Reading what macOS writes -----------------------------------------------------

    [Fact]
    public void A_file_exported_from_macOS_Clipy_imports()
    {
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import(MacOsExport);

        Assert.Equal(2, folders.Count);
        Assert.Equal("Signatures", folders[0].Title);
        Assert.Equal("Boilerplate", folders[1].Title);
    }

    [Fact]
    public void Its_snippets_arrive_in_order_with_their_content()
    {
        IReadOnlyList<ImportedSnippet> snippets = SnippetXml.Import(MacOsExport)[0].Snippets;

        Assert.Equal(["Formal", "Casual"], snippets.Select(snippet => snippet.Title));
        Assert.Equal("Kind regards,", snippets[0].Content);
        Assert.Equal("Cheers,", snippets[1].Content);
    }

    // ---- Writing what macOS can read ------------------------------------------------------

    [Fact]
    public void The_exported_shape_is_the_one_macOS_expects()
    {
        string xml = SnippetXml.Export([Folder("Signatures", ("Formal", "Kind regards,"))]);
        XElement root = XDocument.Parse(xml).Root!;

        Assert.Equal("folders", root.Name.LocalName);

        XElement folder = Assert.Single(root.Elements());
        Assert.Equal("folder", folder.Name.LocalName);
        Assert.Equal("Signatures", folder.Element("title")!.Value);

        XElement snippet = Assert.Single(folder.Element("snippets")!.Elements());
        Assert.Equal("snippet", snippet.Name.LocalName);
        Assert.Equal("Formal", snippet.Element("title")!.Value);
        Assert.Equal("Kind regards,", snippet.Element("content")!.Value);
    }

    [Fact]
    public void An_empty_library_still_writes_a_valid_document()
    {
        string xml = SnippetXml.Export([]);

        Assert.Equal("folders", XDocument.Parse(xml).Root!.Name.LocalName);
        Assert.Empty(SnippetXml.Import(xml));
    }

    [Fact]
    public void A_folder_with_no_snippets_keeps_its_snippets_container()
    {
        // macOS always writes the container, and its importer reaches through it unconditionally.
        string xml = SnippetXml.Export([Folder("Empty")]);

        Assert.NotNull(XDocument.Parse(xml).Root!.Element("folder")!.Element("snippets"));
        Assert.Empty(Assert.Single(SnippetXml.Import(xml)).Snippets);
    }

    [Fact]
    public void There_is_no_byte_order_mark()
    {
        // A BOM ahead of the declaration trips some XML readers.
        Assert.NotEqual('﻿', SnippetXml.Export([Folder("A")])[0]);
    }

    [Fact]
    public void The_declaration_says_utf_8_because_that_is_how_the_file_is_written()
    {
        // Regression guard for a real bug. XmlWriter takes the declared encoding from the writer
        // it is handed, and a plain StringWriter reports UTF-16 — so the document announced
        // encoding="utf-16" while being saved as UTF-8 bytes. Anything non-ASCII then failed to
        // import on the Mac. Parsing a string ignores the declaration, so only reading the bytes
        // catches it.
        string xml = SnippetXml.Export([Folder("Café", ("S", "日本語"))]);

        Assert.Contains("encoding=\"utf-8\"", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_file_written_as_utf_8_bytes_parses_back_from_those_bytes()
    {
        // The interchange path as it actually happens: encode, write, read, decode.
        const string title = "Café 日本語";
        byte[] bytes = new UTF8Encoding(false).GetBytes(
            SnippetXml.Export([Folder(title, ("S", "😀"))]));

        using MemoryStream stream = new(bytes);
        XDocument reloaded = XDocument.Load(stream);

        Assert.Equal(title, reloaded.Root!.Element("folder")!.Element("title")!.Value);
    }

    // ---- Round trips -----------------------------------------------------------------------

    [Fact]
    public void Folders_and_snippets_survive_a_round_trip()
    {
        IReadOnlyList<ImportedFolder> folders = RoundTrip(
            Folder("One", ("A", "alpha"), ("B", "beta")),
            Folder("Two", ("C", "gamma")));

        Assert.Equal(["One", "Two"], folders.Select(folder => folder.Title));
        Assert.Equal(["A", "B"], folders[0].Snippets.Select(snippet => snippet.Title));
        Assert.Equal("gamma", folders[1].Snippets[0].Content);
    }

    [Fact]
    public void Multi_line_content_survives_a_round_trip()
    {
        const string content = "line one\nline two\n\nline four";

        Assert.Equal(content, RoundTrip(Folder("F", ("S", content)))[0].Snippets[0].Content);
    }

    [Fact]
    public void Windows_line_endings_survive_a_round_trip()
    {
        // The reason carriage returns are entitised. XML parsers are required to normalise a
        // literal CRLF to LF, so writing it raw would silently convert every snippet to Unix
        // endings — and a snippet pasted into a Windows text field would lose its formatting.
        const string content = "line one\r\nline two\r\n";

        Assert.Equal(content, RoundTrip(Folder("F", ("S", content)))[0].Snippets[0].Content);
    }

    [Fact]
    public void Leading_and_trailing_whitespace_survives_a_round_trip()
    {
        // Indentation is often the whole point of a code snippet.
        const string content = "    indented\t\ttabbed   ";

        Assert.Equal(content, RoundTrip(Folder("F", ("S", content)))[0].Snippets[0].Content);
    }

    [Fact]
    public void Xml_significant_characters_survive_a_round_trip()
    {
        const string content = "<tag attr=\"v\"> & 'quoted' </tag>";

        Assert.Equal(content, RoundTrip(Folder("R&D", ("S<>", content)))[0].Snippets[0].Content);
        Assert.Equal("R&D", RoundTrip(Folder("R&D"))[0].Title);
    }

    [Fact]
    public void Non_ASCII_text_survives_a_round_trip()
    {
        const string content = "café 日本語 😀";

        Assert.Equal(content, RoundTrip(Folder("F", ("S", content)))[0].Snippets[0].Content);
    }

    [Fact]
    public void A_long_snippet_survives_a_round_trip()
    {
        string content = string.Join("\n", Enumerable.Range(0, 2000).Select(index => $"line {index}"));

        Assert.Equal(content, RoundTrip(Folder("F", ("S", content)))[0].Snippets[0].Content);
    }

    // ---- Tolerance, matching macOS -----------------------------------------------------------

    [Fact]
    public void A_folder_with_no_title_gets_the_default_one()
    {
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import(
            "<folders><folder><snippets /></folder></folders>");

        Assert.Equal("untitled folder", Assert.Single(folders).Title);
    }

    [Fact]
    public void An_empty_title_element_counts_as_missing()
    {
        // AEXML reports an empty element's value as nil, which is what sends macOS to its default.
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import(
            "<folders><folder><title></title><snippets /></folder></folders>");

        Assert.Equal("untitled folder", Assert.Single(folders).Title);
    }

    [Fact]
    public void A_snippet_with_no_title_or_content_gets_the_defaults()
    {
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import(
            "<folders><folder><snippets><snippet /></snippets></folder></folders>");

        ImportedSnippet snippet = Assert.Single(Assert.Single(folders).Snippets);
        Assert.Equal("untitled snippet", snippet.Title);
        Assert.Equal(string.Empty, snippet.Content);
    }

    [Fact]
    public void A_folder_with_no_snippets_container_imports_as_empty()
    {
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import(
            "<folders><folder><title>Bare</title></folder></folders>");

        Assert.Empty(Assert.Single(folders).Snippets);
    }

    [Fact]
    public void Unexpected_folder_element_names_are_still_read()
    {
        // macOS takes every child of the root without checking its name; a hand-edited file
        // should not be rejected over the tag it used.
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import(
            "<folders><group><title>Odd</title></group></folders>");

        Assert.Equal("Odd", Assert.Single(folders).Title);
    }

    [Fact]
    public void Elements_that_are_not_snippets_are_ignored_inside_the_container()
    {
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import("""
            <folders><folder><title>F</title><snippets>
              <note>ignore me</note>
              <snippet><title>Kept</title><content>yes</content></snippet>
            </snippets></folder></folders>
            """);

        Assert.Equal("Kept", Assert.Single(Assert.Single(folders).Snippets).Title);
    }

    [Fact]
    public void Only_the_first_snippets_container_is_read()
    {
        // Matching the macOS subscript, which takes the first match.
        IReadOnlyList<ImportedFolder> folders = SnippetXml.Import("""
            <folders><folder><title>F</title>
              <snippets><snippet><title>First</title></snippet></snippets>
              <snippets><snippet><title>Second</title></snippet></snippets>
            </folder></folders>
            """);

        Assert.Equal("First", Assert.Single(Assert.Single(folders).Snippets).Title);
    }

    [Fact]
    public void A_document_with_a_different_root_name_is_still_read()
    {
        Assert.Single(SnippetXml.Import("<library><folder><title>A</title></folder></library>"));
    }

    // ---- Malformed input ----------------------------------------------------------------------

    [Fact]
    public void Malformed_xml_is_reported_rather_than_thrown_at_the_user()
    {
        Assert.False(SnippetXml.TryImport("<folders><folder>", out IReadOnlyList<ImportedFolder> folders, out string? error));

        Assert.Empty(folders);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void A_file_that_is_not_xml_at_all_is_reported()
    {
        Assert.False(SnippetXml.TryImport("this is not xml", out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Valid_xml_reports_no_error()
    {
        Assert.True(SnippetXml.TryImport(MacOsExport, out IReadOnlyList<ImportedFolder> folders, out string? error));

        Assert.Equal(2, folders.Count);
        Assert.Null(error);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => SnippetXml.Export(null!));
        Assert.Throws<ArgumentNullException>(() => SnippetXml.Import(null!));
    }
}
