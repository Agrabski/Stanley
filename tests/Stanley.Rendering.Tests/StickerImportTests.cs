using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

public class StickerImportTests
{
    /// <summary>The side-view hair template with a drawing in its "front" layer - what the user saves from Inkscape.</summary>
    private static string DrawnTemplate() =>
        StickerTemplates.Export(ViewAngle.Profile, StickerSlots.Hair)
            .Replace("""<g id="front" inkscape:groupmode="layer" inkscape:label="front"/>""",
                """<g id="front" inkscape:groupmode="layer" inkscape:label="front"><path class="slot-hair" d="M-40,-1010 L60,-1010 L60,-980 L-40,-980 Z" fill="#aa5500"/></g>""");

    private static CharacterDefinition Wearing(StickerAsset asset)
    {
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [asset.Sticker.Slot] = [asset.Id] },
        };
        return character with { Wardrobe = character.Wardrobe.With(asset) };
    }

    [Fact]
    public void A_file_drawn_on_a_template_imports_with_no_questions()
    {
        var text = DrawnTemplate();

        var result = StickerImport.FromFile(StickerSlots.Top, "Mine", ArtFile.Svg(text), ViewAngle.Front);

        var asset = Assert.IsType<StickerAsset>(result.Asset);
        Assert.Equal(StickerSlots.Hair, asset.Sticker.Slot);               // the template's slot, not the gallery's
        var part = Assert.Single(asset.Sticker.Parts);                        // only the layer that has drawing
        Assert.Equal(("front", BodyRegion.Head, ArtMapping.Warp), (part.Name, part.Region, part.Art!.Mapping));
        Assert.Equal(text, asset.Files["variants/default/profile.svg"].Text); // the template's view, kept verbatim
        Assert.Equal(ColorValue.FromHex("#aa5500"), asset.Sticker.Colors["hair"]);
        Assert.Empty(result.Report);
    }

    [Fact]
    public void Any_other_file_is_pinned_centred_on_the_slot_region_and_fitted()
    {
        const string logo = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 200"><g id="Layer 1"><rect x="100" y="50" width="200" height="100" fill="#ff0000"/></g></svg>""";

        var asset = StickerImport.FromFile(StickerSlots.Accessory, "Logo", ArtFile.Svg(logo), ViewAngle.Front).Asset!;

        var part = Assert.Single(asset.Sticker.Parts);
        Assert.Equal(ParsedArt.WholeFile, part.Name);
        Assert.Equal(ArtMapping.Pin, part.Art!.Mapping);
        var drawing = ((FigureRenderer)CharacterRenderers.Default).Drawing(Wearing(asset));
        using var outline = drawing.OutlineOf(asset.Id);
        var (centre, size) = StickerImport.RegionBox(ViewAngle.Front, BodyRegion.Torso);
        Assert.Equal(centre.X / 1000, outline.TightBounds.MidX, 3);
        Assert.Equal(centre.Y / 1000, outline.TightBounds.MidY, 3);
        Assert.Equal(size / 1000, outline.TightBounds.Width, 3); // the longer side fits the region
    }

    [Fact]
    public void A_png_is_drawn_pinned_in_its_own_colours()
    {
        using var source = new SKBitmap(20, 10);
        source.Erase(new SKColor(0, 200, 0));
        using var png = SKImage.FromBitmap(source).Encode(SKEncodedImageFormat.Png, 100);
        var asset = StickerImport.FromFile(StickerSlots.Accessory, "Badge", ArtFile.Png(png.ToArray()), ViewAngle.Front).Asset!;
        var character = Wearing(asset);

        using var bitmap = new SKBitmap(400, 440);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2, ViewAngle.Front, new PoseData(ViewAngle.Front, [], []));
        }
        var (centre, _) = StickerImport.RegionBox(ViewAngle.Front, BodyRegion.Torso);
        var pixel = bitmap.GetPixel((int)(200 + centre.X / 1000 * 400), (int)(420 + centre.Y / 1000 * 400));
        Assert.True(pixel.Green > 150 && pixel.Red < 60, pixel.ToString());
        Assert.Equal(asset.Id, CharacterRenderers.Default.StickerAt(character, new CharacterPlacement(default, 1, false), new Point2D(centre.X / 1000, centre.Y / 1000)));
    }

    [Fact]
    public void A_new_version_adds_parts_for_new_layers_and_makes_the_sticker_the_user_own()
    {
        var drawn = StickerImport.NewDrawn(StickerSlots.Hair, "My hair", ViewAngle.Front);
        Assert.Equal(["back", "front"], drawn.Sticker.Parts.Select(p => p.Name));
        Assert.Contains("data-stanley-guide", drawn.Files["variants/default/front.svg"].Text);

        var library = drawn with { Sticker = drawn.Sticker with { Source = "library:hair/x" } };
        var text = StickerTemplates.Export(ViewAngle.Front, StickerSlots.Hair).Replace("</svg>",
            """<g inkscape:label="bow"><circle class="slot-accent" cx="40" cy="-1000" r="8" fill="#e91e63"/></g></svg>""");
        var result = StickerImport.WithArt(library, ViewAngle.Front, text);

        var updated = Assert.IsType<StickerAsset>(result.Asset);
        Assert.Null(updated.Sticker.Source);
        Assert.Contains(updated.Sticker.Parts, p => p.Name == "bow" && p.Region == BodyRegion.Head);
        Assert.Equal(ColorValue.FromHex("#e91e63"), updated.Sticker.Colors["accent"]);
        Assert.Equal(text, updated.Files["variants/default/front.svg"].Text);
        Assert.Null(StickerImport.WithArt(drawn, ViewAngle.Front, "<svg").Asset);
    }
}

public class LookRenderingTests
{
    [Fact]
    public void A_new_expression_starts_as_a_copy_of_one_in_every_view_and_saves_go_to_it()
    {
        var front = ArtFile.Svg(StickerTemplates.Export(ViewAngle.Front, StickerSlots.Mouth));
        var side = ArtFile.Svg(StickerTemplates.Export(ViewAngle.Profile, StickerSlots.Mouth));
        var sticker = new Sticker(StickerId.New(), "Simple", StickerSlots.Mouth, [], new SortedDictionary<string, ColorValue>(), ["neutral", "smile"], Source: "library:mouth/simple");
        var asset = new StickerAsset(sticker, new Dictionary<string, ArtFile>
        {
            ["variants/smile/front.svg"] = front,
            ["variants/smile/profile.svg"] = side,
            ["variants/neutral/front.svg"] = ArtFile.Svg("<svg/>"),
        });

        var added = StickerImport.WithVariant(asset, "myMouth", copyOf: "smile");

        Assert.Equal(["neutral", "smile", "myMouth"], added.Sticker.Variants);
        Assert.Null(added.Sticker.Source); // the user's own from now on
        Assert.Same(front, added.Files["variants/myMouth/front.svg"]);
        Assert.Same(side, added.Files["variants/myMouth/profile.svg"]);
        Assert.Equal(front.Text, StickerImport.ArtToEdit(added, ViewAngle.Front, "myMouth"));

        var redrawn = StickerTemplates.Export(ViewAngle.Profile, StickerSlots.Mouth).Replace("</svg>", "<!-- mine --></svg>");
        var saved = StickerImport.WithArt(added, ViewAngle.Profile, redrawn, "myMouth").Asset!;
        Assert.Equal(redrawn, saved.Files["variants/myMouth/profile.svg"].Text);
        Assert.Same(side, saved.Files["variants/smile/profile.svg"]); // the one it was copied from is untouched

        // A drawing saved for a variant the sticker doesn't list (its "new" was undone) brings it back.
        Assert.Equal(["neutral", "smile", "crying"], StickerImport.WithArt(asset, ViewAngle.Front, redrawn, "crying").Asset!.Sticker.Variants);
    }

    [Fact]
    public void An_instance_is_drawn_in_its_issue_look_unless_it_picks_its_own()
    {
        var sticker = new Sticker(StickerId.New(), "Top", StickerSlots.Top, [new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.9))],
            new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#0000ff") }, ["default"]);
        var character = CharacterDefinition.Create("A") with { Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [sticker.Id] } };
        var red = new CharacterRevision(CharacterRevisionId.New(), character.Id, "Red", new SortedDictionary<string, IReadOnlyList<StickerId>>(),
            new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#ff0000") }, null, null);
        character = character with
        {
            Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>())),
            Revisions = new Dictionary<CharacterRevisionId, CharacterRevision> { [red.Id] = red },
        };
        var characters = new Dictionary<CharacterId, CharacterDefinition> { [character.Id] = character };
        var issue = new Dictionary<CharacterId, CharacterRevisionId> { [character.Id] = red.Id };
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(new Point2D(200, 420), 400, false), null, new PoseData(ViewAngle.Front, [], []), null);

        SKColor Chest(CharacterInstance i)
        {
            using var bitmap = new SKBitmap(400, 440);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                CharacterRenderers.DrawInstance(canvas, i, characters, 2, issue);
            }
            return bitmap.GetPixel(200, 420 - (int)(0.7 * 400));
        }

        Assert.Equal(new SKColor(255, 0, 0), Chest(instance));
        Assert.Equal(new SKColor(0, 0, 255), Chest(instance with { RevisionOverride = CharacterLooks.DefaultLook }));
    }
}
