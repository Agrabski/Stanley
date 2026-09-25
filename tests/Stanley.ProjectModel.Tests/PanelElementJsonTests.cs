using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

public class PanelElementJsonTests
{
    private static ShapeAnchor Corner(double x, double y) => new(new Point2D(x, y), new Point2D(x, y), new Point2D(x, y), AnchorHandleKind.Corner);

    private static Panel PanelWith(PanelBackground? background, params PanelElement[] elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 100, 80)), background, [], [], elements);

    [Fact]
    public void Shapes_and_text_round_trip_with_their_kind_and_layer()
    {
        var shape = new ShapeElement(ElementId.New(), ElementLayer.Background, [Corner(20, 20), Corner(60, 30), Corner(40, 70)], Closed: true,
            new ShapeStyle(ColorValue.FromHex("#1c1c1c"), ColorValue.FromHex("#27ae60"), 0.7));
        var line = new ShapeElement(ElementId.New(), ElementLayer.Foreground, [Corner(20, 20), Corner(80, 20)], Closed: false,
            new ShapeStyle(ColorValue.FromHex("#1c1c1c"), Fill: null, 1.4));
        var text = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(12, 12, 40, 10), "Meanwhile...",
            new TextStyle(3.5, ColorValue.FromHex("#000000"), Bold: true, Align: TextAlign.Left, BoxFill: ColorValue.FromHex("#fff3b0"), BoxStroke: ColorValue.FromHex("#000000")));
        var panel = PanelWith(new GradientBackground(ColorValue.FromHex("#5dade2"), ColorValue.FromHex("#f4f4f4")), shape, line, text);

        var json = ProjectJson.Serialize(panel);
        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Contains("\"kind\": \"shape\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"text\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"gradient\"", json, StringComparison.Ordinal);
        Assert.Contains("\"layer\": \"foreground\"", json, StringComparison.Ordinal);
        Assert.Equivalent(panel, read, strict: true);
        Assert.IsType<ShapeElement>(read.Elements[0]);
        Assert.IsType<TextElement>(read.Elements[2]);
        Assert.IsType<GradientBackground>(read.Background);
    }

    [Fact]
    public void Unset_style_colours_are_left_out_of_the_file()
    {
        var text = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 30, 8), "Hi", new TextStyle(3.5, ColorValue.FromHex("#000000")));

        var json = ProjectJson.Serialize(PanelWith(null, text));

        Assert.DoesNotContain("boxFill", json, StringComparison.Ordinal);
        Assert.DoesNotContain("outline", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fontFamily", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_and_bubble_fonts_round_trip_and_older_files_read_as_the_default_font()
    {
        var text = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 30, 8), "Hi",
            new TextStyle(3.5, ColorValue.FromHex("#000000"), FontFamily: "Comic Neue"));
        var bubble = new Bubble(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(new Rect2D(20, 20, 40, 20)).Anchors), BubbleStylePreset.Speech, [], "Hey!", FontFamily: "DejaVu Serif");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 100, 80)), null, [], [bubble], [text]);

        var json = ProjectJson.Serialize(panel);
        Assert.Contains("\"fontFamily\": \"Comic Neue\"", json, StringComparison.Ordinal);
        Assert.Contains("\"fontFamily\": \"DejaVu Serif\"", json, StringComparison.Ordinal);
        Assert.Equivalent(panel, ProjectJson.Deserialize<Panel>(json), strict: true);

        var older = ProjectJson.Deserialize<Panel>(json
            .Replace("\"fontFamily\": \"Comic Neue\",", "", StringComparison.Ordinal)
            .Replace("\"fontFamily\": \"DejaVu Serif\",", "", StringComparison.Ordinal));
        Assert.Null(older.Bubbles[0].FontFamily);
        Assert.Null(((TextElement)older.Elements[0]).Style.FontFamily);
    }

    [Fact]
    public void A_colour_background_round_trips()
    {
        var panel = PanelWith(new ColorBackground(ColorValue.FromHex("#1b2a49")));

        var read = ProjectJson.Deserialize<Panel>(ProjectJson.Serialize(panel));

        Assert.Equal(new ColorBackground(ColorValue.FromHex("#1b2a49")), read.Background);
    }

    [Fact]
    public void A_panel_file_from_before_elements_reads_as_having_none()
    {
        var old = PanelWith(null) with { Elements = [] };
        var json = ProjectJson.Serialize(old).Replace("\"elements\": [],", "", StringComparison.Ordinal);
        Assert.DoesNotContain("elements", json, StringComparison.Ordinal);

        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.NotNull(read.Elements);
        Assert.Empty(read.Elements);
    }

    [Fact]
    public void A_panel_built_without_elements_has_an_empty_list()
    {
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 10, 10)), null, [], []);

        Assert.NotNull(panel.Elements);
        Assert.Empty(panel.Elements);
    }
}

public class IssueArtTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-art-tests").FullName;

    [Fact]
    public void A_picture_is_named_after_its_content()
    {
        var a = Stanley.ProjectModel.Characters.ArtFile.Png([1, 2, 3]);
        var same = Stanley.ProjectModel.Characters.ArtFile.Png([1, 2, 3]);
        var other = Stanley.ProjectModel.Characters.ArtFile.Png([1, 2, 4]);

        Assert.Equal(Storage.IssueArt.NameFor(a, ".PNG"), Storage.IssueArt.NameFor(same, "png"));
        Assert.NotEqual(Storage.IssueArt.NameFor(a, "png"), Storage.IssueArt.NameFor(other, "png"));
        Assert.Matches("^[0-9a-f]{16}\\.png$", Storage.IssueArt.NameFor(a, "png"));
        Assert.Throws<ArgumentException>(() => Storage.IssueArt.NameFor(a, "exe"));
    }

    [Theory]
    [InlineData("0123abcd.png", true)]
    [InlineData("sky.svg", true)]
    [InlineData("../escape.png", false)]
    [InlineData("sub/dir.png", false)]
    [InlineData("notes.txt", false)]
    [InlineData("", false)]
    public void Only_plain_picture_file_names_are_valid(string name, bool valid) =>
        Assert.Equal(valid, Storage.IssueArt.IsValidName(name));

    [Fact]
    public void Issue_art_is_saved_loaded_and_deleted_in_the_issues_art_folder()
    {
        var repository = Storage.ProjectRepository.Initialize(_root, "Comic", new PageTrim(new PageSize(210, 297), 3));
        var issue = new Issue(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());
        repository.SaveIssue(issue);
        var file = Stanley.ProjectModel.Characters.ArtFile.Png([9, 8, 7]);
        var name = Storage.IssueArt.NameFor(file, "png");

        repository.SaveIssueArt(issue.Id, name, file);
        var written = Directory.GetFiles(_root, name, SearchOption.AllDirectories).Single();
        Assert.Equal("art", Path.GetFileName(Path.GetDirectoryName(written)));
        Assert.True(repository.LoadIssueArt(issue.Id, name)!.SameContent(file));

        repository.DeleteIssueArt(issue.Id, name);
        Assert.Null(repository.LoadIssueArt(issue.Id, name));
        Assert.Null(repository.LoadIssueArt(issue.Id, "../stanley.json"));
        Assert.Throws<ArgumentException>(() => repository.SaveIssueArt(issue.Id, "../x.png", file));
    }

    [Fact]
    public void A_picture_element_round_trips()
    {
        var picture = new PictureElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(10, 20, 30, 15), "0123456789abcdef.png");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 100, 80)), new InlineBackground("fedcba9876543210.jpg"), [], [], [picture]);

        var json = ProjectJson.Serialize(panel);
        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Contains("\"kind\": \"picture\"", json, StringComparison.Ordinal);
        Assert.Equivalent(panel, read, strict: true);
        Assert.Equal(["fedcba9876543210.jpg", "0123456789abcdef.png"], PanelElements.ArtFileNames(read));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

public class LineStyleJsonTests
{
    [Fact]
    public void Dashes_weights_and_hollow_letters_round_trip_and_older_styles_read_as_solid()
    {
        var shape = new ShapeElement(ElementId.New(), ElementLayer.Background, [AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(9, 9))], false,
            new ShapeStyle(ColorValue.FromHex("#000000"), null, 1, LineDash.LongDashDot));
        var text = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 20, 5), "BAM",
            new TextStyle(8, Color: null, Outline: ColorValue.FromHex("#000000"), OutlineWidthMm: 0.5, BoxStroke: ColorValue.FromHex("#c00000"), BoxStrokeWidthMm: 1, BoxDash: LineDash.RoundDot));
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 50, 50)), null, [], [], [shape, text]);

        var json = ProjectJson.Serialize(panel);
        Assert.Contains("\"dash\": \"longDashDot\"", json, StringComparison.Ordinal);
        Assert.Contains("\"boxDash\": \"roundDot\"", json, StringComparison.Ordinal);
        Assert.Equivalent(panel, ProjectJson.Deserialize<Panel>(json), strict: true);

        var older = ProjectJson.Deserialize<Panel>(json.Replace("\"dash\": \"longDashDot\",", "", StringComparison.Ordinal));
        Assert.Equal(LineDash.Solid, ((ShapeElement)older.Elements[0]).Style.Dash);
    }
}
