using Stanley.ProjectModel.Backgrounds;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Poses;
using Stanley.ProjectModel.Props;
using Stanley.ProjectModel.Storage;

namespace Stanley.ProjectModel.Tests;

public class ProjectRepositoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("stanley-repo-tests").FullName;

    [Fact]
    public void Initialize_creates_the_manifest_top_level_folders_and_gitattributes()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        Assert.True(File.Exists(Path.Combine(_root, "stanley.json")));
        Assert.True(File.Exists(Path.Combine(_root, ".gitattributes")));
        foreach (var folder in new[] { "characters", "poses", "props", "backgrounds", "issues" })
            Assert.True(Directory.Exists(Path.Combine(_root, folder)), $"expected {folder}/ to exist");

        var manifest = repository.LoadManifest();
        Assert.Equal("My Comic", manifest.Title);
        Assert.Empty(manifest.IssueIds);
    }

    [Fact]
    public void A_character_with_its_stickers_art_tiles_and_looks_round_trips_and_lands_on_the_documented_layout()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var characterId = CharacterId.New();
        var (jacketId, hairId) = (StickerId.New(), StickerId.New());
        var jacket = new Sticker(jacketId, "Jacket A", StickerSlots.Outer,
            [new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("outer", 0, 0.9)), new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("outer", 0, 1, Ease: 0.02))],
            new SortedDictionary<string, ColorValue> { ["outer"] = ColorValue.FromHex("#335577") }, ["default"], Source: "library:outer/jacket");
        var hair = new Sticker(hairId, "Bob", StickerSlots.Hair,
            [new StickerPart("back", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp), Depth: PartDepth.Back), new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue> { ["hair"] = ColorValue.FromHex("#5a3a22") }, ["default"]);
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><!-- kept verbatim --></svg>\n";
        var wardrobe = new Wardrobe(
            new Dictionary<StickerId, StickerAsset>
            {
                [jacketId] = new StickerAsset(jacket, new Dictionary<string, ArtFile>()),
                [hairId] = new StickerAsset(hair, new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(svg), ["variants/default/profile.png"] = ArtFile.Png([1, 2, 3]) }),
            },
            new Dictionary<string, ArtFile> { ["tartan.svg"] = ArtFile.Svg(svg) });
        var revisionId = CharacterRevisionId.New();
        var revision = new CharacterRevision(revisionId, characterId, "Winter",
            new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Outer] = [jacketId] },
            new SortedDictionary<string, ColorValue>(), ProportionOverride: null, Build: null,
            FabricValues: new SortedDictionary<string, Fabric> { ["outer"] = new(Texture: new TextureFill(TextureKind.Wool)) });
        var character = new CharacterDefinition(
            characterId,
            "Alice",
            BodyPresets.Shape(BodyPreset.Heroic),
            new Skeleton([new ViewAngleRestLayout(ViewAngle.Front, [new BoneRestPose(HumanoidBone.Hips, new Point2D(0, 0))])]),
            new SortedDictionary<string, ColorValue> { ["skin"] = ColorValue.FromHex("#f0c8a0") },
            new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Hair] = [hairId] },
            new SortedDictionary<string, Fabric> { ["hair"] = new(new PatternFill(PatternKind.Stripes, [ColorValue.FromHex("#ffffff")], Angle: 45)) })
        {
            Wardrobe = wardrobe,
            Revisions = new Dictionary<CharacterRevisionId, CharacterRevision> { [revisionId] = revision },
        };
        repository.SaveCharacter(character);

        Assert.Equivalent(character, repository.LoadCharacter(characterId), strict: true);

        var characterDir = Path.Combine(_root, "characters", $"{characterId.Value}-alice");
        Assert.True(File.Exists(Path.Combine(characterDir, "character.json")));
        Assert.DoesNotContain("wardrobe", File.ReadAllText(Path.Combine(characterDir, "character.json")), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(characterDir, "revisions", $"{revisionId.Value}-winter.json")));
        Assert.Equal(svg, File.ReadAllText(Path.Combine(characterDir, "patterns", "tartan.svg")));
        Assert.True(File.Exists(Path.Combine(characterDir, "stickers", $"{jacketId.Value}-jacket-a", "sticker.json")));
        var hairDir = Path.Combine(characterDir, "stickers", $"{hairId.Value}-bob");
        Assert.Equal(svg, File.ReadAllText(Path.Combine(hairDir, "variants", "default", "front.svg")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(hairDir, "variants", "default", "profile.png")));

        // Take the jacket, the side-view art, the tile and the look away: their files go too.
        var trimmed = character with
        {
            Wardrobe = new Wardrobe(
                new Dictionary<StickerId, StickerAsset> { [hairId] = wardrobe.Stickers[hairId] with { Files = new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(svg) } } },
                new Dictionary<string, ArtFile>()),
            Revisions = new Dictionary<CharacterRevisionId, CharacterRevision>(),
        };
        repository.SaveCharacter(trimmed);

        Assert.False(Directory.Exists(Path.Combine(characterDir, "stickers", $"{jacketId.Value}-jacket-a")));
        Assert.False(File.Exists(Path.Combine(hairDir, "variants", "default", "profile.png")));
        Assert.False(File.Exists(Path.Combine(characterDir, "patterns", "tartan.svg")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(characterDir, "revisions")));
        Assert.Equivalent(trimmed, repository.LoadCharacter(characterId), strict: true);
    }

    [Fact]
    public void An_object_group_round_trips_and_lands_on_the_documented_layout()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var groupId = ObjectGroupId.New();
        var leaf = new ShapeElement(ElementId.New(), ElementLayer.Background,
            [new ShapeAnchor(new Point2D(0, 0), new Point2D(0, 0), new Point2D(0, 0), AnchorHandleKind.Corner),
             new ShapeAnchor(new Point2D(10, 0), new Point2D(10, 0), new Point2D(10, 0), AnchorHandleKind.Corner),
             new ShapeAnchor(new Point2D(10, 10), new Point2D(10, 10), new Point2D(10, 10), AnchorHandleKind.Corner)],
            Closed: true, new ShapeStyle(null, ColorValue.FromHex("#335577"), 0));
        var picture = new PictureElement(ElementId.New(), ElementLayer.Background, new Rect2D(0, 0, 10, 10), "nose.svg");
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><!-- nose --></svg>\n";
        var group = new ObjectGroup(groupId, "Rocket ship", [leaf, picture])
        {
            ArtFiles = new Dictionary<string, ArtFile> { ["nose.svg"] = ArtFile.Svg(svg) }
        };
        repository.SaveObjectGroup(group);

        Assert.Equivalent(group, repository.LoadObjectGroup(groupId), strict: true);
        Assert.Contains(repository.ListObjectGroups(), g => g.Id == groupId);

        var groupDir = Path.Combine(_root, "objects", $"{groupId.Value}-rocket-ship");
        Assert.True(File.Exists(Path.Combine(groupDir, "group.json")));
        Assert.DoesNotContain("myAssetsVersion", File.ReadAllText(Path.Combine(groupDir, "group.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(svg, File.ReadAllText(Path.Combine(groupDir, "art", "nose.svg")));

        // Take the picture (and its art) away: the file goes too.
        var trimmed = group with { Children = [leaf], ArtFiles = new Dictionary<string, ArtFile>() };
        repository.SaveObjectGroup(trimmed);

        Assert.False(File.Exists(Path.Combine(groupDir, "art", "nose.svg")));
        Assert.Equivalent(trimmed, repository.LoadObjectGroup(groupId), strict: true);

        repository.DeleteObjectGroup(groupId);
        Assert.False(Directory.Exists(groupDir));
    }

    [Fact]
    public void A_character_file_from_before_stickers_loads_with_nothing_worn()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var id = CharacterId.New();
        var dir = Path.Combine(_root, "characters", $"{id.Value}-old");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "character.json"),
            "{\"colorSlots\":{\"skin\":\"#f2c9a4\"},\"id\":\"" + id.Value + "\",\"name\":\"Old\",\"skeleton\":{\"restLayouts\":[]},\"stickerSlots\":{}}");

        var loaded = repository.LoadCharacter(id);

        Assert.Empty(loaded.Stickers);
        Assert.Equal(BodyShape.Default, loaded.Body);
        Assert.Empty(loaded.Wardrobe.Stickers);
    }

    [Fact]
    public void Saving_again_after_a_rename_keeps_the_original_folder_slug()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var characterId = CharacterId.New();
        var skeleton = new Skeleton([]);

        repository.SaveCharacter(new CharacterDefinition(characterId, "Alice", BodyShape.Default, skeleton, [], []));
        var originalDir = Path.Combine(_root, "characters", $"{characterId.Value}-alice");
        Assert.True(Directory.Exists(originalDir));

        repository.SaveCharacter(new CharacterDefinition(characterId, "Alicia", BodyShape.Default, skeleton, [], []));

        Assert.True(Directory.Exists(originalDir), "renaming should not move the entity's folder");
        Assert.Equal("Alicia", repository.LoadCharacter(characterId).Name);
    }

    [Fact]
    public void Pose_prop_and_background_round_trip()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var pose = new Pose(
            PoseId.New(),
            "Wave",
            new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.RightUpperArm, 45)], new SortedDictionary<string, string> { ["eyes"] = "neutral" }));
        repository.SavePose(pose);
        Assert.Equivalent(pose, repository.LoadPose(pose.Id), strict: true);

        var prop = new Prop(PropId.New(), "Round Table", new Point2D(50, 50), ["default"]);
        repository.SaveProp(prop);
        Assert.Equivalent(prop, repository.LoadProp(prop.Id), strict: true);

        var backdropId = BackdropId.New();
        var background = new Background(BackgroundId.New(), "Alice's Apartment", [new BackdropAsset(backdropId, "Wall and Floor")]);
        repository.SaveBackground(background);
        Assert.Equivalent(background, repository.LoadBackground(background.Id), strict: true);

        var revision = new BackgroundRevision(
            BackgroundRevisionId.New(),
            background.Id,
            "Before Renovation",
            new BackgroundLayer(backdropId, [new PropPlacement(PropPlacementId.New(), prop.Id, "default", new Point2D(10, 10), new Point2D(1, 1), 0)]),
            Front: null);
        repository.SaveBackgroundRevision(revision);
        Assert.Equivalent(revision, repository.LoadBackgroundRevision(background.Id, revision.Id), strict: true);
    }

    [Fact]
    public void Issue_page_and_panel_round_trip_and_panel_file_has_no_slug()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));

        var characterId = CharacterId.New();
        var revisionId = CharacterRevisionId.New();
        var pageId = PageId.New();
        var panelId = PanelId.New();

        var issue = new Issue(
            IssueId.New(),
            "1",
            "Issue One",
            [pageId],
            new SortedDictionary<CharacterId, CharacterRevisionId> { [characterId] = revisionId });
        repository.SaveIssue(issue);

        var page = new Page(pageId, "Splash", TrimOverride: null, [panelId]);
        repository.SavePage(issue.Id, page);

        var panel = new Panel(
            panelId,
            new PanelShape([new ShapeAnchor(new Point2D(0, 0), new Point2D(0, 0), new Point2D(10, 0), AnchorHandleKind.Corner)]),
            new InlineBackground("splash.png"),
            [new CharacterInstance(characterId, new CharacterPlacement(new Point2D(60, 250), 120, Mirrored: true), RevisionOverride: null, new PoseData(ViewAngle.Front, [], []), Overrides: null)],
            [new Bubble(
                BubbleId.New(),
                BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, new Rect2D(20, 20, 120, 60)),
                BubbleStylePreset.Speech,
                [new BubbleTail(0.75, new Point2D(10, 120), TailKind.SmoothTriangle)],
                "Hello!")]);
        repository.SavePanel(issue.Id, page.Id, panel);

        Assert.Equivalent(issue, repository.LoadIssue(issue.Id), strict: true);
        Assert.Equivalent(page, repository.LoadPage(issue.Id, page.Id), strict: true);
        Assert.Equivalent(panel, repository.LoadPanel(issue.Id, page.Id, panel.Id), strict: true);

        var panelsDir = Path.Combine(_root, "issues", $"{issue.Id.Value}-1-issue-one", "pages", $"{pageId.Value}-splash", "panels");
        Assert.True(File.Exists(Path.Combine(panelsDir, $"{panelId.Value}.json")));
    }

    [Fact]
    public void Issue_page_numbering_round_trips_and_is_absent_when_unset()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        var plain = new Issue(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());
        var numbered = new Issue(IssueId.New(), "2", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>(),
            new PageNumbering(PageNumberPosition.BottomOuter, StartAt: 5, NumberFirstPage: true));

        repository.SaveIssue(plain);
        repository.SaveIssue(numbered);

        Assert.Null(repository.LoadIssue(plain.Id).PageNumbering);
        Assert.Equal(numbered.PageNumbering, repository.LoadIssue(numbered.Id).PageNumbering);
        var plainJson = File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "issues"), "issue.json", SearchOption.AllDirectories)
            .Single(f => f.Contains(plain.Id.Value, StringComparison.Ordinal)));
        Assert.DoesNotContain("pageNumbering", plainJson, StringComparison.Ordinal);
        var numberedJson = File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "issues"), "issue.json", SearchOption.AllDirectories)
            .Single(f => f.Contains(numbered.Id.Value, StringComparison.Ordinal)));
        Assert.Contains("\"bottomOuter\"", numberedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_an_unknown_id_throws()
    {
        var repository = ProjectRepository.Initialize(_root, "My Comic", new PageTrim(new PageSize(210, 297), 3));
        Assert.Throws<DirectoryNotFoundException>(() => repository.LoadCharacter(CharacterId.New()));
        Assert.Throws<FileNotFoundException>(() => repository.LoadPose(PoseId.New()));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
